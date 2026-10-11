using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Noti.Core;
using Noti.Storage;
using Windows.UI.ViewManagement;

namespace Noti.App.ViewModels;

public enum EditorViewMode
{
    Write = 0,
    Split = 1,
    View = 2
}

public enum ThemeMode
{
    System = 0,
    Dark = 1,
    Light = 2
}

public sealed class MainViewModel : ObservableObject
{
    private readonly NoteService _noteService;
    private readonly AppSettings _settings;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly UISettings _uiSettings = new();
    private readonly List<NoteItemViewModel> _allNotes = new();
    private readonly Dictionary<string, NoteItemViewModel> _folderViewModels = new(StringComparer.OrdinalIgnoreCase);
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
    private ThemeMode _currentThemeMode = ThemeMode.System;
    private bool _isDarkMode = true;
    private bool _isFluidAnimationEnabled = true;
    private bool _isTreeShortcutEnabled = true;
    private double _editorFontSize = 16.0;
    private EditorViewMode _currentViewMode = EditorViewMode.Write;
    private string _userName = "Writer";
    private string _userAvatarPath = string.Empty;
    private BitmapImage? _userAvatarSource;

    private bool _isUpdateAvailable;
    private string _latestUpdateUrl = string.Empty;
    private string _updateStatusText = $"Up to date ({AppUpdater.CurrentVersionText})";
    private string _updateButtonText = "Check for Updates";
    private string _updateTopBarText = "Update Ready";

    public MainViewModel(NoteService noteService, AppSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(noteService);
        _noteService = noteService;
        _settings = settings ?? AppSettings.Load();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _isFluidAnimationEnabled = _settings.IsFluidAnimationEnabled;
        _isTreeShortcutEnabled = _settings.IsTreeShortcutEnabled;
        _editorFontSize = Math.Clamp(_settings.EditorFontSize, 11.0, 28.0);
        _userName = string.IsNullOrWhiteSpace(_settings.UserName) ? "Writer" : _settings.UserName;
        _userAvatarPath = _settings.UserAvatarPath ?? string.Empty;
        LoadAvatarBitmap();

        _currentThemeMode = Enum.TryParse<ThemeMode>(_settings.ThemePreference, ignoreCase: true, out var parsedTheme)
            ? parsedTheme
            : ThemeMode.System;
        ApplyThemeMode();

        _uiSettings.ColorValuesChanged += (_, _) =>
        {
            if (_currentThemeMode == ThemeMode.System)
                EnqueueUi(ApplyThemeMode);
        };

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

        SetSystemThemeCommand = new RelayCommand(() => CurrentThemeMode = ThemeMode.System);
        SetDarkThemeCommand = new RelayCommand(() => CurrentThemeMode = ThemeMode.Dark);
        SetLightThemeCommand = new RelayCommand(() => CurrentThemeMode = ThemeMode.Light);
        ToggleThemeCommand = new RelayCommand(() =>
        {
            CurrentThemeMode = IsDarkMode ? ThemeMode.Light : ThemeMode.Dark;
        });

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

        DecreaseFontSizeCommand = new RelayCommand(() => EditorFontSize = Math.Max(11.0, EditorFontSize - 1.0));
        IncreaseFontSizeCommand = new RelayCommand(() => EditorFontSize = Math.Min(28.0, EditorFontSize + 1.0));
        ResetZoomCommand = new RelayCommand(() => EditorFontSize = 16.0);

        OpenVaultFolderCommand = new RelayCommand(OpenVaultFolderInExplorer);
        DeleteSelectedNoteCommand = new AsyncRelayCommand(() => DeleteNoteAsync(_selectedNote));
        ClearUserAvatarCommand = new RelayCommand(ClearUserAvatar);
        CheckOrInstallUpdateCommand = new AsyncRelayCommand(CheckOrInstallUpdateAsync);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync);
    }

    public ObservableCollection<NoteItemViewModel> FilteredNotes { get; } = new();

    public IAsyncRelayCommand CreateNoteCommand { get; }
    public IRelayCommand StartNewFolderCommand { get; }
    public IAsyncRelayCommand ConfirmNewFolderCommand { get; }
    public IRelayCommand CancelNewFolderCommand { get; }
    public IRelayCommand ToggleSidebarCommand { get; }
    public IRelayCommand ToggleSettingsCommand { get; }
    public IRelayCommand CloseSettingsCommand { get; }
    public IRelayCommand SetSystemThemeCommand { get; }
    public IRelayCommand SetDarkThemeCommand { get; }
    public IRelayCommand SetLightThemeCommand { get; }
    public IRelayCommand ToggleThemeCommand { get; }
    public IRelayCommand ToggleFluidAnimationCommand { get; }
    public IRelayCommand ToggleTreeShortcutCommand { get; }
    public IRelayCommand SetWriteModeCommand { get; }
    public IRelayCommand SetSplitModeCommand { get; }
    public IRelayCommand SetViewModeCommand { get; }
    public IRelayCommand CycleViewModeCommand { get; }
    public IRelayCommand DecreaseFontSizeCommand { get; }
    public IRelayCommand IncreaseFontSizeCommand { get; }
    public IRelayCommand ResetZoomCommand { get; }
    public IRelayCommand OpenVaultFolderCommand { get; }
    public IAsyncRelayCommand DeleteSelectedNoteCommand { get; }
    public IRelayCommand ClearUserAvatarCommand { get; }
    public IAsyncRelayCommand CheckOrInstallUpdateCommand { get; }
    public IAsyncRelayCommand InstallUpdateCommand { get; }

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
                foreach (var item in _allNotes)
                    item.IsSelected = ReferenceEquals(item, value);

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
        string.IsNullOrEmpty(_activeFolderPath) ? "Vault" : _activeFolderPath;

    public string UserName
    {
        get => _userName;
        set
        {
            var raw = value ?? string.Empty;
            if (SetProperty(ref _userName, raw))
            {
                _settings.UserName = DisplayUserName;
                _settings.Save();
                OnPropertyChanged(nameof(DisplayUserName));
                OnPropertyChanged(nameof(UserInitials));
            }
        }
    }

    public string DisplayUserName =>
        string.IsNullOrWhiteSpace(_userName) ? "Writer" : _userName.Trim();

    public string UserInitials
    {
        get
        {
            var effective = DisplayUserName;
            var parts = effective.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";

            return effective.Length >= 2
                ? effective[..2].ToUpperInvariant()
                : effective.ToUpperInvariant();
        }
    }

    public BitmapImage? UserAvatarSource
    {
        get => _userAvatarSource;
        private set
        {
            if (SetProperty(ref _userAvatarSource, value))
            {
                OnPropertyChanged(nameof(UserAvatarVisibility));
                OnPropertyChanged(nameof(UserInitialsVisibility));
                OnPropertyChanged(nameof(RemoveAvatarVisibility));
            }
        }
    }

    public Visibility UserAvatarVisibility => _userAvatarSource is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UserInitialsVisibility => _userAvatarSource is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RemoveAvatarVisibility => _userAvatarSource is not null ? Visibility.Visible : Visibility.Collapsed;

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

    public ThemeMode CurrentThemeMode
    {
        get => _currentThemeMode;
        set
        {
            if (SetProperty(ref _currentThemeMode, value))
            {
                _settings.ThemePreference = value.ToString();
                _settings.Save();
                ApplyThemeMode();
                OnPropertyChanged(nameof(ThemeIndicatorOffsetX));
            }
        }
    }

    public double ThemeIndicatorOffsetX => (int)_currentThemeMode * 60.0;

    public bool IsDarkMode
    {
        get => _isDarkMode;
        private set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                _settings.IsDarkMode = value;
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
            var clamped = Math.Clamp(value, 11.0, 28.0);
            if (SetProperty(ref _editorFontSize, clamped))
            {
                _settings.EditorFontSize = clamped;
                _settings.Save();
                OnPropertyChanged(nameof(EditorFontSizeText));
                OnPropertyChanged(nameof(ZoomPercentText));
            }
        }
    }

    public string EditorFontSizeText => $"{_editorFontSize:0} px";
    public string ZoomPercentText => $"{Math.Round(_editorFontSize / 16.0 * 100.0):0}%";

    public EditorViewMode CurrentViewMode
    {
        get => _currentViewMode;
        set
        {
            if (SetProperty(ref _currentViewMode, value))
            {
                if (_isSettingsOpen)
                    IsSettingsOpen = false;

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
    public double ModeIndicatorOffsetX => (int)_currentViewMode * 60.0;

    public string VaultDirectoryPath =>
        (_noteService.Store as MarkdownNoteStore)?.DirectoryPath ?? _settings.VaultDirectoryPath;

    public string CurrentVersionText => AppUpdater.CurrentVersionText;

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set
        {
            if (SetProperty(ref _isUpdateAvailable, value))
                OnPropertyChanged(nameof(UpdatePillVisibility));
        }
    }

    public Visibility UpdatePillVisibility => _isUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    public string UpdateButtonText
    {
        get => _updateButtonText;
        private set => SetProperty(ref _updateButtonText, value);
    }

    public string UpdateTopBarText
    {
        get => _updateTopBarText;
        private set => SetProperty(ref _updateTopBarText, value);
    }

    public async Task InitializeAsync()
    {
        var folders = await _noteService.GetFoldersAsync();
        _knownFolders.Clear();
        _folderViewModels.Clear();
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

        _ = CheckForUpdatesAsync(silent: true);
    }

    public async Task SetUserAvatarAsync(string sourceImagePath)
    {
        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
            return;

        try
        {
            var appDataDir = AppSettings.AppDataDirectory;
            Directory.CreateDirectory(appDataDir);

            var ext = Path.GetExtension(sourceImagePath);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";

            var destPath = Path.Combine(appDataDir, $"avatar-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}");
            await Task.Run(() => File.Copy(sourceImagePath, destPath, overwrite: true));

            if (!string.IsNullOrWhiteSpace(_userAvatarPath) && File.Exists(_userAvatarPath) && _userAvatarPath.StartsWith(appDataDir, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(_userAvatarPath); } catch { }
            }

            _userAvatarPath = destPath;
            _settings.UserAvatarPath = destPath;
            _settings.Save();
            LoadAvatarBitmap();
        }
        catch
        {
        }
    }

    private void ClearUserAvatar()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_userAvatarPath) && File.Exists(_userAvatarPath))
                File.Delete(_userAvatarPath);
        }
        catch
        {
        }

        _userAvatarPath = string.Empty;
        _settings.UserAvatarPath = string.Empty;
        _settings.Save();
        UserAvatarSource = null;
    }

    private void LoadAvatarBitmap()
    {
        if (string.IsNullOrWhiteSpace(_userAvatarPath) || !File.Exists(_userAvatarPath))
        {
            UserAvatarSource = null;
            return;
        }

        try
        {
            UserAvatarSource = new BitmapImage(new Uri(_userAvatarPath));
        }
        catch
        {
            UserAvatarSource = null;
        }
    }

    private void ApplyThemeMode()
    {
        if (_currentThemeMode == ThemeMode.Dark)
        {
            IsDarkMode = true;
            return;
        }

        if (_currentThemeMode == ThemeMode.Light)
        {
            IsDarkMode = false;
            return;
        }

        try
        {
            var bg = _uiSettings.GetColorValue(UIColorType.Background);
            // Luminance < 128 means the Windows machine is in Dark Mode
            var isMachineDark = (bg.R + bg.G + bg.B) < 384;
            IsDarkMode = isMachineDark;
        }
        catch
        {
            IsDarkMode = true;
        }
    }

    private async Task CheckOrInstallUpdateAsync()
    {
        if (_isUpdateAvailable && !string.IsNullOrWhiteSpace(_latestUpdateUrl))
        {
            await InstallUpdateAsync();
            return;
        }

        await CheckForUpdatesAsync(silent: false);
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (!silent)
            SetUpdateStatus("Checking GitHub Releases...");

        var (available, latestVer, url) = await AppUpdater.CheckForUpdateAsync();
        _latestUpdateUrl = url;

        EnqueueUi(() =>
        {
            IsUpdateAvailable = available;
            if (available)
            {
                UpdateStatusText = $"New update available: {latestVer} (current {AppUpdater.CurrentVersionText})";
                UpdateButtonText = $"Install {latestVer}";
                UpdateTopBarText = $"Update {latestVer}";
            }
            else
            {
                UpdateStatusText = $"Up to date ({AppUpdater.CurrentVersionText})";
                UpdateButtonText = "Check for Updates";
            }
        });
    }

    private async Task InstallUpdateAsync()
    {
        var url = string.IsNullOrWhiteSpace(_latestUpdateUrl)
            ? "https://github.com/akrram0/noti/releases/latest/download/Noti-Setup-win-x64.exe"
            : _latestUpdateUrl;

        await ForceSaveAsync();
        await AppUpdater.DownloadAndInstallAsync(url, progress =>
        {
            SetUpdateStatus(progress);
            EnqueueUi(() => UpdateTopBarText = progress);
        });
    }

    private void SetUpdateStatus(string status) =>
        EnqueueUi(() => UpdateStatusText = status);

    private void EnqueueUi(Action action)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
            action();
        else
            _dispatcherQueue.TryEnqueue(() => action());
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

    public async Task DeleteFolderAsync(string folderPath)
    {
        var normalized = NoteService.NormalizeFolderPath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        await _noteService.DeleteFolderAsync(normalized);

        var prefix = $"{normalized}/";
        _knownFolders.RemoveWhere(f =>
            string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase) ||
            f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        foreach (var key in _folderViewModels.Keys.Where(f =>
                     string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase) ||
                     f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _folderViewModels.Remove(key);
        }

        _collapsedFolders.RemoveWhere(f =>
            string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase) ||
            f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        var notesToRemove = _allNotes
            .Where(n => !n.IsFolder &&
                        (string.Equals(n.FolderPath, normalized, StringComparison.OrdinalIgnoreCase) ||
                         n.FolderPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var noteVm in notesToRemove)
        {
            _allNotes.Remove(noteVm);
            _pinnedNoteIds.Remove(noteVm.Id);
        }

        if (string.Equals(_activeFolderPath, normalized, StringComparison.OrdinalIgnoreCase) ||
            _activeFolderPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            ActiveFolderPath = string.Empty;
        }

        _settings.CollapsedFolders = _collapsedFolders.ToList();
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
        if (_isSettingsOpen)
            IsSettingsOpen = false;
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
        if (_isSettingsOpen)
            IsSettingsOpen = false;
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
        var matchingModels = _noteService
            .FilterNotes(_allNotes.Where(vm => vm.Model is not null).Select(vm => vm.Model!), _searchQuery)
            .ToHashSet();

        var matchingNotes = _allNotes
            .Where(vm => vm.Model is not null && matchingModels.Contains(vm.Model))
            .OrderByDescending(vm => vm.IsPinned)
            .ThenByDescending(vm => vm.Model!.UpdatedAt)
            .ToList();

        var desiredItems = new List<NoteItemViewModel>();
        var isSearching = !string.IsNullOrWhiteSpace(_searchQuery);

        var topFolders = _knownFolders
            .Where(f => !f.Contains('/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var folder in topFolders)
            AppendFolderBranch(folder, 0, matchingNotes, isSearching, desiredItems);

        foreach (var rootNote in matchingNotes.Where(n => string.IsNullOrEmpty(n.FolderPath)))
        {
            rootNote.Depth = 0;
            desiredItems.Add(rootNote);
        }

        // Diff-sync FilteredNotes in-place so expanding/collapsing a folder never clears/flashes the list
        var desiredSet = desiredItems.ToHashSet();
        for (var i = FilteredNotes.Count - 1; i >= 0; i--)
        {
            if (!desiredSet.Contains(FilteredNotes[i]))
                FilteredNotes.RemoveAt(i);
        }

        for (var i = 0; i < desiredItems.Count; i++)
        {
            var target = desiredItems[i];
            if (i < FilteredNotes.Count && ReferenceEquals(FilteredNotes[i], target))
                continue;

            var existingIndex = FilteredNotes.IndexOf(target);
            if (existingIndex >= 0)
                FilteredNotes.Move(existingIndex, i);
            else
                FilteredNotes.Insert(i, target);
        }

        // Keep the active note untouched when expanding/collapsing folders so the editor never flashes/reloads
        if (_selectedNote is not null && _allNotes.Contains(_selectedNote))
        {
            foreach (var item in _allNotes)
                item.IsSelected = ReferenceEquals(item, _selectedNote);
        }
        else
        {
            SelectedNote = desiredItems.FirstOrDefault(n => !n.IsFolder);
        }
    }

    private void AppendFolderBranch(
        string folderPath,
        int depth,
        List<NoteItemViewModel> matchingNotes,
        bool isSearching,
        List<NoteItemViewModel> desiredItems)
    {
        var branchNotesCount = matchingNotes.Count(n =>
            string.Equals(n.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase) ||
            n.FolderPath.StartsWith($"{folderPath}/", StringComparison.OrdinalIgnoreCase));

        if (isSearching && branchNotesCount == 0 && !folderPath.Contains(_searchQuery.Trim(), StringComparison.OrdinalIgnoreCase))
            return;

        var isExpanded = isSearching || !_collapsedFolders.Contains(folderPath);
        var lastSlash = folderPath.LastIndexOf('/');
        var displayName = lastSlash >= 0 ? folderPath[(lastSlash + 1)..] : folderPath;

        if (!_folderViewModels.TryGetValue(folderPath, out var folderVm))
        {
            folderVm = new NoteItemViewModel(folderPath, displayName, depth, isExpanded, branchNotesCount);
            _folderViewModels[folderPath] = folderVm;
        }
        else
        {
            folderVm.Depth = depth;
            folderVm.IsExpanded = isExpanded;
            folderVm.FolderNoteCount = branchNotesCount;
        }

        desiredItems.Add(folderVm);

        if (!isExpanded)
            return;

        var prefix = $"{folderPath}/";
        var childFolders = _knownFolders
            .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        !f[prefix.Length..].Contains('/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var childFolder in childFolders)
            AppendFolderBranch(childFolder, depth + 1, matchingNotes, isSearching, desiredItems);

        foreach (var note in matchingNotes.Where(n => string.Equals(n.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)))
        {
            note.Depth = depth + 1;
            desiredItems.Add(note);
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
