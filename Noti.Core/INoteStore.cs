namespace Noti.Core;

public interface INoteStore
{
    Task<IReadOnlyList<Note>> LoadAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(Note note, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetFoldersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    Task CreateFolderAsync(string relativeFolderPath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
