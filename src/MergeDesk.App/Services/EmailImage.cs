using System.IO;
using System.Net;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MergeDesk.Core;

namespace MergeDesk.App.Services;

public static class EmailImage
{
    private sealed record Metadata(string DataUri, string Alt);
    public static Image FromFile(string path)
    {
        if (new FileInfo(path).Length > InlineImageTools.MaximumImageBytes) throw new InvalidDataException("Choose an image no larger than 2 MiB.");
        var type = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", _ => throw new InvalidDataException("Choose a PNG, JPEG or GIF image.") };
        return FromDataUri("data:" + type + ";base64," + Convert.ToBase64String(File.ReadAllBytes(path)), Path.GetFileNameWithoutExtension(path));
    }
    public static Image FromDataUri(string uri, string alt, double? width = null)
    {
        var image = InlineImageTools.Parse(uri);
        using var stream = new MemoryStream(image.Bytes);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 20_000_000) throw new InvalidDataException("Choose an image smaller than 20 megapixels.");
        var displayWidth = Math.Clamp(width ?? Math.Min(600, bitmap.PixelWidth), 1, 1200);
        return new Image { Source = bitmap, Width = displayWidth, Height = displayWidth * bitmap.PixelHeight / bitmap.PixelWidth,
            Stretch = Stretch.Uniform, Tag = new Metadata(InlineImageTools.DataUri(image), alt), ToolTip = alt };
    }
    public static string Description(Image image) => image.Tag is Metadata data ? data.Alt : "";
    public static void Resize(Image image, double width, string description)
    {
        if (!double.IsFinite(width) || width < 1 || width > 1200) throw new ArgumentException("Enter a width between 1 and 1200 pixels.");
        if (image.Source is not BitmapSource bitmap || image.Tag is not Metadata data) throw new ArgumentException("Choose an embedded picture.");
        image.Width=width; image.Height=width*bitmap.PixelHeight/bitmap.PixelWidth;
        image.Tag=data with { Alt=description }; image.ToolTip=description;
    }
    public static double OriginalWidth(Image image) => image.Source is BitmapSource bitmap ? Math.Min(1200, bitmap.PixelWidth) : image.Width;
    public static string ToHtml(Image image)
    {
        if (image.Tag is not Metadata data) return "";
        return $"<img src=\"{data.DataUri}\" alt=\"{WebUtility.HtmlEncode(data.Alt)}\" width=\"{Math.Round(image.Width)}\" height=\"{Math.Round(image.Height)}\"/>";
    }
}
