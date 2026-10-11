using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Noti.Core;
using Noti.Storage;

namespace Noti.App.ViewModels;

public enum EditorViewMode
{
    Write = 0,
    Split = 1,
    View = 2
}

public sealed class MainViewModel : ObservableObject
{
    private readonly NoteService _noteService;
    private readonly AppSettings _settings;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly List<NoteItemViewModel> _allNotes = new();
    private readonly HashSet<string> _knownFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pinnedNoteIds = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _autosaveCts;
    private bool _suppressAutosave;

    private NoteItemViewModel? _selectedNote;
    private string _activeFolderPath = string.Empty;
    private string _searchQuery = string.Empty;
    private string _editorText = string.Empty;
    private string _saveStatusText = "Saved";
    private bool _isSidebarOpen = true;
    private bool _isSettingsOpen;
    private bool _isCreatingFolder;
    private string _newFolderName = string.Empty;
    private bool _isDarkMode = true;
    private bool _isFluidAnimationEnabled = true;
    private bool _isTreeShortcutEnabled = true;
    private double _editorFontSize = 16.0;
    private EditorViewMode _currentViewMode = EditorViewMode.Write;

    public MainViewModel(NoteService noteService, AppSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(noteService);
        _noteService = noteService;
        _settings = settings ?? AppSettings.Load();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _isDarkMode = _settings.IsDarkMode;
        _isFluidAnimationEnabled = _settings.IsFluidAnimationEnabled;
        _isTreeShortcutEnabled = _settings.IsTreeShortcutEnabled;
        _editorFontSize = Math.Clamp(_settings.EditorFontSize, 12.0, 24.0);

        foreach (var id in _settings.PinnedNoteIds)
            _pinnedNoteIds.Add(id);
        foreach (var folder in _settings.CollapsedFolders)
            _collapsedFolders.Add(folder);

        if (!string.IsNullOrWhiteSpace(_settings.VaultDirectoryPath) && _noteService.Store is MarkdownNoteStore mdStore)
            mdStore.SetDirectoryPath(_settings.VaultDirectoryPath);

        CreateNoteCommand = new AsyncRelayCommand(() => CreateNoteInFolderAsync(_activeFolderPath));
        StartNewFolderCommand = new RelayCommand(StartNewFolder);
        ConfirmNewFolderCommand = new AsyncRelayCommand(ConfirmNewFolderAsync);
        CancelNewFolderCommand = new RelayCommand(() =>
        {
            IsCreatingFolder = false;
            NewFolderName = string.Empty;
        });
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarOpen = !IsSidebarOpen);
        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        ToggleThemeCommand = new RelayCommand(() => IsDarkMode = !IsDarkMode);
        ToggleFluidAnimationCommand = new RelayCommand(() => IsFluidAnimationEnabled = !IsFluidAnimationEnabled);
        ToggleTreeShortcutCommand = new RelayCommand(() => IsTreeShortcutEnabled = !IsTreeShortcutEnabled);
        SetWriteModeCommand = new RelayCommand(() => CurrentViewMode = EditorViewMode.Write);
        SetSplitModeCommand = new RelayCommand(() => CurrentViewMode = EditorViewMode.Split);
        SetViewModeCommand = new RelayCommand(() => CurrentViewMode = EditorViewMode.View);
        CycleViewModeCommand = new RelayCommand(() =>
        {
            CurrentViewMode = CurrentViewMode switch
            {
                EditorViewMode.Write => EditorViewMode.Split,
                EditorViewMode.Split => EditorViewMode.View,
                _ => EditorViewMode.Write
            };
        });
        DecreaseFontSizeCommand = new RelayCommand(() => EditorFontSize = Math.Max(13.0, EditorFontSize - 1.0));
        IncreaseFontSizeCommand = new RelayCommand(() => EditorFontSize = Math.Min(22.0, EditorFontSize + 1.0));
        OpenVaultFolderCommand = new RelayCommand(OpenVaultFolderInExplorer);
        DeleteSelectedNoteCommand = new AsyncRelayCommand(() => DeleteNoteAsync(_selectedNote));
    }

    public ObservableCollection<NoteItemViewModel> FilteredNotes { get; } = new();

    public IAsyncRelayCommand CreateNoteCommand { get; }
    public IRelayCommand StartNewFolderCommand { get; }
    public IAsyncRelayCommand ConfirmNewFolderCommand { get; }
    public IRelayCommand CancelNewFolderCommand { get; }
    public IRelayCommand ToggleSidebarCommand { get; }
    public IRelayCommand ToggleSettingsCommand { get; }
    public IRelayCommand CloseSettingsCommand { get; }
    public IRelayCommand ToggleThemeCommand { get; }
    public IRelayCommand ToggleFluidAnimationCommand { get; }
    public IRelayCommand ToggleTreeShortcutCommand { get; }
    public IRelayCommand SetWriteModeCommand { get; }
    public IRelayCommand SetSplitModeCommand { get; }
    public IRelayCommand SetViewModeCommand { get; }
    public IRelayCommand CycleViewModeCommand { get; }
    public IRelayCommand DecreaseFontSizeCommand { get; }
    public IRelayCommand IncreaseFontSizeCommand { get; }
    public IRelayCommand OpenVaultFolderCommand { get; }
    public IAsyncRelayCommand DeleteSelectedNoteCommand { get; }

    public NoteItemViewModel? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (value is { IsFolder: true })
            {
                ToggleFolderExpansion(value.FolderPath);
                return;
            }

            if (SetProperty(ref _selectedNote, value))
            {
                foreach (var item in FilteredNotes)
                    item.IsSelected = !item.IsFolder && ReferenceEquals(item, value);

                if (value is not null)
                {
                    _activeFolderPath = value.FolderPath;
                    OnPropertyChanged(nameof(ActiveFolderBadgeText));
                }

                _suppressAutosave = true;
                EditorText = value?.Content ?? string.Empty;
                _suppressAutosave = false;
            }
        }
    }

    public string ActiveFolderPath
    {
        get => _activeFolderPath;
        set
        {
            if (SetProperty(ref _activeFolderPath, value ?? string.Empty))
                OnPropertyChanged(nameof(ActiveFolderBadgeText));
        }
    }

    public string ActiveFolderBadgeText =>
        string.IsNullOrEmpty(_activeFolderPath) ? "Root Vault" : _activeFolderPath;

    public bool IsCreatingFolder
    {
        get => _isCreatingFolder;
        set
        {
            if (SetProperty(ref _isCreatingFolder, value))
                OnPropertyChanged(nameof(NewFolderBoxVisibility));
        }
    }

    public Visibility NewFolderBoxVisibility => _isCreatingFolder ? Visibility.Visible : Visibility.Collapsed;

    public string NewFolderName
    {
        get => _newFolderName;
        set => SetProperty(ref _newFolderName, value);
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

                if (_suppressAutosave)
                    return;

                if (_selectedNote is null && !string.IsNullOrEmpty(value))
                {
                    var note = _noteService.CreateNote(value, _activeFolderPath);
                    var vm = new NoteItemViewModel(note) { IsSelected = true };
                    _allNotes.Insert(0, vm);
                    _selectedNote = vm;
                    RefreshFilteredNotes();
                    OnPropertyChanged(nameof(SelectedNote));
                    OnPropertyChanged(nameof(NoteCountText));
                }

                if (_selectedNote?.Model is not null)
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
        set => SetProperty(ref _isSidebarOpen, value);
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                _settings.IsDarkMode = value;
                _settings.Save();
                OnPropertyChanged(nameof(CurrentElementTheme));
                OnPropertyChanged(nameof(ToggleThumbAlignment));
            }
        }
    }

    public ElementTheme CurrentElementTheme => _isDarkMode ? ElementTheme.Dark : ElementTheme.Light;
    public HorizontalAlignment ToggleThumbAlignment => _isDarkMode ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public bool IsFluidAnimationEnabled
    {
        get => _isFluidAnimationEnabled;
        set
        {
            if (SetProperty(ref _isFluidAnimationEnabled, value))
            {
                _settings.IsFluidAnimationEnabled = value;
                _settings.Save();
                OnPropertyChanged(nameof(FluidToggleThumbAlignment));
            }
        }
    }

    public HorizontalAlignment FluidToggleThumbAlignment => _isFluidAnimationEnabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public bool IsTreeShortcutEnabled
    {
        get => _isTreeShortcutEnabled;
        set
        {
            if (SetProperty(ref _isTreeShortcutEnabled, value))
            {
                _settings.IsTreeShortcutEnabled = value;
                _settings.Save();
                OnPropertyChanged(nameof(TreeToggleThumbAlignment));
            }
        }
    }

    public HorizontalAlignment TreeToggleThumbAlignment => _isTreeShortcutEnabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public double EditorFontSize
    {
        get => _editorFontSize;
        set
        {
            if (SetProperty(ref _editorFontSize, value))
            {
                _settings.EditorFontSize = value;
                _settings.Save();
                OnPropertyChanged(nameof(EditorFontSizeText));
            }
        }
    }

    public string EditorFontSizeText => $"{_editorFontSize:0} px";

    public EditorViewMode CurrentViewMode
    {
        get => _currentViewMode;
        set
        {
            if (SetProperty(ref _currentViewMode, value))
            {
                OnPropertyChanged(nameof(EditorColumnWidth));
                OnPropertyChanged(nameof(PreviewColumnWidth));
                OnPropertyChanged(nameof(EditorPaneVisibility));
                OnPropertyChanged(nameof(PreviewPaneVisibility));
                OnPropertyChanged(nameof(SplitDividerVisibility));
                OnPropertyChanged(nameof(ModeIndicatorOffsetX));
            }
        }
    }

    public GridLength EditorColumnWidth => _currentViewMode == EditorViewMode.View ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
    public GridLength PreviewColumnWidth => _currentViewMode == EditorViewMode.Write ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
    public Visibility EditorPaneVisibility => _currentViewMode == EditorViewMode.View ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PreviewPaneVisibility => _currentViewMode == EditorViewMode.Write ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SplitDividerVisibility => _currentViewMode == EditorViewMode.Split ? Visibility.Visible : Visibility.Collapsed;
    public double ModeIndicatorOffsetX => (int)_currentViewMode * 64.0;

    public string VaultDirectoryPath =>
        (_noteService.Store as MarkdownNoteStore)?.DirectoryPath ?? _settings.VaultDirectoryPath;

    public async Task InitializeAsync()
    {
        var folders = await _noteService.GetFoldersAsync();
        _knownFolders.Clear();
        foreach (var f in folders)
            AddFolderHierarchy(f);

        var loaded = await _noteService.LoadNotesAsync();
        _allNotes.Clear();
        foreach (var note in loaded)
        {
            note.IsPinned = _pinnedNoteIds.Contains(note.Id);
            if (!string.IsNullOrEmpty(note.FolderPath))
                AddFolderHierarchy(note.FolderPath);

            _allNotes.Add(new NoteItemViewModel(note));
        }

        RefreshFilteredNotes();
        SelectedNote = FilteredNotes.FirstOrDefault(n => !n.IsFolder);
        OnPropertyChanged(nameof(NoteCountText));
        OnPropertyChanged(nameof(VaultDirectoryPath));
    }

    public void ToggleFolderExpansion(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        ActiveFolderPath = folderPath;
        if (!_collapsedFolders.Remove(folderPath))
            _collapsedFolders.Add(folderPath);

        _settings.CollapsedFolders = _collapsedFolders.ToList();
        _settings.Save();
        RefreshFilteredNotes();
    }

    public async Task CreateNoteInFolderAsync(string? folderPath = null)
    {
        var targetFolder = NoteService.NormalizeFolderPath(folderPath ?? _activeFolderPath);
        if (!string.IsNullOrEmpty(targetFolder))
        {
            AddFolderHierarchy(targetFolder);
            _collapsedFolders.Remove(targetFolder);
        }

        var note = _noteService.CreateNote(string.Empty, targetFolder);
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
        SetSaveStatus("Saved");
        OnPropertyChanged(nameof(NoteCountText));
    }

    public void TogglePinNote(NoteItemViewModel? item)
    {
        if (item is null || item.IsFolder)
            return;

        item.IsPinned = !item.IsPinned;
        if (item.IsPinned)
            _pinnedNoteIds.Add(item.Id);
        else
            _pinnedNoteIds.Remove(item.Id);

        _settings.PinnedNoteIds = _pinnedNoteIds.ToList();
        _settings.Save();
        RefreshFilteredNotes();
    }

    public async Task DeleteNoteAsync(NoteItemViewModel? item)
    {
        var target = item ?? _selectedNote;
        if (target is null || target.IsFolder)
            return;

        await _noteService.DeleteNoteAsync(target.Id);
        _allNotes.Remove(target);
        _pinnedNoteIds.Remove(target.Id);
        _settings.PinnedNoteIds = _pinnedNoteIds.ToList();
        _settings.Save();

        if (_allNotes.Count == 0)
        {
            var blank = _noteService.CreateNote(string.Empty);
            await _noteService.SaveNoteAsync(blank);
            _allNotes.Add(new NoteItemViewModel(blank));
        }

        RefreshFilteredNotes();
        SelectedNote = FilteredNotes.FirstOrDefault(n => !n.IsFolder);
        SetSaveStatus("Saved");
        OnPropertyChanged(nameof(NoteCountText));
    }

    public async Task ImportMarkdownFileAsync(string externalFilePath)
    {
        if (string.IsNullOrWhiteSpace(externalFilePath) || !File.Exists(externalFilePath))
            return;

        var content = await File.ReadAllTextAsync(externalFilePath);
        var fileTitle = Path.GetFileNameWithoutExtension(externalFilePath);
        if (!string.IsNullOrWhiteSpace(content) && !content.TrimStart().StartsWith('#'))
            content = $"# {fileTitle}\n{content}";
        else if (string.IsNullOrWhiteSpace(content))
            content = $"# {fileTitle}\n";

        var note = _noteService.CreateNote(content, _activeFolderPath);
        await _noteService.SaveNoteAsync(note);

        var vm = new NoteItemViewModel(note);
        _allNotes.Insert(0, vm);
        RefreshFilteredNotes();
        SelectedNote = vm;
        SetSaveStatus("Imported");
        OnPropertyChanged(nameof(NoteCountText));
    }

    public async Task ExportCurrentNoteAsync(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            return;

        var content = (_editorText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
        await File.WriteAllTextAsync(destinationPath, content);
        SetSaveStatus("Exported");
    }

    public async Task ChangeVaultDirectoryAsync(string newDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(newDirectoryPath))
            return;

        if (_noteService.Store is MarkdownNoteStore mdStore)
        {
            mdStore.SetDirectoryPath(newDirectoryPath);
            _settings.VaultDirectoryPath = mdStore.DirectoryPath;
            _settings.Save();
            OnPropertyChanged(nameof(VaultDirectoryPath));
            await InitializeAsync();
        }
    }

    private void StartNewFolder()
    {
        NewFolderName = string.IsNullOrEmpty(_activeFolderPath) ? string.Empty : $"{_activeFolderPath}/";
        IsCreatingFolder = true;
    }

    private async Task ConfirmNewFolderAsync()
    {
        var normalized = NoteService.NormalizeFolderPath(_newFolderName);
        IsCreatingFolder = false;
        NewFolderName = string.Empty;

        if (string.IsNullOrWhiteSpace(normalized))
            return;

        await _noteService.CreateFolderAsync(normalized);
        AddFolderHierarchy(normalized);
        _collapsedFolders.Remove(normalized);
        ActiveFolderPath = normalized;
        RefreshFilteredNotes();
    }

    private void AddFolderHierarchy(string folderPath)
    {
        var normalized = NoteService.NormalizeFolderPath(folderPath);
        if (string.IsNullOrEmpty(normalized))
            return;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        foreach (var part in parts)
        {
            current = string.IsNullOrEmpty(current) ? part : $"{current}/{part}";
            _knownFolders.Add(current);
        }
    }

    private void OpenVaultFolderInExplorer()
    {
        try
        {
            var dir = VaultDirectoryPath;
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
        }
    }

    public void RefreshFilteredNotes()
    {
        var previousSelectedId = _selectedNote?.Id;
        var matchingModels = _noteService
            .FilterNotes(_allNotes.Where(vm => vm.Model is not null).Select(vm => vm.Model!), _searchQuery)
            .ToHashSet();

        var matchingNotes = _allNotes
            .Where(vm => vm.Model is not null && matchingModels.Contains(vm.Model))
            .OrderByDescending(vm => vm.IsPinned)
            .ThenByDescending(vm => vm.Model!.UpdatedAt)
            .ToList();

        FilteredNotes.Clear();
        var isSearching = !string.IsNullOrWhiteSpace(_searchQuery);

        // Emit hierarchical folder tree
        var topFolders = _knownFolders
            .Where(f => !f.Contains('/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var folder in topFolders)
            AppendFolderBranch(folder, 0, matchingNotes, isSearching);

        // Emit root notes (not inside any folder)
        foreach (var rootNote in matchingNotes.Where(n => string.IsNullOrEmpty(n.FolderPath)))
        {
            rootNote.Depth = 0;
            FilteredNotes.Add(rootNote);
        }

        var restored = previousSelectedId is not null
            ? FilteredNotes.FirstOrDefault(n => !n.IsFolder && n.Id == previousSelectedId)
            : null;

        var nextSelection = restored ?? FilteredNotes.FirstOrDefault(n => !n.IsFolder);
        if (!ReferenceEquals(_selectedNote, nextSelection))
        {
            SelectedNote = nextSelection;
        }
        else
        {
            foreach (var item in FilteredNotes)
                item.IsSelected = !item.IsFolder && ReferenceEquals(item, _selectedNote);
            OnPropertyChanged(nameof(SelectedNote));
        }
    }

    private void AppendFolderBranch(string folderPath, int depth, List<NoteItemViewModel> matchingNotes, bool isSearching)
    {
        var branchNotesCount = matchingNotes.Count(n =>
            string.Equals(n.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase) ||
            n.FolderPath.StartsWith($"{folderPath}/", StringComparison.OrdinalIgnoreCase));

        if (isSearching && branchNotesCount == 0 && !folderPath.Contains(_searchQuery.Trim(), StringComparison.OrdinalIgnoreCase))
            return;

        var isExpanded = isSearching || !_collapsedFolders.Contains(folderPath);
        var lastSlash = folderPath.LastIndexOf('/');
        var displayName = lastSlash >= 0 ? folderPath[(lastSlash + 1)..] : folderPath;

        var folderVm = new NoteItemViewModel(folderPath, displayName, depth, isExpanded, branchNotesCount);
        FilteredNotes.Add(folderVm);

        if (!isExpanded)
            return;

        var prefix = $"{folderPath}/";
        var childFolders = _knownFolders
            .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        !f[prefix.Length..].Contains('/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var childFolder in childFolders)
            AppendFolderBranch(childFolder, depth + 1, matchingNotes, isSearching);

        foreach (var note in matchingNotes.Where(n => string.Equals(n.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)))
        {
            note.Depth = depth + 1;
            FilteredNotes.Add(note);
        }
    }

    private void QueueAutosave(Note note)
    {
        _autosaveCts?.Cancel();
        _autosaveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _autosaveCts = cts;

        SetSaveStatus("Saving...");
        _ = RunAutosaveAsync(note, cts.Token);
    }

    private async Task RunAutosaveAsync(Note note, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            await _noteService.SaveNoteAsync(note, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
                SetSaveStatus("Saved");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!cancellationToken.IsCancellationRequested)
                SetSaveStatus("Retry");
        }
    }

    public async Task ForceSaveAsync()
    {
        if (_selectedNote?.Model is null)
            return;

        await _noteService.SaveNoteAsync(_selectedNote.Model);
        SetSaveStatus("Saved");
    }

    private void SetSaveStatus(string status)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
            SaveStatusText = status;
        else
            _dispatcherQueue.TryEnqueue(() => SaveStatusText = status);
    }
}
