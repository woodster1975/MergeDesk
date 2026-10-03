using System.Collections.ObjectModel;
using System.Net;
using MergeDesk.Core;
namespace MergeDesk.App.ViewModels;
public sealed partial class MainViewModel
{
 private readonly IMessageLibrary? library;
 private bool pendingTemplateMappings;
 private LibraryMessage? selectedTemplate, selectedSignature;
 private string templateName="", libraryStatus="Save frequently used messages and signatures on this computer.";
 public ObservableCollection<LibraryMessage> Templates { get; }=[];
 public ObservableCollection<LibraryMessage> Signatures { get; }=[];
 public string TemplateName { get=>templateName; set { if(Set(ref templateName,value)) SaveTemplateCommand.Refresh(); } }
 public string LibraryStatus { get=>libraryStatus; private set=>Set(ref libraryStatus,value); }
 public LibraryMessage? SelectedTemplate { get=>selectedTemplate; set { if(Set(ref selectedTemplate,value)) { TemplateName=value?.Name??""; UseTemplateCommand.Refresh(); DeleteTemplateCommand.Refresh(); } } }
 public LibraryMessage? SelectedSignature { get=>selectedSignature; set { if(Set(ref selectedSignature,value)) { EditSignatureCommand.Refresh(); AppendSignatureCommand.Refresh(); DeleteSignatureCommand.Refresh(); } } }
 public AsyncCommand SaveTemplateCommand { get; }
 public AsyncCommand UseTemplateCommand { get; }
 public AsyncCommand DeleteTemplateCommand { get; }
 public AsyncCommand NewTemplateCommand { get; }
 public AsyncCommand NewSignatureCommand { get; }
 public AsyncCommand EditSignatureCommand { get; }
 public AsyncCommand AppendSignatureCommand { get; }
 public AsyncCommand DeleteSignatureCommand { get; }
 public AsyncCommand RefreshLibraryCommand { get; }
 public async Task LoadLibraryAsync()
 {
  if(library==null)return;
  var items=await library.LoadAsync(); var tid=SelectedTemplate?.Id; var sid=SelectedSignature?.Id;
  Templates.Clear(); Signatures.Clear(); foreach(var item in items) (item.IsSignature?Signatures:Templates).Add(item);
  SelectedTemplate=Templates.FirstOrDefault(x=>x.Id==tid); SelectedSignature=Signatures.FirstOrDefault(x=>x.Id==sid);
 }
 private async Task SaveTemplateAsync()
 {
  if(library==null)return;
  if(string.IsNullOrWhiteSpace(Html)&&string.IsNullOrWhiteSpace(Text)) { LibraryStatus="Write a message before saving a template.";return; }
  var item=new LibraryMessage(SelectedTemplate?.Id??Guid.NewGuid(),TemplateName.Trim(),false,Template(),FieldMappings());
  await library.SaveAsync(item); await LoadLibraryAsync(); SelectedTemplate=Templates.Single(x=>x.Id==item.Id);
  LibraryStatus="Template saved. It includes message fields, mappings and attachment paths; recipient data and account settings stay in your project.";
 }
 private Task UseTemplateAsync()
 {
  if(SelectedTemplate is not {} item)return Task.CompletedTask;
  var t=item.Template; To=t.To;Cc=t.Cc;Bcc=t.Bcc;Subject=t.Subject;Html=t.Html;Text=t.Text;
  Patterns=string.Join(Environment.NewLine,t.AttachmentPatterns); Attachments.Clear();foreach(var a in t.GlobalAttachments)Attachments.Add(a);
  Mappings.Clear();foreach(var m in item.Mappings)Mappings.Add(new(m.Field,m.Column)); pendingTemplateMappings=data.Recipients.Count==0;
  LibraryStatus="Template applied. Check column mappings and attachment paths, then validate and preview.";
  return Task.CompletedTask;
 }
 private async Task EditSignatureAsync(LibraryMessage? existing)
 {
  if(library==null)return;
  var item=dialogs.EditSignature(existing,FieldMappings()); if(item==null)return;
  await library.SaveAsync(item);await LoadLibraryAsync();SelectedSignature=Signatures.Single(x=>x.Id==item.Id);
  LibraryStatus="Signature saved. Existing messages keep their inserted copy; append it to a new message when needed.";
 }
 private static string HtmlOrText(string html,string text)=>!string.IsNullOrWhiteSpace(html)?html:"<p>"+WebUtility.HtmlEncode(text).Replace("\r\n","\n").Replace("\n","<br/>")+"</p>";
 private Task AppendSignatureAsync()
 {
  if(SelectedSignature is not {} item)return Task.CompletedTask;
  Html=HtmlOrText(Html,Text)+"\n"+HtmlOrText(item.Template.Html,item.Template.Text);
  Text=Text.TrimEnd()+"\n\n"+item.Template.Text;
  LibraryStatus="Signature appended to the end of the message. Preview it and avoid appending the same signature twice.";
  return Task.CompletedTask;
 }
 private async Task DeleteLibraryAsync(LibraryMessage? item)
 {
  if(library==null||item==null||!dialogs.ConfirmLibraryDelete(item.Name))return;
  await library.DeleteAsync(item.Id);await LoadLibraryAsync(); LibraryStatus="Removed from the library. Your current message is unchanged; the saved file is kept in the local library trash folder.";
 }
}
