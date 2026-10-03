using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace MergeDesk.App.Controls;
public partial class HelpView : UserControl
{
    private readonly List<HelpTopic> topics;
    public HelpView()
    {
        using var stream = Application.GetResourceStream(new Uri("/MergeDesk.App;component/HelpContent.json", UriKind.Relative))!.Stream;
        topics = JsonSerializer.Deserialize<List<string[]>>(stream)!.Select(x => new HelpTopic(x[0], x[1])).ToList();
        InitializeComponent();
        Filter();
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (Topics != null) Filter(); }
    private void Filter()
    {
        var query = Search.Text.Trim();
        Topics.ItemsSource = topics.Where(t => (t.Title + " " + t.Body).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        Topics.SelectedIndex = 0;
        if (Topics.Items.Count == 0) { TopicTitle.Text = "No matching topics"; TopicBody.Text = "Try another word, or clear the search box to show all instructions."; }
    }
    private void TopicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Topics.SelectedItem is HelpTopic topic) { TopicTitle.Text = topic.Title; TopicBody.Text = topic.Body; }
    }
    private void WatchVideo(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "HelpAssets", "MergeDesk-Walkthrough.mp4");
            if (!File.Exists(path)) throw new FileNotFoundException("The walkthrough was not found. Extract the complete application ZIP, including HelpAssets, and try again.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), "Could not open the video. " + ex.Message, "MergeDesk help"); }
    }
    public sealed record HelpTopic(string Title, string Body);
}
