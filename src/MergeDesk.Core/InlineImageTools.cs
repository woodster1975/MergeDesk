using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MergeDesk.Core;

public static partial class InlineImageTools
{
    public const int MaximumImageBytes = 2 * 1024 * 1024;
    private const int MaximumTotalBytes = 10 * 1024 * 1024;
    [GeneratedRegex("\\bsrc\\s*=\\s*(?<q>[\"'])(?<uri>data:image/[^\"']*)\\k<q>", RegexOptions.IgnoreCase)]
    private static partial Regex Sources();
    public static InlineImage Parse(string uri)
    {
        if (uri.Length > MaximumImageBytes * 4 / 3 + 100) throw new InvalidDataException("Images must be 2 MiB or smaller.");
        var comma = uri.IndexOf(',');
        if (comma < 0) throw new InvalidDataException("Invalid embedded image.");
        var prefix = uri[..comma].ToLowerInvariant();
        var media = prefix switch { "data:image/png;base64" => "image/png", "data:image/jpeg;base64" => "image/jpeg", "data:image/gif;base64" => "image/gif", _ => throw new InvalidDataException("Use PNG, JPEG or GIF images.") };
        var bytes = Convert.FromBase64String(uri[(comma + 1)..]);
        if (bytes.Length == 0 || bytes.Length > MaximumImageBytes) throw new InvalidDataException("Images must be nonempty and 2 MiB or smaller.");
        var signature = media switch
        {
            "image/png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
            _ => bytes.Length >= 6 && (System.Text.Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a")
        };
        if (!signature) throw new InvalidDataException("The image bytes do not match its file type.");
        var id = "image-" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + "@mergedesk";
        var extension = media == "image/jpeg" ? ".jpg" : media == "image/png" ? ".png" : ".gif";
        return new(id, "image-" + id[6..18] + extension, media, bytes);
    }
    public static string DataUri(InlineImage image) => "data:" + image.MediaType + ";base64," + Convert.ToBase64String(image.Bytes);
    public static (string Html, IReadOnlyList<InlineImage> Images) Extract(string html)
    {
        var images = new Dictionary<string, InlineImage>();
        var result = Sources().Replace(html, match =>
        {
            var image = Parse(match.Groups["uri"].Value); images.TryAdd(image.ContentId, image);
            if (images.Values.Sum(i => i.Bytes.Length) > MaximumTotalBytes) throw new InvalidDataException("Keep embedded images below 10 MiB in total.");
            return "src=\"cid:" + image.ContentId + "\"";
        });
        return (result, images.Values.ToArray());
    }
    public static string PreviewHtml(MergedMessage message)
    {
        var html = message.Html;
        foreach (var image in message.InlineImages ?? []) html = html.Replace("cid:" + image.ContentId, DataUri(image), StringComparison.Ordinal);
        return html;
    }
}
