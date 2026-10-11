namespace Noti.Core;

public sealed class NoteService
{
    private static readonly char[] WordSeparators = [' ', '\r', '\n', '\t'];
    private static readonly HashSet<string> LegacyDemoIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "welcome",
        "shopping",
        "ideas"
    };

    private readonly INoteStore _store;

    public NoteService(INoteStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public INoteStore Store => _store;

    public async Task<IReadOnlyList<Note>> LoadNotesAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var userNotes = new List<Note>();

        foreach (var note in existing)
        {
            if (LegacyDemoIds.Contains(note.Id))
            {
                await _store.DeleteAsync(note.Id, cancellationToken).ConfigureAwait(false);
                continue;
            }
            userNotes.Add(note);
        }

        if (userNotes.Count > 0)
            return userNotes.OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.UpdatedAt).ToList();

        var initialNote = CreateNote(string.Empty);
        await _store.SaveAsync(initialNote, cancellationToken).ConfigureAwait(false);
        return [initialNote];
    }

    public Task<IReadOnlyList<string>> GetFoldersAsync(CancellationToken cancellationToken = default) =>
        _store.GetFoldersAsync(cancellationToken);

    public Task CreateFolderAsync(string relativeFolderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFolderPath);
        return _store.CreateFolderAsync(relativeFolderPath, cancellationToken);
    }

    public Task DeleteFolderAsync(string relativeFolderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFolderPath);
        return _store.DeleteFolderAsync(relativeFolderPath, cancellationToken);
    }

    public Note CreateNote(string initialContent = "", string? folderPath = null)
    {
        var baseId = $"note-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..6]}";
        var normalizedFolder = NormalizeFolderPath(folderPath);
        var fullId = string.IsNullOrEmpty(normalizedFolder) ? baseId : $"{normalizedFolder}/{baseId}";

        return new Note
        {
            Id = fullId,
            Content = initialContent,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static string NormalizeFolderPath(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var segments = folderPath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(seg => string.Concat(seg.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim('.'))
            .Where(seg => !string.IsNullOrWhiteSpace(seg))
            .ToArray();

        return string.Join("/", segments);
    }

    public async Task SaveNoteAsync(Note note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        note.UpdatedAt = DateTimeOffset.UtcNow;
        await _store.SaveAsync(note, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteNoteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _store.DeleteAsync(id, cancellationToken);
    }

    public IReadOnlyList<Note> FilterNotes(IEnumerable<Note> notes, string? query)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (string.IsNullOrWhiteSpace(query))
            return notes.ToList();

        var term = query.Trim();
        return notes
            .Where(n =>
                n.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                n.Content.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                n.FolderPath.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
