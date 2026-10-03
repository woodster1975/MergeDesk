using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using MergeDesk.App.Services;
using MergeDesk.App.ViewModels;
using MergeDesk.Core;
using MergeDesk.Infrastructure;

internal static class ImageChecks
{
    private static int count;
    private static readonly string DataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
    public static async Task<int> RunAsync(string root)
    {
        var image = InlineImageTools.Parse(DataUri);
        var template = new MergeTemplate("{{Email}}", "", "", "Hello", $"<p>Before<img src=\"{DataUri}\" alt=\"Logo\" width=\"100\"/>After<img src=\"{DataUri}\"/></p>", "Before After", [], []);
        var data = new RecipientData(["Email"], [new(2, new() { ["Email"] = "a@example.com" })]);
        var merged = new MergeEngine().Merge(data, [new("Email", "Email")], template);
        var message = merged.Messages[0];
        Check(merged.Issues.Count == 0 && message.InlineImages?.Count == 1 && message.Html.Contains("cid:" + image.ContentId) && !message.Html.Contains("data:image"), "merge creates deduplicated inline resources with matching CID references");
        Check(InlineImageTools.PreviewHtml(message).Contains(DataUri), "preview restores embedded data without remote requests");
        Check(new MergeEngine().Merge(data, [new("Email", "Email")], template with { Html = "<img src=\"data:image/png;base64,broken\"/>" }).Issues.Any(i => i.Area == "Images"), "invalid image data blocks validation");
        try { InlineImageTools.Parse("data:image/svg+xml;base64,PHN2Zy8+"); throw new Exception("unsupported image accepted"); }
        catch (InvalidDataException) { Check(true, "unsupported SVG is rejected"); }
        var export = Path.Combine(root, "image-export"); await new DryRunMailSender(export).SendAsync(message);
        var folder = Path.Combine(export, "row-000002"); var mime = await File.ReadAllTextAsync(Directory.GetFiles(folder, "*.eml").Single());
        Check(mime.Contains("multipart/related") && mime.Contains("Content-ID: <" + image.ContentId + ">") && mime.Contains("image/png"), "dry-run MIME carries related inline image bytes");
        Check((await File.ReadAllTextAsync(Path.Combine(folder, "body.html"))).Contains(DataUri), "standalone dry-run HTML shows image locally");
        var handler = new ImageHandler(); var http = new HttpClient(handler);
        await new GraphDraftCreator(http, _ => Task.FromResult("fixture"), "owner@example.com").CreateDraftAsync(message, "image-fixture");
        using var attachment = JsonDocument.Parse(handler.Bodies[1]);
        Check(attachment.RootElement.GetProperty("isInline").GetBoolean() && attachment.RootElement.GetProperty("contentId").GetString() == image.ContentId &&
            Convert.FromBase64String(attachment.RootElement.GetProperty("contentBytes").GetString()!).SequenceEqual(image.Bytes), "Graph inline attachment preserves CID and image bytes");
        var plain = new MergedMessage(2, "a@example.com", "", "", "Hello", "<p>Hello</p>", "Hello", []);
        var legacyJson = "{\"Account\":\"owner@example.com\",\"To\":[\"a@example.com\"],\"Cc\":[],\"Bcc\":[],\"Subject\":\"Hello\",\"BodyKind\":\"HTML\",\"Body\":\"\\u003Cp\\u003EHello\\u003C/p\\u003E\",\"Files\":[]}";
        var previousHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyJson)));
        Check(await LiveSendRunner.FingerprintAsync(plain, "Classic Outlook / owner@example.com / Drafts") == previousHash, "messages without images retain prior duplicate-send identity");
        Check(await LiveSendRunner.FingerprintAsync(plain with { InlineImages = [image] }, "Classic Outlook / owner@example.com / Drafts") != previousHash, "embedded image bytes contribute to send identity");
        return count;
    }
    public static int UI(MergeDesk.App.MainWindow window)
    {
        var before = count;
        var model = (MainViewModel)window.DataContext;
        model.Mappings.Add(new("FirstName", "Name"));
        var subject = (TextBox)window.FindName("SubjectEditor");
        var picker = (ComboBox)window.FindName("SubjectFieldPicker");
        var insert = (Button)window.FindName("SubjectInsertField");
        model.Subject = "Hello friend, an update";
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        subject.Select(6, 6); picker.SelectedValue = "FirstName";
        insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(model.Subject == "Hello {{FirstName}}, an update" && subject.SelectionStart == 19, "subject inserter replaces selected text and advances cursor through MVVM binding");
        subject.Select(subject.Text.Length, 0); insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(model.Subject.EndsWith("{{FirstName}}"), "subject inserter adds field at cursor");
        var document = EmailDocumentConverter.FromHtml($"<p>Before<img src=\"{DataUri}\" alt=\"Logo\" width=\"100\"/>After</p>").Document;
        var html = EmailDocumentConverter.ToHtml(document);
        Check(html.Contains(DataUri) && html.IndexOf("Before", StringComparison.Ordinal) < html.IndexOf("<img ", StringComparison.Ordinal) && html.IndexOf("<img ", StringComparison.Ordinal) < html.IndexOf("After", StringComparison.Ordinal), "editor round trip keeps image position and portable bytes");
        Check(html.Contains("width=\"100\"") && html.Contains("alt=\"Logo\""), "image width and alternate text survive editor round trip");
        var onlyImage = EmailDocumentConverter.ToHtml(EmailDocumentConverter.FromHtml($"<p><img src=\"{DataUri}\"/></p>").Document);
        Check(onlyImage.Contains("<img "), "image-only email body is retained");
        var path = Path.Combine(Path.GetTempPath(), "MergeDesk-image-" + Guid.NewGuid().ToString("N") + ".png"); File.WriteAllBytes(path, InlineImageTools.Parse(DataUri).Bytes);
        var loaded = EmailImage.FromFile(path); File.Delete(path);
        Check(EmailImage.ToHtml(loaded).Contains(DataUri), "inserted local image remains embedded after original file is removed");
        return count - before;
    }
    private sealed class ImageHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Bodies.Add(await request.Content!.ReadAsStringAsync(token)); return new(HttpStatusCode.Created) { Content = new StringContent("{\"id\":\"image-draft\"}") }; }
    }
}
