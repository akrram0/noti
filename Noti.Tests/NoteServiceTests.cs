using Noti.Core;
using Noti.Storage;
using Xunit;

namespace Noti.Tests;

public sealed class NoteServiceTests
{
    [Fact]
    public async Task LoadNotesAsync_WhenStoreEmpty_SeedsDefaultNotesMatchingSpec()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"noti-tests-{Guid.NewGuid():N}");
        try
        {
            var store = new MarkdownNoteStore(tempDir);
            var service = new NoteService(store);

            var notes = await service.LoadNotesAsync();

            Assert.Equal(3, notes.Count);
            Assert.Equal("Welcome", notes[0].Title);
            Assert.Equal("One window, one list, one blank page. ...", notes[0].Snippet);
            Assert.Equal(17, notes[0].WordCount);

            Assert.Equal("Shopping", notes[1].Title);
            Assert.Equal("Bread Olive oil Batteries", notes[1].Snippet);

            Assert.Equal("Ideas", notes[2].Title);
            Assert.Equal("A shortcut that opens a new note insta...", notes[2].Snippet);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FilterNotes_MatchesTitleOrContent_CaseInsensitive()
    {
        var store = new InMemoryNoteStore();
        var service = new NoteService(store);
        var notes = new[]
        {
            new Note { Id = "1", Content = "Shopping\nBread\nOlive oil" },
            new Note { Id = "2", Content = "Ideas\nBuild a minimal markdown app" }
        };

        var matchByContent = service.FilterNotes(notes, "olive");
        Assert.Single(matchByContent);
        Assert.Equal("Shopping", matchByContent[0].Title);

        var matchByTitle = service.FilterNotes(notes, "IDEAS");
        Assert.Single(matchByTitle);
        Assert.Equal("Ideas", matchByTitle[0].Title);

        var allWhenBlank = service.FilterNotes(notes, "   ");
        Assert.Equal(2, allWhenBlank.Count);
    }

    [Fact]
    public async Task MarkdownNoteStore_SavesLoadsAndDeletesMarkdownFilesSafely()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"noti-store-{Guid.NewGuid():N}");
        try
        {
            var store = new MarkdownNoteStore(tempDir);
            var service = new NoteService(store);

            var created = service.CreateNote("# Project Plan\nShip unpackaged WinUI build.");
            await service.SaveNoteAsync(created);

            var loaded = await store.LoadAllAsync();
            Assert.Single(loaded);
            Assert.Equal("Project Plan", loaded[0].Title);
            Assert.Equal("Ship unpackaged WinUI build.", loaded[0].Snippet);

            await service.DeleteNoteAsync(created.Id);
            var afterDelete = await store.LoadAllAsync();
            Assert.Empty(afterDelete);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class InMemoryNoteStore : INoteStore
    {
        private readonly Dictionary<string, Note> _notes = new();

        public Task<IReadOnlyList<Note>> LoadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Note>>(_notes.Values.ToList());

        public Task SaveAsync(Note note, CancellationToken cancellationToken = default)
        {
            _notes[note.Id] = note;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            _notes.Remove(id);
            return Task.CompletedTask;
        }
    }
}
