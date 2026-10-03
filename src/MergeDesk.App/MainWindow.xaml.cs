using System.IO;

using System.ComponentModel;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Data;

using MergeDesk.App.Services;

using MergeDesk.App.ViewModels;

using MergeDesk.Core;

using MergeDesk.Infrastructure;



namespace MergeDesk.App;

public partial class MainWindow : Window

{

    private readonly MainViewModel model;

    private bool closeReady, finishingClose;

    public MainWindow()

    {

        InitializeComponent();

        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.F1) { ShowHelp(this, new RoutedEventArgs()); e.Handled = true; } };

        var storage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MergeDesk", "history.db");

        model = new(new RecipientReader(), new SqliteRunRepository(storage), new DesktopDialogs(), new OutlookDraftConnector(),

            new JsonConnectionSettingsStore(Path.Combine(Path.GetDirectoryName(storage)!, "connection-settings.json")),
            recoveryStore: new JsonProjectRecoveryStore(Path.Combine(Path.GetDirectoryName(storage)!, "recovery")), messageLibrary: new JsonMessageLibrary(Path.Combine(Path.GetDirectoryName(storage)!, "library")));

        DataContext = model;

        model.PropertyChanged += ModelChanged;

        Loaded += async (_, _) =>
        {
            try { await model.LoadConnectionSettingsAsync(); await model.LoadHistoryAsync(); await model.LoadLibraryAsync(); }
            catch (Exception ex) { MessageBox.Show(this, $"Could not load local settings or history: {ex.Message}", "MergeDesk"); }
            await model.LoadRecoveryAsync();
        };
        Closing += async (_, e) =>

        {

            if (closeReady) return;

            e.Cancel = true;

            if (finishingClose) return;

            if (model.IsBusy) { MessageBox.Show(this, "Cancel the current operation and wait for it to finish before closing.", "Operation in progress"); return; }

            finishingClose = true; IsEnabled = false;
            try
            {
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                var saved = await model.FlushAutosaveAsync();
                if (!saved && MessageBox.Show(this, "Your latest changes could not be autosaved. Choose No to stay and save your project, or Yes to close anyway.", "Autosave failed", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;

                closeReady = true; Close();

            }

            finally { finishingClose = false; IsEnabled = true; }

        };

        Closed += (_, _) => model.CloseConnection();

    }

    private void RecipientColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)

    {

        if (DataContext is not MainViewModel workspace) return;

        if (e.PropertyName == workspace.MatchColumn) { e.Cancel = true; return; }

        if (e.PropertyName == workspace.InclusionColumn)

        {

            var checkbox = new FrameworkElementFactory(typeof(CheckBox));

            checkbox.SetBinding(CheckBox.IsCheckedProperty, new Binding(e.PropertyName) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

            checkbox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);

            checkbox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            checkbox.SetValue(FrameworkElement.ToolTipProperty, "Include this source row in drafts, dry-run exports and batch sending.");
            checkbox.SetValue(ToolTipService.ShowOnDisabledProperty, true);
            checkbox.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Include recipient");

            e.Column = new DataGridTemplateColumn { Header = "Include", Width = 75, CellTemplate = new DataTemplate { VisualTree = checkbox }, CanUserSort = false };

        }

        else if (e.PropertyName == workspace.SourceRowColumn) { e.Column.Header = "Row"; e.Column.Width = 55; }

        e.Column.IsReadOnly = true;

    }

    private void ShowHelp(object sender, RoutedEventArgs e) => model.SelectedTab = 5;

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)

    {

        if (e.PropertyName == nameof(MainViewModel.PreviewHtml)) HtmlPreview.Document = SafeHtmlPreview.Render(model.PreviewHtml);

    }

    private void HistorySelected(object sender, SelectionChangedEventArgs e) => model.SelectRun((sender as ListBox)?.SelectedItem as RunRecord);

    private void UseEmailPlainText(object sender, EventArgs e) => model.Text = EmailEditor.PlainText;

    private void InsertSubjectField(object sender, RoutedEventArgs e)

    {

        if (SubjectFieldPicker.SelectedValue is not string field || string.IsNullOrWhiteSpace(field))

        { SubjectFieldHint.Text = "Choose a mapped field first. Load recipients if the list is empty."; return; }

        var token = "{{" + field.Trim() + "}}";

        var start = SubjectEditor.SelectionStart;

        SubjectEditor.SelectedText = token;

        SubjectEditor.Select(start + token.Length, 0);

        SubjectEditor.Focus();

        SubjectFieldHint.Text = "Field inserted in the subject. Preview to check each recipient's value.";

    }

}
