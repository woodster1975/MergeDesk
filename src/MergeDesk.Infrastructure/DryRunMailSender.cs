using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using MergeDesk.Core;

namespace MergeDesk.Infrastructure;

/// <summary>Exports complete MIME messages locally. No network delivery is performed.</summary>
public sealed class DryRunMailSender(string outputDirectory) : IMailSender
{
    public async Task<string> SendAsync(MergedMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folder = Path.Combine(outputDirectory, $"row-{message.Row:D6}");
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "message.json"), JsonSerializer.Serialize(message, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, "body.html"), InlineImageTools.PreviewHtml(message), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, "body.txt"), message.Text, cancellationToken);
            using var mail = new MailMessage { From = new MailAddress("dry-run@example.invalid"), Subject = message.Subject,
                SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8, HeadersEncoding = Encoding.UTF8 };
            foreach (var value in MergeEngine.SplitAddresses(message.To)) mail.To.Add(value);
            foreach (var value in MergeEngine.SplitAddresses(message.Cc)) mail.CC.Add(value);
            foreach (var value in MergeEngine.SplitAddresses(message.Bcc)) mail.Bcc.Add(value);
            if (!string.IsNullOrEmpty(message.Text)) mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.Text, Encoding.UTF8, MediaTypeNames.Text.Plain));
            if (!string.IsNullOrEmpty(message.Html))
            {
                var view = AlternateView.CreateAlternateViewFromString(message.Html, Encoding.UTF8, MediaTypeNames.Text.Html);
                foreach (var image in message.InlineImages ?? [])
                    view.LinkedResources.Add(new LinkedResource(new MemoryStream(image.Bytes), image.MediaType) { ContentId = image.ContentId, TransferEncoding = TransferEncoding.Base64 });
                mail.AlternateViews.Add(view);
            }
            foreach (var file in message.Attachments) mail.Attachments.Add(new Attachment(file));
            // SpecifiedPickupDirectory writes MIME to disk; it cannot contact a mail server.
            using var client = new SmtpClient { DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                PickupDirectoryLocation = Path.GetFullPath(folder) };
            await client.SendMailAsync(mail, cancellationToken);
            return folder;
        }
        catch
        {
            // Partial output remains for diagnosis; the UI records this recipient as failed.
            throw;
        }
    }
}
