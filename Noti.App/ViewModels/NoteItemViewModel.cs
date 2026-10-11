using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Noti.Core;

namespace Noti.App.ViewModels;

public sealed class NoteItemViewModel : ObservableObject
{
    private readonly string _folderDisplayName = string.Empty;
    private bool _isSelected;
    private bool _isExpanded = true;
    private int _depth;
    private int _folderNoteCount;

    public NoteItemViewModel(Note model, int depth = 0)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
        IsFolder = false;
        FolderPath = model.FolderPath;
        _depth = depth;
    }

    public NoteItemViewModel(string folderPath, string displayName, int depth = 0, bool isExpanded = true, int noteCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        IsFolder = true;
        FolderPath = folderPath;
        _folderDisplayName = displayName;
        _depth = depth;
        _isExpanded = isExpanded;
        _folderNoteCount = noteCount;
    }

    public Note? Model { get; }
    public bool IsFolder { get; }
    public string FolderPath { get; }

    public string Id => IsFolder ? $"folder:{FolderPath}" : Model!.Id;
    public string Title => IsFolder ? _folderDisplayName : Model!.Title;
    public string Snippet => IsFolder ? string.Empty : Model!.Snippet;

    public int Depth
    {
        get => _depth;
        set
        {
            if (SetProperty(ref _depth, value))
                OnPropertyChanged(nameof(IndentMargin));
        }
    }

    public Thickness IndentMargin => new(_depth * 14, 0, 0, 4);

    public Visibility FolderVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoteVisibility => IsFolder ? Visibility.Collapsed : Visibility.Visible;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ChevronGlyph));
                OnPropertyChanged(nameof(FolderIconGlyph));
            }
        }
    }

    public string ChevronGlyph => _isExpanded ? "\uE70D" : "\uE76C";
    public string FolderIconGlyph => _isExpanded ? "\uE838" : "\uE8B7";

    public int FolderNoteCount
    {
        get => _folderNoteCount;
        set
        {
            if (SetProperty(ref _folderNoteCount, value))
                OnPropertyChanged(nameof(FolderCountText));
        }
    }

    public string FolderCountText => _folderNoteCount.ToString();

    public bool IsPinned
    {
        get => Model?.IsPinned ?? false;
        set
        {
            if (Model is null || Model.IsPinned == value)
                return;

            Model.IsPinned = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinBadgeVisibility));
            OnPropertyChanged(nameof(PinOpacity));
            OnPropertyChanged(nameof(PinIconGlyph));
        }
    }

    public Visibility PinBadgeVisibility => (!IsFolder && IsPinned) ? Visibility.Visible : Visibility.Collapsed;
    public double PinOpacity => IsPinned ? 1.0 : 0.35;
    public string PinIconGlyph => IsPinned ? "\uE840" : "\uE718";

    public string Content
    {
        get => Model?.Content ?? string.Empty;
        set
        {
            if (Model is null || Model.Content == value)
                return;

            Model.Content = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Snippet));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                OnPropertyChanged(nameof(SelectionOpacity));
        }
    }

    public double SelectionOpacity => _isSelected ? 1.0 : 0.0;
}
