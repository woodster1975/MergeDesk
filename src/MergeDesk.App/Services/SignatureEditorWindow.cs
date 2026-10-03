using System.Windows;
using System.Windows.Controls;
using MergeDesk.App.Controls;
using MergeDesk.Core;
namespace MergeDesk.App.Services;
public sealed class SignatureEditorWindow : Window
{
 public LibraryMessage? Result { get; private set; }
 public SignatureEditorWindow(LibraryMessage? existing,IReadOnlyList<FieldMapping> mappings)
 {
  Title=existing==null?"New signature":"Edit signature";Width=820;Height=760;MinWidth=620;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel { Margin=new Thickness(24) };Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  panel.Children.Add(new TextBlock {Text="Reusable signature",FontSize=24,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock {Text="Add your name, contact details and optional logo. Merge fields use the message's column mappings.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});
  panel.Children.Add(new TextBlock {Text="Signature name"});
  var name=new TextBox {Text=existing?.Name??"",MaxLength=100,ToolTip="Give this signature a unique name, up to 100 characters."};panel.Children.Add(name);
  var editor=new RichEmailEditor {Html=existing?.Template.Html??"<p>Kind regards,<br/>Your name</p>",Fields=mappings};panel.Children.Add(editor);
  panel.Children.Add(new TextBlock {Text="Plain-text signature"});
  var plain=new TextBox {Text=existing?.Template.Text??"Kind regards,\nYour name",AcceptsReturn=true,Height=90,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,ToolTip="Signature for the plain-text email alternative. The copy button above updates this from the formatted signature."};panel.Children.Add(plain);
  editor.PlainTextRequested+=(_,_)=>plain.Text=editor.PlainText;
  var warning=new TextBlock {TextWrapping=TextWrapping.Wrap};panel.Children.Add(warning);
  var buttons=new WrapPanel();panel.Children.Add(buttons);
  var save=new Button {Content="Save signature",IsDefault=true,ToolTip="Save this signature locally for reuse."};buttons.Children.Add(save);
  var cancel=new Button {Content="Cancel",IsCancel=true,ToolTip="Close without saving changes."};buttons.Children.Add(cancel);
  save.Click+=(_,_)=> { if(string.IsNullOrWhiteSpace(name.Text)||string.IsNullOrWhiteSpace(editor.PlainText)&&!editor.Html.Contains("<img")) { warning.Text="Enter a name and some signature text or an image.";return; }
   Result=new(existing?.Id??Guid.NewGuid(),name.Text.Trim(),true,new("","","","",editor.Html,string.IsNullOrWhiteSpace(plain.Text)?editor.PlainText:plain.Text,[],[]),[]);DialogResult=true; };
 }
}
