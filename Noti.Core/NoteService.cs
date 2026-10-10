namespace Noti.Core;

public sealed class NoteService
{
    private static readonly char[] WordSeparators = [' ', '\r', '\n', '\t'];
    private readonly INoteStore _store;

    public NoteService(INoteStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<IReadOnlyList<Note>> LoadNotesAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
            return existing.OrderByDescending(n => n.UpdatedAt).ToList();

        var now = DateTimeOffset.UtcNow;
        var seeded = new[]
        {
            new Note
            {
                Id = "welcome",
                Content = "Welcome\nOne window, one list, one blank page.\nEvery note is a plain .md file in Documents\\Noti.",
                UpdatedAt = now
            },
            new Note
            {
                Id = "shopping",
                Content = "Shopping\nBread\nOlive oil\nBatteries",
                UpdatedAt = now.AddSeconds(-1)
            },
            new Note
            {
                Id = "ideas",
                Content = "Ideas\nA shortcut that opens a new note instantly from anywhere.",
                UpdatedAt = now.AddSeconds(-2)
            }
        };

        foreach (var note in seeded)
            await _store.SaveAsync(note, cancellationToken).ConfigureAwait(false);

        return seeded;
    }

    public Note CreateNote(string initialContent = "Untitled\n")
    {
        return new Note
        {
            Id = $"note-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..6]}",
            Content = initialContent,
            UpdatedAt = DateTimeOffset.UtcNow
        };
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
                n.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
