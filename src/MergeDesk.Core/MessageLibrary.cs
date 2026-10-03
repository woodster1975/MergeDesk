namespace MergeDesk.Core;
public sealed record LibraryMessage(Guid Id, string Name, bool IsSignature, MergeTemplate Template, FieldMapping[] Mappings);
public interface IMessageLibrary
{
 Task<IReadOnlyList<LibraryMessage>> LoadAsync();
 Task SaveAsync(LibraryMessage message);
 Task DeleteAsync(Guid id);
}
