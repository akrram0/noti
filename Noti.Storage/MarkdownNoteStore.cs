using Noti.Core;

namespace Noti.Storage;

public sealed class MarkdownNoteStore : INoteStore
{
    private readonly string _directoryPath;

    public MarkdownNoteStore(string? directoryPath = null)
    {
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            _directoryPath = directoryPath;
        }
        else
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(docs))
                docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");

            _directoryPath = Path.Combine(docs, "Noti");
        }
    }

    public string DirectoryPath => _directoryPath;

    public async Task<IReadOnlyList<Note>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);
        var files = Directory.EnumerateFiles(_directoryPath, "*.md", SearchOption.TopDirectoryOnly);
        var notes = new List<Note>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var updatedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
            notes.Add(new Note
            {
                Id = Path.GetFileNameWithoutExtension(file),
                Content = content,
                UpdatedAt = updatedAt
            });
        }

        return notes.OrderByDescending(n => n.UpdatedAt).ToList();
    }

    public async Task SaveAsync(Note note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        Directory.CreateDirectory(_directoryPath);

        var filePath = GetFilePath(note.Id);
        var normalized = (note.Content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
        await File.WriteAllTextAsync(filePath, normalized, cancellationToken).ConfigureAwait(false);
        File.SetLastWriteTimeUtc(filePath, note.UpdatedAt.UtcDateTime);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = GetFilePath(id);
        if (File.Exists(filePath))
            File.Delete(filePath);

        return Task.CompletedTask;
    }

    private string GetFilePath(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var safeName = string.Concat(Path.GetFileName(id).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName))
            throw new ArgumentException("Note ID resolves to an invalid file name.", nameof(id));

        return Path.Combine(_directoryPath, $"{safeName}.md");
    }
}
