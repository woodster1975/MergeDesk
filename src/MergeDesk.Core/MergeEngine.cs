using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace MergeDesk.Core;

public sealed partial class MergeEngine
{
    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}")]
    private static partial Regex Tokens();

    public MergeBatch Merge(RecipientData data, IReadOnlyList<FieldMapping> mappings, MergeTemplate template)
    {
        var messages = new List<MergedMessage>();
        var issues = new List<ValidationIssue>();
        var embedded = (Html: template.Html ?? "", Images: (IReadOnlyList<InlineImage>)Array.Empty<InlineImage>());
        try { embedded = InlineImageTools.Extract(embedded.Html); }
        catch (Exception ex) when (ex is FormatException or InvalidDataException) { issues.Add(new(0, "Images", ex.Message)); }
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Field) || mapping.Field.IndexOfAny(['{', '}', '\r', '\n']) >= 0)
                issues.Add(new(0, "Mapping", "Merge field names must be non-empty and contain no braces or line breaks."));
            if (!fields.Add(mapping.Field.Trim())) issues.Add(new(0, "Mapping", $"Duplicate merge field: {mapping.Field}."));
            if (!data.Columns.Contains(mapping.Column, StringComparer.OrdinalIgnoreCase))
                issues.Add(new(0, "Mapping", $"Column '{mapping.Column}' no longer exists. Choose a source column."));
        }
        if (data.Recipients.Count == 0) issues.Add(new(0, "Recipients", "Load a file with at least one recipient."));
        if (string.IsNullOrWhiteSpace(template.Html) && string.IsNullOrWhiteSpace(template.Text))
            issues.Add(new(0, "Body", "Enter an HTML or plain-text body."));
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipient in data.Recipients)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in mappings)
                if (recipient.Values.TryGetValue(mapping.Column, out var value)) values[mapping.Field.Trim()] = value;
            string Render(string source, string area, bool html = false)
            {
                var merged = Tokens().Replace(source ?? "", match =>
                {
                    var key = match.Groups[1].Value.Trim();
                    if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    {
                        issues.Add(new(recipient.Row, area, $"Field '{key}' is unmapped or empty."));
                        return match.Value;
                    }
                    return html ? WebUtility.HtmlEncode(value) : value;
                });
                // Also reject malformed or nested tokens, including tokens introduced by source values.
                if (merged.Contains("{{") || merged.Contains("}}"))
                    issues.Add(new(recipient.Row, area, "Unresolved or malformed {{Field}} token."));
                return merged;
            }
            var to = Render(template.To, "To");
            var cc = Render(template.Cc, "CC");
            var bcc = Render(template.Bcc, "BCC");
            var subject = Render(template.Subject, "Subject");
            if (string.IsNullOrWhiteSpace(subject)) issues.Add(new(recipient.Row, "Subject", "Subject is required."));
            if (subject.Contains('\r') || subject.Contains('\n')) issues.Add(new(recipient.Row, "Subject", "Subject cannot contain line breaks."));
            var toAddresses = ValidateAddresses(to, true, "To", recipient.Row, issues);
            ValidateAddresses(cc, false, "CC", recipient.Row, issues);
            ValidateAddresses(bcc, false, "BCC", recipient.Row, issues);
            foreach (var address in toAddresses.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (seen.TryGetValue(address, out var first))
                    issues.Add(new(recipient.Row, "Duplicates", $"'{address}' also appears in row {first}. Remove the duplicate before running."));
                else seen[address] = recipient.Row;
            }
            var attachments = new List<string>();
            foreach (var path in template.GlobalAttachments.Concat(template.AttachmentPatterns.Select(p => Render(p, "Attachments"))))
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                try
                {
                    if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Use an absolute attachment path.");
                    var full = Path.GetFullPath(path);
                    if (!File.Exists(full)) issues.Add(new(recipient.Row, "Attachments", $"File not found: {full}"));
                    else
                    {
                        using var readable = File.Open(full, FileMode.Open, FileAccess.Read, FileShare.Read);
                        attachments.Add(full);
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
                { issues.Add(new(recipient.Row, "Attachments", $"Cannot use '{path}': {ex.Message}")); }
            }
            messages.Add(new(recipient.Row, to, cc, bcc, subject, Render(embedded.Html, "HTML", true),
                Render(template.Text, "Plain text"), attachments.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), embedded.Images));
        }
        return new(messages, issues.Distinct().ToArray());
    }

    public static string[] SplitAddresses(string value) => value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string[] ValidateAddresses(string value, bool required, string area, int row, List<ValidationIssue> issues)
    {
        var addresses = SplitAddresses(value);
        if (required && addresses.Length == 0) issues.Add(new(row, area, "An email address is required."));
        if (value.Contains('\r') || value.Contains('\n')) issues.Add(new(row, area, "Email fields cannot contain line breaks."));
        foreach (var address in addresses)
        {
            if (!MailAddress.TryCreate(address, out var parsed) || parsed.Address != address ||
                !parsed.Host.Contains('.') || parsed.Host.StartsWith('.') || parsed.Host.EndsWith('.'))
                issues.Add(new(row, area, $"Invalid email address: '{address}'. Use a bare address such as name@example.com."));
        }
        return addresses;
    }
}
