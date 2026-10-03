using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using HtmlAgilityPack;
using WpfList = System.Windows.Documents.List;

namespace MergeDesk.App.Services;

/// <summary>Converts supported email formatting to inert WPF text and back.</summary>
public static partial class EmailDocumentConverter
{
    public sealed record ImportedDocument(FlowDocument Document, bool HasUnsupportedFormatting);
    private sealed record TextStyle(string Family, double Size, bool Bold, bool Italic, bool Underline, string Color);
    private sealed record Segment(string Text, TextStyle Style, string? Link, string? ImageHtml = null);
    private static readonly HashSet<string> BlockTags = ["p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "pre", "section", "article", "tr", "table"];
    private static readonly HashSet<string> SupportedTags = ["html", "body", "p", "div", "span", "b", "strong", "i", "em", "u", "br", "ul", "ol", "li", "a", "font", "img"];
    private static readonly HashSet<string> SupportedStyles = ["font-family", "font-size", "font-weight", "font-style", "text-decoration", "color", "text-align", "margin", "white-space"];
    [GeneratedRegex(@"\{\{\s*[^{}]+?\s*\}\}")]
    private static partial Regex MergeTokens();
    [GeneratedRegex(@"\s+")]
    private static partial Regex HtmlWhitespace();

    public static ImportedDocument FromHtml(string? html)
    {
        var parsed = new HtmlDocument(); parsed.LoadHtml(html ?? "");
        var document = new FlowDocument { FontFamily = new("Arial"), FontSize = 16, Foreground = Brushes.Black, PagePadding = new(8) };
        var unsupported = parsed.DocumentNode.Descendants().Any(n => n.NodeType == HtmlNodeType.Element &&
            (!SupportedTags.Contains(n.Name) || (n.Name == "img" && !n.GetAttributeValue("src", "").StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) || n.Attributes.Any(a => a.Name is not ("style" or "href" or "face" or "color" or "size" or "src" or "width" or "height" or "alt") ||
                (a.Name == "style" && Css(n).Keys.Any(k => !SupportedStyles.Contains(k))))));
        var root = parsed.DocumentNode.SelectSingleNode("//body") ?? parsed.DocumentNode;
        if (root.Name == "body") ApplyStyles(document, root);
        ReadBlocks(root.ChildNodes, document.Blocks);
        if (document.Blocks.Count == 0) document.Blocks.Add(new Paragraph { Margin = new(0, 0, 0, 12) });
        return new(document, unsupported);
    }
    private static void ReadBlocks(IEnumerable<HtmlNode> nodes, BlockCollection blocks)
    {
        Paragraph? current = null;
        foreach (var node in nodes)
        {
            if (Ignore(node)) continue;
            if (node.Name is "ul" or "ol")
            {
                current = null;
                var list = new WpfList { MarkerStyle = node.Name == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Margin = new(0, 0, 0, 12), Padding = new(24, 0, 0, 0) };
                ApplyStyles(list, node);
                foreach (var child in node.ChildNodes.Where(n => n.Name == "li"))
                {
                    var item = new ListItem(); ApplyStyles(item, child); ReadBlocks(child.ChildNodes, item.Blocks);
                    if (item.Blocks.Count == 0) item.Blocks.Add(new Paragraph());
                    list.ListItems.Add(item);
                }
                if (list.ListItems.Count > 0) blocks.Add(list);
                continue;
            }
            if (BlockTags.Contains(node.Name) || node.Name is "html" or "body")
            {
                current = null;
                if (node.ChildNodes.Any(n => BlockTags.Contains(n.Name) || n.Name is "ul" or "ol" or "body"))
                {
                    var section = new Section(); ApplyStyles(section, node); ReadBlocks(node.ChildNodes, section.Blocks);
                    if (section.Blocks.Count > 0) blocks.Add(section);
                }
                else
                {
                    var paragraph = new Paragraph { Margin = new(0, 0, 0, 12) };
                    ApplyStyles(paragraph, node); ReadInlines(node.ChildNodes, paragraph.Inlines);
                    if (node.Name is "h1" or "h2" or "h3") { paragraph.FontWeight = FontWeights.Bold; paragraph.FontSize = node.Name == "h1" ? 32 : 24; }
                    blocks.Add(paragraph);
                }
                continue;
            }
            if (node.NodeType == HtmlNodeType.Text && string.IsNullOrWhiteSpace(WebUtility.HtmlDecode(node.InnerText)) && current == null) continue;
            if (current == null) { current = new Paragraph { Margin = new(0, 0, 0, 12) }; blocks.Add(current); }
            ReadInlines([node], current.Inlines);
        }
    }
    private static bool Ignore(HtmlNode node) => node.NodeType == HtmlNodeType.Comment || node.Name is "script" or "style" or "head" or "iframe" or "object" or "embed" or "svg";
    private static void ReadInlines(IEnumerable<HtmlNode> nodes, InlineCollection inlines)
    {
        foreach (var node in nodes)
        {
            if (Ignore(node)) continue;
            if (node.NodeType == HtmlNodeType.Text)
            {
                var preserve = node.Ancestors().Any(n => Css(n).GetValueOrDefault("white-space") is "pre-wrap" or "pre");
                var value = WebUtility.HtmlDecode(preserve ? node.InnerText : HtmlWhitespace().Replace(node.InnerText, " "));
                if (value.Length > 0) inlines.Add(new Run(value));
                continue;
            }
            if (node.Name == "br") { inlines.Add(new LineBreak()); continue; }
            if (node.Name == "img")
            {
                var uri = node.GetAttributeValue("src", "");
                if (uri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var width = double.TryParse(node.GetAttributeValue("width", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : (double?)null;
                        inlines.Add(new InlineUIContainer(EmailImage.FromDataUri(uri, WebUtility.HtmlDecode(node.GetAttributeValue("alt", "Image")), width)));
                    }
                    catch (Exception ex) when (ex is System.IO.IOException or FormatException or NotSupportedException or ArgumentException) { inlines.Add(new Run("[Image unavailable]")); }
                }
                continue;
            }
            Span span = node.Name == "a" && SafeLink(node.GetAttributeValue("href", "")) is { } url ? new Hyperlink { NavigateUri = new(url) } : new Span();
            ApplyStyles(span, node); ReadInlines(node.ChildNodes, span.Inlines); inlines.Add(span);
        }
    }
    private static Dictionary<string, string> Css(HtmlNode node) => WebUtility.HtmlDecode(node.GetAttributeValue("style", "")).Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split(':', 2)).Where(p => p.Length == 2).GroupBy(p => p[0].Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Last()[1].Trim());
    private static void ApplyStyles(DependencyObject element, HtmlNode node)
    {
        var css = Css(node);
        var family = css.GetValueOrDefault("font-family") ?? WebUtility.HtmlDecode(node.GetAttributeValue("face", ""));
        if (!string.IsNullOrWhiteSpace(family)) element.SetValue(TextElement.FontFamilyProperty, new FontFamily(CleanFamily(family)));
        if (css.TryGetValue("font-size", out var size) && ParseSize(size) is { } pixels) element.SetValue(TextElement.FontSizeProperty, pixels);
        var weight = css.GetValueOrDefault("font-weight");
        if (node.Name is "b" or "strong" || weight is "bold" or "bolder" || (int.TryParse(weight, out var numeric) && numeric >= 600)) element.SetValue(TextElement.FontWeightProperty, FontWeights.Bold);
        else if (weight == "normal") element.SetValue(TextElement.FontWeightProperty, FontWeights.Normal);
        var italic = css.GetValueOrDefault("font-style");
        if (node.Name is "i" or "em" || italic is "italic" or "oblique") element.SetValue(TextElement.FontStyleProperty, FontStyles.Italic);
        else if (italic == "normal") element.SetValue(TextElement.FontStyleProperty, FontStyles.Normal);
        var color = css.GetValueOrDefault("color") ?? node.GetAttributeValue("color", "");
        if (!string.IsNullOrEmpty(color))
        {
            try { if (new BrushConverter().ConvertFromInvariantString(color) is SolidColorBrush brush) element.SetValue(TextElement.ForegroundProperty, brush); }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { }
        }
        if (element is Inline inline)
        {
            if (node.Name == "u" || css.GetValueOrDefault("text-decoration", "").Contains("underline")) inline.TextDecorations = TextDecorations.Underline;
            else if (css.GetValueOrDefault("text-decoration") == "none") inline.TextDecorations = [];
        }
        if (element is Block block && Enum.TryParse<TextAlignment>(css.GetValueOrDefault("text-align"), true, out var alignment)) block.TextAlignment = alignment;
    }
    private static double? ParseSize(string value)
    {
        var unit = value.EndsWith("pt", StringComparison.OrdinalIgnoreCase) ? 96d / 72 : 1;
        var numeric = value.EndsWith("pt", StringComparison.OrdinalIgnoreCase) || value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value;
        return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size * unit is >= 6 and <= 200 ? size * unit : null;
    }
    private static string CleanFamily(string family)
    {
        var first = family.Split(',')[0].Trim().Trim('"', '\'');
        return first.Length > 0 && first.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-') ? first : "Arial";
    }
    private static string? SafeLink(string url) => Uri.TryCreate(WebUtility.HtmlDecode(url), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" ? uri.AbsoluteUri : null;
    public static string ToHtml(FlowDocument document)
    {
        var html = new StringBuilder("<div style=\"font-family:Arial,sans-serif;font-size:12pt;color:#000000;\">\n");
        WriteBlocks(document.Blocks, html); html.Append("</div>");
        return string.IsNullOrWhiteSpace(new TextRange(document.ContentStart, document.ContentEnd).Text) && !html.ToString().Contains("<img ") ? "" : html.ToString();
    }
    public static string ToPlainText(FlowDocument document) => new TextRange(document.ContentStart, document.ContentEnd).Text.Replace("\uFFFC", "").TrimEnd('\r', '\n');
    private static void WriteBlocks(BlockCollection blocks, StringBuilder output)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    output.Append("<p style=\"margin:0 0 12px 0;text-align:").Append(paragraph.TextAlignment.ToString().ToLowerInvariant()).Append(";\">");
                    var segments = new List<Segment>(); Gather(paragraph.Inlines, segments); WriteSegments(segments, output);
                    if (segments.Count == 0) output.Append("<br/>");
                    output.Append("</p>\n"); break;
                case WpfList list:
                    var tag = list.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman ? "ol" : "ul";
                    output.Append('<').Append(tag).Append(">\n");
                    foreach (var item in list.ListItems) { output.Append("<li>"); WriteBlocks(item.Blocks, output); output.Append("</li>\n"); }
                    output.Append("</").Append(tag).Append(">\n"); break;
                case Section section: WriteBlocks(section.Blocks, output); break;
            }
        }
    }
    private static TextStyle StyleOf(Inline inline)
    {
        var color = inline.Foreground is SolidColorBrush brush ? $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}" : "#000000";
        var decorations = inline.TextDecorations;
        if (inline.ReadLocalValue(Inline.TextDecorationsProperty) == DependencyProperty.UnsetValue)
        {
            for (DependencyObject? parent = LogicalTreeHelper.GetParent(inline); parent is Inline ancestor; parent = LogicalTreeHelper.GetParent(parent))
                if (ancestor.ReadLocalValue(Inline.TextDecorationsProperty) != DependencyProperty.UnsetValue) { decorations = ancestor.TextDecorations; break; }
        }
        return new(CleanFamily(inline.FontFamily.Source), inline.FontSize, inline.FontWeight.ToOpenTypeWeight() >= 600, inline.FontStyle == FontStyles.Italic, decorations.Any(d => d.Location == TextDecorationLocation.Underline), color);
    }
    private static void Gather(InlineCollection inlines, List<Segment> segments, string? link = null)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run: if (run.Text.Length > 0) segments.Add(new(run.Text, StyleOf(run), link)); break;
                case LineBreak line: segments.Add(new("\n", StyleOf(line), link)); break;
                case Hyperlink hyperlink: Gather(hyperlink.Inlines, segments, SafeLink(hyperlink.NavigateUri?.OriginalString ?? "")); break;
                case Span span: Gather(span.Inlines, segments, link); break;
                case InlineUIContainer container when container.Child is System.Windows.Controls.Image image:
                    segments.Add(new("\uFFFC", StyleOf(container), link, EmailImage.ToHtml(image))); break;
            }
        }
    }
    private static void WriteSegments(List<Segment> segments, StringBuilder output)
    {
        // Formatting half a merge field can split it into WPF runs. Keep each token
        // contiguous in HTML, adopting the style of its first character.
        var text = string.Concat(segments.Select(s => s.Text));
        var tokens = MergeTokens().Matches(text).Cast<Match>().ToDictionary(m => m.Index);
        var offsets = new int[segments.Count];
        for (var i = 1; i < segments.Count; i++) offsets[i] = offsets[i - 1] + segments[i - 1].Text.Length;
        var position = 0; var index = 0;
        while (position < text.Length)
        {
            while (index + 1 < segments.Count && offsets[index + 1] <= position) index++;
            var segment = segments[index];
            if (segment.ImageHtml != null) { output.Append(segment.ImageHtml); position++; continue; }
            var length = tokens.TryGetValue(position, out var token) ? token.Length : Math.Min(segment.Text.Length - (position - offsets[index]), text.Length - position);
            if (token == null) length = Math.Min(length, tokens.Keys.Where(k => k > position).DefaultIfEmpty(text.Length).Min() - position);
            WriteText(text.Substring(position, length), segment.Style, segment.Link, output); position += length;
        }
    }
    private static void WriteText(string text, TextStyle style, string? link, StringBuilder output)
    {
        if (link != null) output.Append("<a href=\"").Append(WebUtility.HtmlEncode(link)).Append("\">");
        var css = $"font-family:'{style.Family}',sans-serif;font-size:{(style.Size * 72 / 96).ToString("0.##", CultureInfo.InvariantCulture)}pt;color:{style.Color};font-weight:{(style.Bold ? "bold" : "normal")};font-style:{(style.Italic ? "italic" : "normal")};text-decoration:{(style.Underline ? "underline" : "none")};white-space:pre-wrap;";
        output.Append("<span style=\"").Append(WebUtility.HtmlEncode(css)).Append("\">").Append(WebUtility.HtmlEncode(text).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br/>")).Append("</span>");
        if (link != null) output.Append("</a>");
    }
}
