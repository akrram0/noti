using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Noti.Core;

namespace Noti.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly NoteService _noteService;
    private readonly List<NoteItemViewModel> _allNotes = new();
    private CancellationTokenSource? _autosaveCts;
    private bool _suppressAutosave;

    private NoteItemViewModel? _selectedNote;
    private string _searchQuery = string.Empty;
    private string _editorText = string.Empty;
    private string _saveStatusText = "Saved";
    private bool _isSidebarOpen = true;
    private bool _isSettingsOpen;
    private bool _isDarkMode = true;

    public MainViewModel(NoteService noteService)
    {
        ArgumentNullException.ThrowIfNull(noteService);
        _noteService = noteService;

        CreateNoteCommand = new AsyncRelayCommand(CreateNoteAsync);
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarOpen = !IsSidebarOpen);
        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        ToggleThemeCommand = new RelayCommand(() => IsDarkMode = !IsDarkMode);
    }

    public ObservableCollection<NoteItemViewModel> FilteredNotes { get; } = new();

    public IAsyncRelayCommand CreateNoteCommand { get; }
    public IRelayCommand ToggleSidebarCommand { get; }
    public IRelayCommand ToggleSettingsCommand { get; }
    public IRelayCommand ToggleThemeCommand { get; }

    public NoteItemViewModel? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (SetProperty(ref _selectedNote, value))
            {
                foreach (var item in _allNotes)
                    item.IsSelected = ReferenceEquals(item, value);

                _suppressAutosave = true;
                EditorText = value?.Content ?? string.Empty;
                _suppressAutosave = false;
            }
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
                RefreshFilteredNotes();
        }
    }

    public string EditorText
    {
        get => _editorText;
        set
        {
            if (SetProperty(ref _editorText, value))
            {
                OnPropertyChanged(nameof(WordCountText));

                if (_selectedNote is not null && !_suppressAutosave)
                {
                    _selectedNote.Content = value;
                    QueueAutosave(_selectedNote.Model);
                }
            }
        }
    }

    public string NoteCountText => _allNotes.Count == 1 ? "1 note" : $"{_allNotes.Count} notes";

    public string WordCountText
    {
        get
        {
            var words = NoteService.CountWords(_editorText);
            return words == 1 ? "1 word" : $"{words} words";
        }
    }

    public string SaveStatusText
    {
        get => _saveStatusText;
        private set => SetProperty(ref _saveStatusText, value);
    }

    public bool IsSidebarOpen
    {
        get => _isSidebarOpen;
        set
        {
            if (SetProperty(ref _isSidebarOpen, value))
            {
                OnPropertyChanged(nameof(SidebarWidth));
                OnPropertyChanged(nameof(SidebarVisibility));
            }
        }
    }

    public GridLength SidebarWidth => _isSidebarOpen ? new GridLength(280) : new GridLength(0);
    public Visibility SidebarVisibility => _isSidebarOpen ? Visibility.Visible : Visibility.Collapsed;

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (SetProperty(ref _isSettingsOpen, value))
                OnPropertyChanged(nameof(SettingsVisibility));
        }
    }

    public Visibility SettingsVisibility => _isSettingsOpen ? Visibility.Visible : Visibility.Collapsed;

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                OnPropertyChanged(nameof(CurrentElementTheme));
                OnPropertyChanged(nameof(ToggleThumbAlignment));
            }
        }
    }

    public ElementTheme CurrentElementTheme => _isDarkMode ? ElementTheme.Dark : ElementTheme.Light;
    public HorizontalAlignment ToggleThumbAlignment => _isDarkMode ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public async Task InitializeAsync()
    {
        var loaded = await _noteService.LoadNotesAsync();
        _allNotes.Clear();
        foreach (var note in loaded)
            _allNotes.Add(new NoteItemViewModel(note));

        RefreshFilteredNotes();
        SelectedNote = FilteredNotes.FirstOrDefault();
        OnPropertyChanged(nameof(NoteCountText));
    }

    private async Task CreateNoteAsync()
    {
        var note = _noteService.CreateNote(string.Empty);
        await _noteService.SaveNoteAsync(note);

        var vm = new NoteItemViewModel(note);
        _allNotes.Insert(0, vm);

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            _searchQuery = string.Empty;
            OnPropertyChanged(nameof(SearchQuery));
        }

        RefreshFilteredNotes();
        SelectedNote = vm;
        SaveStatusText = "Saved";
        OnPropertyChanged(nameof(NoteCountText));
    }

    private void RefreshFilteredNotes()
    {
        var previousSelection = _selectedNote;
        var matchingModels = _noteService
            .FilterNotes(_allNotes.Select(vm => vm.Model), _searchQuery)
            .ToHashSet();

        FilteredNotes.Clear();
        foreach (var vm in _allNotes)
        {
            if (matchingModels.Contains(vm.Model))
                FilteredNotes.Add(vm);
        }

        if (previousSelection is not null && FilteredNotes.Contains(previousSelection))
            SelectedNote = previousSelection;
        else
            SelectedNote = FilteredNotes.FirstOrDefault();
    }

    private void QueueAutosave(Note note)
    {
        _autosaveCts?.Cancel();
        _autosaveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _autosaveCts = cts;

        SaveStatusText = "Saving...";
        _ = RunAutosaveAsync(note, cts.Token);
    }

    private async Task RunAutosaveAsync(Note note, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            await _noteService.SaveNoteAsync(note, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
                SaveStatusText = "Saved";
        }
        catch (OperationCanceledException)
        {
        }
    }
}
