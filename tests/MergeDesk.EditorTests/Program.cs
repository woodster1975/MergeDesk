using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MergeDesk.App.Controls;
using MergeDesk.App.Services;
using MergeDesk.App.ViewModels;
using MergeDesk.Core;
using MergeDesk.Infrastructure;
using WpfList = System.Windows.Documents.List;

internal static class Program
{
    private static int passed;
    [STAThread]
    private static int Main()
    {
        try
        {
            var application = new MergeDesk.App.App(); application.InitializeComponent();
            var initial = "<p>Hello {{FirstName}},</p><p>Your <strong>account</strong> is ready.<br>Thank you.</p>";
            var imported = EmailDocumentConverter.FromHtml(initial);
            Check(!imported.HasUnsupportedFormatting && EmailDocumentConverter.ToPlainText(imported.Document).Contains("Hello {{FirstName}}"), "legacy HTML loads into editable text");
            var document = new FlowDocument { FontFamily = new("Arial"), FontSize = 16 };
            var paragraph = new Paragraph { TextAlignment = TextAlignment.Center };
            var run = new Run("Hello {{FirstName}} & <team>") { FontFamily = new("Georgia"), FontSize = 24, FontWeight = FontWeights.Bold,
                FontStyle = FontStyles.Italic, Foreground = Brushes.Blue, TextDecorations = TextDecorations.Underline };
            paragraph.Inlines.Add(run); paragraph.Inlines.Add(new LineBreak()); paragraph.Inlines.Add(new Run("Second line")); document.Blocks.Add(paragraph);
            var html = EmailDocumentConverter.ToHtml(document);
            Check(html.Contains("Georgia") && html.Contains("font-size:18pt"), "font and point size exported");
            Check(html.Contains("font-weight:bold") && html.Contains("font-style:italic") && html.Contains("text-decoration:underline"), "bold italic underline exported");
            Check(html.Contains("color:#0000FF") && html.Contains("text-align:center"), "text colour and alignment exported");
            Check(html.Contains("&amp;") && html.Contains("&lt;team&gt;") && html.Contains("<br/>"), "typed text escaped and line break preserved");
            var reloaded = EmailDocumentConverter.FromHtml(html);
            var roundtrip = EmailDocumentConverter.ToHtml(reloaded.Document);
            Check(roundtrip.Contains("font-size:18pt") && roundtrip.Contains("Georgia") && roundtrip.Contains("font-weight:bold") && roundtrip.Contains("text-align:center"), "generated HTML formatting survives reload");
            Check(EmailDocumentConverter.ToPlainText(reloaded.Document).Contains("& <team>"), "plain text extraction decodes HTML entities");
            var whitespace = EmailDocumentConverter.FromHtml("<p><span style=\"white-space:pre-wrap\">A  B</span></p>");
            Check(EmailDocumentConverter.ToPlainText(whitespace.Document) == "A  B", "editor whitespace survives reload");
            var underline = EmailDocumentConverter.ToHtml(EmailDocumentConverter.FromHtml("<p><u>underlined <span>child</span></u></p>").Document);
            Check(underline.Contains("text-decoration:underline"), "nested underline survives conversion");
            var split = new FlowDocument(); var splitParagraph = new Paragraph();
            splitParagraph.Inlines.Add(new Run("Hello {{First") { FontWeight = FontWeights.Bold });
            splitParagraph.Inlines.Add(new Run("Name}}!") { FontStyle = FontStyles.Italic }); split.Blocks.Add(splitParagraph);
            var splitHtml = EmailDocumentConverter.ToHtml(split);
            Check(splitHtml.Contains("{{FirstName}}"), "partially formatted token remains contiguous");
            var data = new RecipientData(["FirstName", "Email"], [new(2, new() { ["FirstName"] = "Alex & Team", ["Email"] = "alex@example.com" })]);
            var batch = new MergeEngine().Merge(data, [new("FirstName", "FirstName"), new("Email", "Email")], new("{{Email}}", "", "", "Test", splitHtml, "Hello {{FirstName}}", [], []));
            Check(batch.Issues.Count == 0 && batch.Messages[0].Html.Contains("Alex &amp; Team"), "formatted field merges and escapes recipient value");
            var lists = EmailDocumentConverter.FromHtml("<ul><li>One</li><li><b>Two</b></li></ul><ol><li>Three</li></ol>");
            var listHtml = EmailDocumentConverter.ToHtml(lists.Document);
            Check(lists.Document.Blocks.OfType<WpfList>().Count() == 2 && listHtml.Contains("<ul>") && listHtml.Contains("<ol>") && listHtml.Contains("<li>"), "bullets and numbering survive roundtrip");
            var active = EmailDocumentConverter.FromHtml("<p>Safe <a href='javascript:alert(1)'>link</a></p><script>alert(1)</script><img src='https://example.com/tracker'><iframe src='https://example.com'></iframe>");
            Check(active.HasUnsupportedFormatting && !EmailDocumentConverter.ToPlainText(active.Document).Contains("alert"), "active and remote content omitted with notice");
            var activeHtml = EmailDocumentConverter.ToHtml(active.Document);
            Check(!activeHtml.Contains("javascript:") && !activeHtml.Contains("<script") && !activeHtml.Contains("<img"), "visual export omits active content");
            var link = EmailDocumentConverter.ToHtml(EmailDocumentConverter.FromHtml("<p><a href='https://example.com/path?a=1&amp;b=2'>Website</a></p>").Document);
            Check(link.Contains("href=\"https://example.com/path?a=1&amp;b=2\""), "safe links preserved with encoded attributes");
            Check(EmailDocumentConverter.ToHtml(new FlowDocument(new Paragraph())) == "", "empty visual editor stays an empty body");
            Check(EmailDocumentConverter.FromHtml("<table><tr><td>Statement</td></tr></table>").HasUnsupportedFormatting, "custom HTML layout warns before visual editing");

            var viewModel = new MainViewModel(new RecipientReader(), new SqliteRunRepository(Path.Combine(Path.GetTempPath(), "unused-editor-test.db")), new TestDialogs());
            var control = new RichEmailEditor();
            BindingOperations.SetBinding(control, RichEmailEditor.HtmlProperty, new Binding(nameof(MainViewModel.Html)) { Source = viewModel, Mode = BindingMode.TwoWay });
            Pump();
            var editor = (RichTextBox)control.FindName("Editor");
            Check(control.PlainText.Contains("[Write your message here") && !control.PlainText.Contains("{{AccountNo}}"), "generic MVVM example populates visual editor without required account field");
            editor.SelectAll(); editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold); Pump();
            Check(viewModel.Html.Contains("font-weight:bold"), "visual formatting updates bound template");
            viewModel.Html = "<p><em>Loaded project</em></p>"; Pump();
            Check(control.PlainText == "Loaded project" && control.Html == viewModel.Html, "project HTML reload updates editor without rewriting source");
            var savedHtml = viewModel.Html; Pump();
            Check(viewModel.Html == savedHtml, "selection refresh does not mutate source");
            viewModel.Html = "<p>Hello </p>"; Pump();
            control.Fields = new[] { new MappingRow("FirstName", "Name") };
            var fieldPicker = (ComboBox)control.FindName("FieldPicker"); fieldPicker.SelectedIndex = 0;
            editor.CaretPosition = editor.Document.ContentEnd;
            // Exercise the actual insert button handler without operating a desktop window.
            var insert = Descendants(control).OfType<Button>().Single(b => Equals(b.Content, "Insert merge field"));
            insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(viewModel.Html.Contains("{{FirstName}}"), "insert menu adds mapped token to bound HTML");
            string? copied = null; control.PlainTextRequested += (_, _) => copied = control.PlainText;
            var copy = Descendants(control).OfType<Button>().Single(b => Equals(b.Content, "Use email text for plain-text version"));
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(copied != null && copied.Contains("{{FirstName}}") && !copied.Contains("<p>"), "plain text button produces reusable text with tokens");
            var previewHtml = EmailDocumentConverter.ToHtml(SafeHtmlPreview.Render(html));
            Check(previewHtml.Contains("Georgia") && previewHtml.Contains("font-size:18pt") && previewHtml.Contains("color:#0000FF"), "merged preview retains authored formatting");
            var export = Path.Combine(Path.GetTempPath(), "MergeDesk-EditorExport-" + Guid.NewGuid().ToString("N"));
            new DryRunMailSender(export).SendAsync(batch.Messages[0]).GetAwaiter().GetResult();
            var exported = File.ReadAllText(Path.Combine(export, "row-000002", "body.html"));
            Check(exported.Contains("font-weight:bold") && exported.Contains("Alex &amp; Team"), "dry run carries rich formatting and merged field values");
            Console.WriteLine($"{passed} editor checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private sealed class TestDialogs : IDesktopDialogs
    {
        public string? OpenSource() => null;
        public string[] OpenAttachments() => [];
        public string? ChooseOutput() => null;
        public string? ProjectFile(bool save) => null;
    }
}
