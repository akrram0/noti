using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Noti.Core;
using Windows.UI;

namespace Noti.App.ViewModels;

public sealed class NoteItemViewModel : ObservableObject
{
    private readonly string _folderDisplayName = string.Empty;
    private bool _isSelected;
    private bool _isExpanded = true;
    private int _depth;
    private int _folderNoteCount;
    private string _colorHex = string.Empty;

    public NoteItemViewModel(Note model, int depth = 0, string colorHex = "")
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
        IsFolder = false;
        FolderPath = model.FolderPath;
        _depth = depth;
        _colorHex = colorHex ?? string.Empty;
    }

    public NoteItemViewModel(string folderPath, string displayName, int depth = 0, bool isExpanded = true, int noteCount = 0, string colorHex = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        IsFolder = true;
        FolderPath = folderPath;
        _folderDisplayName = displayName;
        _depth = depth;
        _isExpanded = isExpanded;
        _folderNoteCount = noteCount;
        _colorHex = colorHex ?? string.Empty;
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
            {
                OnPropertyChanged(nameof(IndentMargin));
                OnPropertyChanged(nameof(TreeGuideVisibility));
            }
        }
    }

    public Thickness IndentMargin => new(_depth * 16, 0, 0, 4);
    public Visibility TreeGuideVisibility => _depth > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (SetProperty(ref _colorHex, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasCustomColor));
                OnPropertyChanged(nameof(AccentColorBrush));
                OnPropertyChanged(nameof(CustomRowTintBrush));
                OnPropertyChanged(nameof(ColorDotVisibility));
                OnPropertyChanged(nameof(ColorDotRestingOpacity));
            }
        }
    }

    public bool HasCustomColor => !string.IsNullOrWhiteSpace(_colorHex);
    public Visibility ColorDotVisibility => HasCustomColor ? Visibility.Visible : Visibility.Collapsed;
    public double ColorDotRestingOpacity => HasCustomColor ? 0.95 : 0.0;

    public SolidColorBrush AccentColorBrush =>
        TryParseHexColor(_colorHex, 255, out var c)
            ? new SolidColorBrush(c)
            : new SolidColorBrush(ColorHelper.FromArgb(150, 142, 142, 147));

    public SolidColorBrush CustomRowTintBrush =>
        TryParseHexColor(_colorHex, 30, out var c)
            ? new SolidColorBrush(c)
            : new SolidColorBrush(Colors.Transparent);

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

    public static Color ParseHexColor(string? hex, Color fallback) =>
        TryParseHexColor(hex, 255, out var c) ? c : fallback;

    private static bool TryParseHexColor(string? hex, byte overrideAlpha, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var clean = hex.Trim().TrimStart('#');
        if (clean.Length == 8 && uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            color = ColorHelper.FromArgb(
                overrideAlpha,
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF));
            return true;
        }
        if (clean.Length == 6 && uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = ColorHelper.FromArgb(
                overrideAlpha,
                (byte)((rgb >> 16) & 0xFF),
                (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF));
            return true;
        }
        return false;
    }
}
