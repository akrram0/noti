using Noti.Core;
using Noti.Storage;
using Xunit;

namespace Noti.Tests;

public sealed class NoteServiceTests
{
    [Fact]
    public async Task LoadNotesAsync_WhenStoreEmpty_CreatesSingleBlankNote()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"noti-tests-{Guid.NewGuid():N}");
        try
        {
            var store = new MarkdownNoteStore(tempDir);
            var service = new NoteService(store);

            var notes = await service.LoadNotesAsync();

            Assert.Single(notes);
            Assert.Equal("Untitled", notes[0].Title);
            Assert.Equal("No additional text", notes[0].Snippet);
            Assert.Equal(0, notes[0].WordCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Note_ComputesTitleSnippetAndWordCountCorrectly()
    {
        var note = new Note
        {
            Id = "test",
            Content = "Welcome\nOne window, one list, one blank page.\nEvery note is a plain .md file in Documents\\Noti."
        };

        Assert.Equal("Welcome", note.Title);
        Assert.Equal("One window, one list, one blank page. ...", note.Snippet);
        Assert.Equal(17, note.WordCount);
    }

    [Fact]
    public void Note_StripsTreeAndMarkdownSymbolsFromSnippet()
    {
        var note = new Note
        {
            Id = "Work/Roadmap",
            Content = "# Project Roadmap\n├─ Design UI\n│\n└── Ship installer"
        };

        Assert.Equal("Work", note.FolderPath);
        Assert.Equal("Project Roadmap", note.Title);
        Assert.Equal("Design UI Ship installer", note.Snippet);
    }

    [Fact]
    public void FilterNotes_MatchesTitleContentOrFolder_CaseInsensitive()
    {
        var store = new InMemoryNoteStore();
        var service = new NoteService(store);
        var notes = new[]
        {
            new Note { Id = "Home/1", Content = "Shopping\nBread\nOlive oil" },
            new Note { Id = "Work/2", Content = "Ideas\nBuild a minimal markdown app" }
        };

        var matchByContent = service.FilterNotes(notes, "olive");
        Assert.Single(matchByContent);
        Assert.Equal("Shopping", matchByContent[0].Title);

        var matchByTitle = service.FilterNotes(notes, "IDEAS");
        Assert.Single(matchByTitle);
        Assert.Equal("Ideas", matchByTitle[0].Title);

        var matchByFolder = service.FilterNotes(notes, "work");
        Assert.Single(matchByFolder);
        Assert.Equal("Ideas", matchByFolder[0].Title);

        var allWhenBlank = service.FilterNotes(notes, "   ");
        Assert.Equal(2, allWhenBlank.Count);
    }

    [Fact]
    public async Task MarkdownNoteStore_SupportsNestedFoldersAndMarkdownFilesSafely()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"noti-store-{Guid.NewGuid():N}");
        try
        {
            var store = new MarkdownNoteStore(tempDir);
            var service = new NoteService(store);

            await service.CreateFolderAsync("Work/Specs");
            var folders = await service.GetFoldersAsync();
            Assert.Contains("Work", folders);
            Assert.Contains("Work/Specs", folders);

            var created = service.CreateNote("# Project Plan\nShip unpackaged WinUI build.", "Work/Specs");
            await service.SaveNoteAsync(created);

            var loaded = await store.LoadAllAsync();
            Assert.Single(loaded);
            Assert.Equal("Work/Specs", loaded[0].FolderPath);
            Assert.Equal("Project Plan", loaded[0].Title);
            Assert.Equal("Ship unpackaged WinUI build.", loaded[0].Snippet);

            await service.DeleteFolderAsync("Work");
            var afterFolderDelete = await store.LoadAllAsync();
            Assert.Empty(afterFolderDelete);
            Assert.Empty(await service.GetFoldersAsync());
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
