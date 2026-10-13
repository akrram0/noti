using Noti.Core;

namespace Noti.Storage;

public sealed class MarkdownNoteStore : INoteStore
{
    private string _directoryPath;

    public MarkdownNoteStore(string? directoryPath = null)
    {
        _directoryPath = ResolveDirectoryPath(directoryPath);
    }

    public string DirectoryPath => _directoryPath;

    public void SetDirectoryPath(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = ResolveDirectoryPath(directoryPath);
        Directory.CreateDirectory(_directoryPath);
    }

    public async Task<IReadOnlyList<Note>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);
        var files = Directory.EnumerateFiles(_directoryPath, "*.md", SearchOption.AllDirectories);
        var notes = new List<Note>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relPath = Path.GetRelativePath(_directoryPath, file).Replace('\\', '/');
            if (relPath.StartsWith('.') || relPath.Contains("/.", StringComparison.Ordinal))
                continue;

            var id = relPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? relPath[..^3]
                : relPath;

            var content = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var updatedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
            notes.Add(new Note
            {
                Id = id,
                Content = content,
                UpdatedAt = updatedAt
            });
        }

        return notes.OrderByDescending(n => n.UpdatedAt).ToList();
    }

    public Task<IReadOnlyList<string>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_directoryPath);

        var folders = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(_directoryPath, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(_directoryPath, dir).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(rel) || rel.StartsWith('.') || rel.Contains("/.", StringComparison.Ordinal))
                continue;

            if (rel.Equals("assets", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith("/assets", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories).Any())
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir, recursive: false);
                        continue;
                    }
                }
                catch
                {
                    continue;
                }
            }

            folders.Add(rel);
        }

        folders.Sort(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult<IReadOnlyList<string>>(folders);
    }

    public Task CreateFolderAsync(string relativeFolderPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = NoteService.NormalizeFolderPath(relativeFolderPath);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Folder name is invalid.", nameof(relativeFolderPath));

        var fullFolder = GetSafeFolderPath(normalized);
        Directory.CreateDirectory(fullFolder);
        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(string relativeFolderPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = NoteService.NormalizeFolderPath(relativeFolderPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return Task.CompletedTask;

        var fullFolder = GetSafeFolderPath(normalized);
        if (Directory.Exists(fullFolder))
            Directory.Delete(fullFolder, recursive: true);

        return Task.CompletedTask;
    }

    public async Task SaveAsync(Note note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        Directory.CreateDirectory(_directoryPath);

        var filePath = GetFilePath(note.Id);
        var parentDir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(parentDir))
            Directory.CreateDirectory(parentDir);

        var normalized = (note.Content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
        var tempFilePath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllTextAsync(tempFilePath, normalized, cancellationToken).ConfigureAwait(false);
            File.Move(tempFilePath, filePath, overwrite: true);
            File.SetLastWriteTimeUtc(filePath, note.UpdatedAt.UtcDateTime);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                }
                catch
                {
                }
            }
        }
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = GetFilePath(id);
        if (File.Exists(filePath))
            File.Delete(filePath);

        return Task.CompletedTask;
    }

    public string GetFilePath(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var invalid = Path.GetInvalidFileNameChars();
        var parts = id
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(seg => string.Concat(seg.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim('.'))
            .Where(seg => !string.IsNullOrWhiteSpace(seg))
            .ToArray();

        if (parts.Length == 0)
            throw new ArgumentException("Note ID resolves to an invalid file name.", nameof(id));

        var relative = Path.Combine(parts) + ".md";
        var fullRoot = Path.GetFullPath(_directoryPath);
        var combined = Path.GetFullPath(Path.Combine(fullRoot, relative));

        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Note path escapes the vault directory.", nameof(id));

        return combined;
    }

    private string GetSafeFolderPath(string normalizedFolder)
    {
        var fullRoot = Path.GetFullPath(_directoryPath);
        var combined = Path.GetFullPath(Path.Combine(fullRoot, normalizedFolder.Replace('/', Path.DirectorySeparatorChar)));

        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Folder path escapes the vault directory.", nameof(normalizedFolder));

        return combined;
    }

    private static string ResolveDirectoryPath(string? directoryPath)
    {
        if (!string.IsNullOrWhiteSpace(directoryPath))
            return Path.GetFullPath(directoryPath);

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(docs))
            docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");

        return Path.Combine(docs, "Noti");
    }
}
