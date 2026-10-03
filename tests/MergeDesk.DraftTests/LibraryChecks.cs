using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MergeDesk.Core;
using MergeDesk.Infrastructure;
using MergeDesk.App.Services;
using MergeDesk.App.ViewModels;
using MergeDesk.App.Controls;
internal static class LibraryChecks
{
 static int count;
 static void Check(bool ok,string name) { if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name); }
 static void Wait(Task task) {var end=DateTime.UtcNow.AddSeconds(20);while(!task.IsCompleted&&DateTime.UtcNow<end){Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Thread.Sleep(5);}task.GetAwaiter().GetResult();}
 static void Execute(AsyncCommand c) {Check(c.CanExecute(null),"library command available");c.Execute(null);var end=DateTime.UtcNow.AddSeconds(20);while(!c.CanExecute(null)&&DateTime.UtcNow<end){Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Thread.Sleep(5);}if(!c.CanExecute(null))throw new TimeoutException();}
 public static int Run(string root)
 {
  count=0;var dir=Path.Combine(root,"library");var store=new JsonMessageLibrary(dir);
  var item=new LibraryMessage(Guid.NewGuid(),"Greeting",false,new("{{Address}}","","","Hi {{Person}}","<p>Hello {{Person}}</p>","Hello {{Person}}",[],[]),[new("Person","Name"),new("Address","Email")]);
  Wait(store.SaveAsync(item));var restart=new JsonMessageLibrary(dir);var load=restart.LoadAsync();Wait(load);Check(load.Result.Single().Mappings[0].Column=="Name","template mappings persist across restart");
  var duplicate=store.SaveAsync(item with {Id=Guid.NewGuid(),Name="greeting"});try{Wait(duplicate);throw new Exception("duplicate accepted");}catch(InvalidOperationException){}Check(Directory.GetFiles(dir,"*.json").Length==1,"duplicate name does not overwrite existing template");
  File.WriteAllText(Path.Combine(dir,Guid.NewGuid().ToString("N")+".json"),"damaged");Wait(load=store.LoadAsync());Check(load.Result.Count==1,"damaged entry is preserved and does not block other templates");
  var source=Path.Combine(root,"library.csv");File.WriteAllText(source,"Email,Name\nalex@example.com,Alex\njordan@example.com,Jordan\n");
  var dialogs=new Dialogs(source);var model=new MainViewModel(new RecipientReader(),new SqliteRunRepository(Path.Combine(root,"library-history.db")),dialogs,messageLibrary:store);
  Wait(model.LoadLibraryAsync());model.SelectedTemplate=model.Templates.Single();Execute(model.UseTemplateCommand);Execute(model.ImportCommand);
  Check(model.Mappings.Any(m=>m.Field=="Person"&&m.Column=="Name"),"template loaded before recipients preserves aliases on import");
  model.ExcludeAllCommand.Execute(null);var snapshot=model.ProjectSnapshot();model.SelectedConnection=1;Execute(model.UseTemplateCommand);
  Check(model.IncludedCount==0&&model.SourcePath==source&&model.SelectedConnection==1,"applying template preserves recipient selection source and account choice");
  model.Html="<p>Updated</p>";model.TemplateName="Greeting";Execute(model.SaveTemplateCommand);Wait(load=store.LoadAsync());Check(load.Result.Single().Id==item.Id&&load.Result.Single().Template.Html.Contains("Updated"),"saving selected template updates same identity");
  var sig=new LibraryMessage(Guid.NewGuid(),"Team",true,new("","","","","<p>Regards {{Person}}</p>","Regards {{Person}}",[],[]),[]);Wait(store.SaveAsync(sig));Wait(model.LoadLibraryAsync());model.SelectedSignature=model.Signatures.Single();model.Html="";model.Text="Plain body <kept>";Execute(model.AppendSignatureCommand);
  Check(model.Html.Contains("Plain body &lt;kept&gt;")&&model.Html.Contains("{{Person}}")&&model.Text.Contains("Regards"),"signature preserves a plain-text-only message and appends both bodies");
  Wait(store.SaveAsync(sig with {Template=sig.Template with {Html="<p>Changed</p>"}}));Check(!model.Html.Contains("Changed"),"editing signature does not change already inserted messages");
  Wait(store.DeleteAsync(sig.Id));Wait(load=store.LoadAsync());Check(load.Result.Count==1&&Directory.GetFiles(Path.Combine(dir,"trash")).Length==1,"removal preserves saved item in trash and keeps other templates");
  var png="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
  var editor=new RichEmailEditor {Html="<p>Picture<img src=\""+png+"\" width=\"100\"/></p>"};
  var picker=(ComboBox)editor.FindName("PicturePicker");Check(picker.Items.Count==1,"existing embedded picture is available in picture settings");
  ((TextBox)editor.FindName("PictureWidth")).Text="240";((TextBox)editor.FindName("PictureDescription")).Text="Logo & team";
  Invoke(editor,"ApplyPicture");Check(editor.Html.Contains("width=\"240\"")&&editor.Html.Contains("height=\"240\"")&&editor.Html.Contains("Logo &amp; team"),"picture resize preserves aspect ratio and encodes alternative text");
  var before=editor.Html;((TextBox)editor.FindName("PictureWidth")).Text="NaN";Invoke(editor,"ApplyPicture");Check(editor.Html==before,"invalid picture size leaves body unchanged");
  Invoke(editor,"AlignPicture",new Button {Tag="Center"});Check(editor.Html.Contains("text-align:center"),"picture paragraph alignment is saved in HTML");
  var reopened=new RichEmailEditor {Html=editor.Html};Check(((ComboBox)reopened.FindName("PicturePicker")).Items.Count==1&&((TextBox)reopened.FindName("PictureWidth")).Text=="240","resized picture survives editor reload");
  Invoke(editor,"OriginalPicture");Check(editor.Html.Contains("width=\"1\""),"original size restores source dimensions");
  editor.Html="<p>Removed</p>";Check(((ComboBox)editor.FindName("PicturePicker")).Items.Count==0,"removed picture is cleared from picture controls");
  var signatureWindow=new SignatureEditorWindow(null,[]);signatureWindow.Measure(new Size(820,760));signatureWindow.Arrange(new Rect(0,0,820,760));Check(signatureWindow.Content!=null,"signature editor lays out without a mail connection");model.CloseConnection();return count;
 }
 static void Invoke(RichEmailEditor editor,string method,Button? button=null)=>typeof(RichEmailEditor).GetMethod(method,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(editor,[button??new Button(),new RoutedEventArgs()]);
 sealed class Dialogs(string path):IDesktopDialogs {public string? OpenSource()=>path;public string[] OpenAttachments()=>[];public string? ChooseOutput()=>null;public string? ProjectFile(bool save)=>null;}
}
