using System.Windows.Documents;
namespace MergeDesk.App.Services;

// Compose and preview share an inert renderer; no browser, scripts or network requests.
public static class SafeHtmlPreview
{
    public static FlowDocument Render(string html) => EmailDocumentConverter.FromHtml(html).Document;
}
