namespace Noti.Core;

public interface INoteStore
{
    Task<IReadOnlyList<Note>> LoadAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(Note note, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
