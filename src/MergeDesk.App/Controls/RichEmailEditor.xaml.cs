using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MergeDesk.App.Services;

namespace MergeDesk.App.Controls;

/// <summary>View-only rich-text behaviour; the bound HTML remains in the view model.</summary>
public partial class RichEmailEditor : UserControl
{
    public sealed record TextColor(string Name, string Hex);
    public static string[] FontNames { get; } = ["Arial", "Calibri", "Segoe UI", "Verdana", "Tahoma", "Georgia", "Times New Roman", "Courier New"];
    public static double[] FontSizes { get; } = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36];
    public static TextColor[] TextColors { get; } = [new("Black", "#000000"), new("Charcoal", "#374151"), new("Teal", "#146C60"), new("Blue", "#1D4ED8"), new("Red", "#B91C1C"), new("Purple", "#7E22CE")];
    public static readonly DependencyProperty HtmlProperty = DependencyProperty.Register(nameof(Html), typeof(string), typeof(RichEmailEditor),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, HtmlChanged));
    public static readonly DependencyProperty FieldsProperty = DependencyProperty.Register(nameof(Fields), typeof(IEnumerable), typeof(RichEmailEditor));
    public string Html { get => (string)GetValue(HtmlProperty); set => SetValue(HtmlProperty, value); }
    public IEnumerable? Fields { get => (IEnumerable?)GetValue(FieldsProperty); set => SetValue(FieldsProperty, value); }
    public event EventHandler? PlainTextRequested;
    public string PlainText => EmailDocumentConverter.ToPlainText(Editor.Document);
    private bool synchronizing = true, updatingTools;

    public RichEmailEditor()
    {
        InitializeComponent();
        // Pasted content enters as plain text. This avoids Office markup, active content
        // and token-splitting XAML; users can format the pasted text with the toolbar.
        DataObject.AddPastingHandler(Editor, OnPaste);
        LoadHtml(Html);
    }
    private static void HtmlChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (RichEmailEditor)sender;
        if (!control.synchronizing && control.Editor != null) control.LoadHtml(args.NewValue as string ?? "");
    }
    private void LoadHtml(string html)
    {
        synchronizing = true;
        try
        {
            var imported = EmailDocumentConverter.FromHtml(html);
            Editor.Document = imported.Document;
            ImportNotice.Visibility = imported.HasUnsupportedFormatting ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { synchronizing = false; }
        UpdateTools(); RefreshPictures();
    }
    private void EditorChanged(object sender, TextChangedEventArgs e)
    {
        if (synchronizing) return;
        synchronizing = true;
        try { SetCurrentValue(HtmlProperty, EmailDocumentConverter.ToHtml(Editor.Document)); ImportNotice.Visibility = Visibility.Collapsed; }
        finally { synchronizing = false; }
        UpdateTools(); RefreshPictures();
    }
    private void EditorSelectionChanged(object sender, RoutedEventArgs e)
    { if (!synchronizing && FontPicker != null) UpdateTools(); }
    private void UpdateTools()
    {
        updatingTools = true;
        try
        {
            var selection = Editor.Selection;
            BoldToggle.IsChecked = selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight weight && weight.ToOpenTypeWeight() >= 600;
            ItalicToggle.IsChecked = Equals(selection.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic);
            UnderlineToggle.IsChecked = selection.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations && decorations.Any(d => d.Location == TextDecorationLocation.Underline);
            FontPicker.SelectedItem = (selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily)?.Source;
            var size = selection.GetPropertyValue(TextElement.FontSizeProperty);
            SizePicker.SelectedItem = size is double pixels ? FontSizes.Cast<double?>().FirstOrDefault(s => Math.Abs(s!.Value - pixels * 72 / 96) < .01) : null;
            var color = selection.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush;
            ColorPicker.SelectedItem = color == null ? null : TextColors.FirstOrDefault(c => c.Hex == $"#{color.Color.R:X2}{color.Color.G:X2}{color.Color.B:X2}");
        }
        finally { updatingTools = false; }
    }
    private void FontChanged(object sender, SelectionChangedEventArgs e)
    { if (!updatingTools && FontPicker.SelectedItem is string family) Apply(TextElement.FontFamilyProperty, new FontFamily(family)); }
    private void FontSizeChanged(object sender, SelectionChangedEventArgs e)
    { if (!updatingTools && SizePicker.SelectedItem is double points) Apply(TextElement.FontSizeProperty, points * 96 / 72); }
    private void ColorChanged(object sender, SelectionChangedEventArgs e)
    { if (!updatingTools && ColorPicker.SelectedItem is TextColor color) Apply(TextElement.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(color.Hex))); }
    private void Apply(DependencyProperty property, object value)
    { Editor.Selection.ApplyPropertyValue(property, value); Editor.Focus(); }
    private void InsertField(object sender, RoutedEventArgs e)
    {
        if (FieldPicker.SelectedValue is not string field || string.IsNullOrWhiteSpace(field))
        { EditorHint.Text = "Choose a mapped field first. Load recipients in the Recipients tab if the list is empty."; return; }
        Editor.Selection.Text = "{{" + field.Trim() + "}}";
        Editor.CaretPosition = Editor.Selection.End;
        Editor.Focus();
        EditorHint.Text = "Merge field inserted. Each recipient will see their own value.";
    }
    private void CopyPlainText(object sender, RoutedEventArgs e)
    { PlainTextRequested?.Invoke(this, EventArgs.Empty); EditorHint.Text = "Plain-text version updated from the email. You can edit it separately below."; }
    private void InsertImage(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Email images|*.png;*.jpg;*.jpeg;*.gif", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var image = EmailImage.FromFile(dialog.FileName);
            Editor.Selection.Text = "";
            var inline = new InlineUIContainer(image, Editor.CaretPosition);
            Editor.CaretPosition = inline.ElementEnd;
            SetCurrentValue(HtmlProperty, EmailDocumentConverter.ToHtml(Editor.Document));
            RefreshPictures(); PicturePicker.SelectedItem = pictureChoices.FirstOrDefault(x=>ReferenceEquals(x.Image,image)); PictureSettings.IsExpanded=true;
            Editor.Focus(); EditorHint.Text = "Image inserted and embedded in the email. Preview to check its position.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or FormatException or NotSupportedException or ArgumentException)
        { EditorHint.Text = "Could not insert image: " + ex.Message; }
    }
    private sealed record PictureChoice(Image Image, Paragraph Paragraph, string Label);
    private List<PictureChoice> pictureChoices = [];
    private void RefreshPictures()
    {
        if(PicturePicker == null)return;
        var selected=(PicturePicker.SelectedItem as PictureChoice)?.Image;
        var items=new List<PictureChoice>();
        void Inlines(InlineCollection inlines,Paragraph paragraph) { foreach(var inline in inlines) { if(inline is InlineUIContainer {Child:Image image}) items.Add(new(image,paragraph,$"Picture {items.Count+1}: {EmailImage.Description(image)}")); else if(inline is Span span) Inlines(span.Inlines,paragraph); } }
        void Blocks(BlockCollection blocks) { foreach(var block in blocks) { if(block is Paragraph paragraph)Inlines(paragraph.Inlines,paragraph); else if(block is Section section)Blocks(section.Blocks); else if(block is System.Windows.Documents.List list) foreach(var item in list.ListItems)Blocks(item.Blocks); } }
        Blocks(Editor.Document.Blocks);
        if(items.Count==pictureChoices.Count && items.Zip(pictureChoices).All(pair=>ReferenceEquals(pair.First.Image,pair.Second.Image)&&pair.First.Label==pair.Second.Label))return;
        pictureChoices=items; PicturePicker.ItemsSource=items; PicturePicker.SelectedItem=items.FirstOrDefault(x=>ReferenceEquals(x.Image,selected))??items.FirstOrDefault();
    }
    private void PictureSelected(object sender,SelectionChangedEventArgs e)
    {
        var chosen=PicturePicker.SelectedItem as PictureChoice;
        PictureWidth.Text=chosen?.Image.Width.ToString("0",System.Globalization.CultureInfo.CurrentCulture)??"";
        PictureDescription.Text=chosen==null?"":EmailImage.Description(chosen.Image);
    }
    private void CommitPicture()
    {
        synchronizing=true;
        try { SetCurrentValue(HtmlProperty,EmailDocumentConverter.ToHtml(Editor.Document)); ImportNotice.Visibility=Visibility.Collapsed; }
        finally { synchronizing=false; }
        RefreshPictures(); EditorHint.Text="Picture updated. Preview the merged email before sending.";
    }
    private void ApplyPicture(object sender,RoutedEventArgs e)
    {
        if(PicturePicker.SelectedItem is not PictureChoice chosen) {EditorHint.Text="Insert or choose a picture first.";return;}
        if(!double.TryParse(PictureWidth.Text,out var width)) {EditorHint.Text="Enter a width between 1 and 1200 pixels.";return;}
        try { EmailImage.Resize(chosen.Image,width,PictureDescription.Text); CommitPicture(); }
        catch(ArgumentException ex) {EditorHint.Text=ex.Message;}
    }
    private void OriginalPicture(object sender,RoutedEventArgs e)
    {
        if(PicturePicker.SelectedItem is not PictureChoice chosen)return;
        EmailImage.Resize(chosen.Image,EmailImage.OriginalWidth(chosen.Image),PictureDescription.Text); CommitPicture(); PictureSelected(sender,new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,Array.Empty<object>(),Array.Empty<object>()));
    }
    private void AlignPicture(object sender,RoutedEventArgs e)
    {
        if(PicturePicker.SelectedItem is not PictureChoice chosen) {EditorHint.Text="Insert or choose a picture first.";return;}
        chosen.Paragraph.TextAlignment=Enum.Parse<TextAlignment>((string)((Button)sender).Tag); CommitPicture();
    }
    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText)) { e.CancelCommand(); return; }
        var value = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? "";
        e.DataObject = new DataObject(DataFormats.UnicodeText, value);
        e.FormatToApply = DataFormats.UnicodeText;
    }
}
