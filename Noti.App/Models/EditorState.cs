using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Noti.App
{
    public enum LiveBlockKind
    {
        Paragraph,
        Heading1,
        Heading2,
        Heading3,
        Checklist,
        Bullet,
        Quote,
        CodeBlock,
        TableBlock,
        Divider,
        ImageBlock,
        HtmlWrapper
    }

    public sealed class LiveDocBlock
    {
        public LiveBlockKind Kind { get; set; } = LiveBlockKind.Paragraph;
        public string Content { get; set; } = string.Empty;
        public bool IsChecked { get; set; }
        public TextAlignment BlockAlignment { get; set; } = TextAlignment.Left;
        public bool IsCentered
        {
            get => BlockAlignment == TextAlignment.Center;
            set => BlockAlignment = value ? TextAlignment.Center : TextAlignment.Left;
        }
        public HorizontalAlignment HorizontalBlockAlignment => BlockAlignment switch
        {
            TextAlignment.Center => HorizontalAlignment.Center,
            TextAlignment.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left
        };
        public bool IsBlockBold { get; set; }
        public bool IsBlockItalic { get; set; }
        public bool IsBlockStrikethrough { get; set; }
        public bool IsBlockCode { get; set; }
        public List<List<string>> TableMatrix { get; set; } = [];
        public List<double> ColumnWidths { get; set; } = [];
        public TextBox? PrimaryTextBox { get; set; }
        public Border? RichDisplayHost { get; set; }
        public Grid? CellHostGrid { get; set; }
        public TextBlock? DisplayTextBlock { get; set; }
        public FrameworkElement? RootElement { get; set; }
    }
}

namespace Noti.App.ViewModels
{
    public enum EditorViewMode
    {
        Live = 0,
        Source = 1
    }

    public enum ThemeMode
    {
        System = 0,
        Dark = 1,
        Light = 2
    }

    public enum BackdropMode
    {
        Blur = 0,
        Mica = 1
    }

    public enum SettingsSection
    {
        Appearance = 0,
        Profile = 1,
        Vault = 2,
        Shortcuts = 3
    }

    public sealed class CommandPaletteItem : ObservableObject
    {
        private bool _isSelected;

        public required string Title { get; init; }
        public required string Subtitle { get; init; }
        public required string IconGlyph { get; init; }
        public required string ShortcutHint { get; init; }
        public NoteItemViewModel? TargetNote { get; init; }
        public Action? ExecuteAction { get; init; }

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

    public sealed class SlashCommandItem
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required string Subtitle { get; init; }
        public string BadgeText { get; init; } = string.Empty;
        public string IconGlyph { get; init; } = string.Empty;
        public Visibility BadgeTextVisibility => string.IsNullOrEmpty(BadgeText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility IconVisibility => string.IsNullOrEmpty(IconGlyph) ? Visibility.Collapsed : Visibility.Visible;
    }
}
