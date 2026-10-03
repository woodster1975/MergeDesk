using Microsoft.Win32;
using MergeDesk.Core;
namespace MergeDesk.App.Services;
public interface IDesktopDialogs
{
    LibraryMessage? EditSignature(LibraryMessage? existing, IReadOnlyList<FieldMapping> mappings) => null;
    bool ConfirmLibraryDelete(string name) => false;
    bool ConfirmHistoryClear() => false;
    string? OpenSource();
    string[] OpenAttachments();
    string? ChooseOutput();
    string? ProjectFile(bool save);
    bool ConfirmDraftCreation(int count, string destination) => false;
    bool ConfirmSending(int count, string destination, int intervalSeconds) => false;
    bool ConfirmTestSending(MergedMessage message, string destination) => false;
}
public sealed class DesktopDialogs : IDesktopDialogs
{
    public LibraryMessage? EditSignature(LibraryMessage? existing, IReadOnlyList<FieldMapping> mappings) { var window = new SignatureEditorWindow(existing, mappings); window.Owner = System.Windows.Application.Current?.MainWindow; return window.ShowDialog() == true ? window.Result : null; }
    public bool ConfirmLibraryDelete(string name) => System.Windows.MessageBox.Show($"Remove '{name}' from the library? Your current message will remain unchanged.", "Remove saved item", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
    public bool ConfirmHistoryClear() => System.Windows.MessageBox.Show(
        "Clear ALL saved run history and the displayed results?\n\nThis cannot be undone. Exported files, local run logs and Outlook messages are kept. Duplicate-send protection is also kept, so previously submitted emails will still be skipped.\n\nClear history?", "Clear history", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
    public bool ConfirmTestSending(MergedMessage message, string destination) => System.Windows.MessageBox.Show(
        $"Send ONE real test email to:\n{message.To}\n\nUsing: {destination}\nSource row: {message.Row}\nSubject: {message.Subject}\n\nCC and BCC are cleared. The selected row's body, embedded images and {message.Attachments.Count} attachments are included. Check that this test address belongs to you and is appropriate for this row's information.\n\nAn identical previously submitted test will be skipped. Production sending has a separate record.\n\nSend this test email?",
        "Confirm test email", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
        System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
    public bool ConfirmSending(int count, string destination, int intervalSeconds) => System.Windows.MessageBox.Show(
        $"SEND up to {count:N0} personalised emails using:\n{destination}\n\nThis sends real email. Review To, CC, BCC, subject, body and attachments before continuing.\n\nMessages are processed one at a time with a {intervalSeconds}-second interval. Identical previously submitted messages will be skipped. Prepared messages from an interrupted send may be reused; check those drafts before continuing.\n\nProceed with sending?",
        "Confirm sending", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
        System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
    public bool ConfirmDraftCreation(int count, string destination) => System.Windows.MessageBox.Show(
        $"Create {count:N0} drafts in:\n{destination}\n\nRecipients, body and attachments will be copied into this mailbox. No emails will be sent.\n\nA fresh batch creates new drafts; check previous results before repeating it.",
        "Create Outlook drafts", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question,
        System.Windows.MessageBoxResult.Cancel) == System.Windows.MessageBoxResult.OK;
    public string? OpenSource() { var dialog = new OpenFileDialog { Filter = "Recipient files|*.csv;*.xlsx", CheckFileExists = true }; return dialog.ShowDialog() == true ? dialog.FileName : null; }
    public string[] OpenAttachments() { var dialog = new OpenFileDialog { Multiselect = true, CheckFileExists = true, Title = "Add files for every recipient" }; return dialog.ShowDialog() == true ? dialog.FileNames : []; }
    public string? ChooseOutput() { var dialog = new OpenFolderDialog { Title = "Choose a folder for dry-run output" }; return dialog.ShowDialog() == true ? dialog.FolderName : null; }
    public string? ProjectFile(bool save)
    {
        FileDialog dialog = save ? new SaveFileDialog { DefaultExt = ".mergedesk.json", FileName = "Project.mergedesk.json" } : new OpenFileDialog();
        dialog.Filter = "MergeDesk projects|*.json";
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
