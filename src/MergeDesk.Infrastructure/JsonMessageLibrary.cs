using System.Text.Json;
using MergeDesk.Core;
namespace MergeDesk.Infrastructure;
/// <summary>One atomic file per entry; IDs, never user names, form file paths.</summary>
public sealed class JsonMessageLibrary(string directory) : IMessageLibrary
{
 public async Task<IReadOnlyList<LibraryMessage>> LoadAsync()
 {
  if(!Directory.Exists(directory)) return [];
  var result = new List<LibraryMessage>();
  foreach(var file in Directory.EnumerateFiles(directory, "*.json"))
  {
   try { var item = JsonSerializer.Deserialize<LibraryMessage>(await File.ReadAllTextAsync(file));
    if(item != null && item.Id != Guid.Empty && Path.GetFileNameWithoutExtension(file) == item.Id.ToString("N") && !string.IsNullOrWhiteSpace(item.Name) && item.Template != null && item.Mappings != null) result.Add(item);
   } catch(JsonException) { /* Preserve damaged files for recovery; other entries remain available. */ }
  }
  return result.OrderBy(x=>x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
 }
 public async Task SaveAsync(LibraryMessage item)
 {
  if(item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 100) throw new ArgumentException("Enter a name between 1 and 100 characters.");
  var entries = await LoadAsync();
  if(entries.Any(x=>x.Id != item.Id && x.IsSignature == item.IsSignature && string.Equals(x.Name,item.Name,StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("That name already exists. Select it to update it, or choose a different name.");
  Directory.CreateDirectory(directory);
  var target=Path.Combine(directory,item.Id.ToString("N")+".json"); var temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
  try { await using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { await JsonSerializer.SerializeAsync(stream,item); stream.Flush(true); } File.Move(temp,target,true); }
  finally { if(File.Exists(temp)) File.Delete(temp); }
 }
 public Task DeleteAsync(Guid id)
 {
  var file=Path.Combine(directory,id.ToString("N")+".json");
  if(File.Exists(file)) { var trash=Path.Combine(directory,"trash"); Directory.CreateDirectory(trash); File.Move(file,Path.Combine(trash,id.ToString("N")+"-"+Guid.NewGuid().ToString("N")+".json")); }
  return Task.CompletedTask;
 }
}
