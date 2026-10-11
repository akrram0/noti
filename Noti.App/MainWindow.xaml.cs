using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Noti.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace Noti.App;

public sealed partial class MainWindow : Window
{
    public const string BranchMid = "├─ ";
    public const string BranchEnd = "└── ";
    public const string BranchIndent = "│   ";

    private Storyboard? _typingWordStoryboard;
    private string? _pendingDeleteFolderPath;
    private bool _isCommandPaletteOpen;
    private int _activeMentionAtIndex = -1;
    private int _activeSlashIndex = -1;
    private List<LiveDocBlock> _liveBlocks = [];
    private LiveDocBlock? _activeLiveBlock;
    private bool _isSyncingLiveEditor;
    private string? _lastRenderedLiveMarkdown;
    private int _liveRebuildVersion;
    private int _liveMaterializedCount;
    private readonly Stack<TextBox> _liveTextBoxPool = new(160);
    private readonly DispatcherTimer _liveSyncTimer;

    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush AccentBrush = new(ColorHelper.FromArgb(255, 10, 132, 255));
    private static readonly SolidColorBrush SelectionUnfocusedBrush = new(ColorHelper.FromArgb(140, 10, 132, 255));
    private static readonly FontFamily MonoFontFamily = new("Cascadia Mono, Consolas, Courier New");
    private static readonly FontFamily DefaultFontFamily = FontFamily.XamlAutoFontFamily;

    private bool? _cachedIsDarkMode;
    private SolidColorBrush _cachedPrimaryBrush = new(ColorHelper.FromArgb(255, 245, 245, 247));
    private SolidColorBrush _cachedSecondaryBrush = new(ColorHelper.FromArgb(255, 142, 142, 147));
    private SolidColorBrush _cachedSurfaceBrush = new(ColorHelper.FromArgb(36, 255, 255, 255));
    private SolidColorBrush _cachedBorderBrush = new(ColorHelper.FromArgb(38, 255, 255, 255));
    private Style? _cachedBorderlessStyle;
    private List<string> _cachedSortedNoteTitles = [];

    private static readonly (string Name, string Hex)[] PaletteColors =
    [
        ("Blue", "#FF3B82F6"),
        ("Purple", "#FF8B5CF6"),
        ("Pink", "#FFEC4899"),
        ("Red", "#FFEF4444"),
        ("Amber", "#FFF59E0B"),
        ("Green", "#FF10B981"),
        ("Cyan", "#FF06B6D4")
    ];

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        ViewModel.RequestExportMarkdown = () => _ = PickAndExportMarkdownAsync();

        InitializeComponent();
        ConfigureWindowChrome();

        _liveSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _liveSyncTimer.Tick += (_, _) => FlushSyncLiveBlocksToViewModel();

        RootGrid.Loaded += (_, _) =>
        {
            ApplyResponsiveLayout();
            ThemeIndicatorTranslate.X = ViewModel.ThemeIndicatorOffsetX;
            BackdropIndicatorTranslate.X = ViewModel.BackdropIndicatorOffsetX;
            ModeIndicatorTranslate.X = ViewModel.ModeIndicatorOffsetX;
            UpdateTitleBarPassthrough();
            RebuildLiveEditor(reparse: true);
        };
        RootGrid.SizeChanged += (_, _) =>
        {
            ApplyResponsiveLayout();
            UpdateTitleBarPassthrough();
        };
        SidebarToggleButton.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
        ViewSwitcherBorder.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
        UpdateTopBarButton.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
        ZoomBadgeButton.SizeChanged += (_, _) => UpdateTitleBarPassthrough();

        LiveEditorScrollViewer.ViewChanged += OnLiveEditorScrollViewerViewChanged;
        RootGrid.PreviewKeyDown += OnRootPreviewKeyDown;
        EditorTextBox.PreviewKeyDown += OnEditorPreviewKeyDown;
        EditorTextBox.TextChanged += OnEditorTextChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public MainViewModel ViewModel { get; }

    private double TargetSidebarWidth => RootGrid.ActualWidth is > 0 and < 860 ? 224.0 : 272.0;

    private void ConfigureWindowChrome()
    {
        AppWindow.Resize(new SizeInt32(1080, 680));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Noti.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        ApplyWindowBackdrop();

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.Gray;
        }
    }

    private void ApplyWindowBackdrop()
    {
        try
        {
            if (ViewModel.CurrentBackdropMode == BackdropMode.Blur && DesktopAcrylicController.IsSupported())
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
                return;
            }

            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            }
        }
        catch
        {
        }
    }

    private void ApplyResponsiveLayout()
    {
        var width = RootGrid.ActualWidth;
        if (width <= 0)
            return;

        var isCompact = width < 860;
        var horizPad = isCompact ? 20.0 : 44.0;
        var vertPad = isCompact ? 18.0 : 28.0;

        EditorTextBox.Padding = new Thickness(horizPad, vertPad, horizPad, 16);
        LiveEditorPanel.Padding = new Thickness(horizPad, vertPad, horizPad, 80);
        StatusBarGrid.Padding = new Thickness(horizPad, 0, horizPad, 0);

        if (ViewModel.IsSidebarOpen)
            SidebarContainer.Width = TargetSidebarWidth;
    }

    private void OnRootPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var ctrlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (!ctrlDown)
            return;

        var delta = e.GetCurrentPoint(RootGrid).Properties.MouseWheelDelta;
        if (delta > 0)
            ViewModel.IncreaseFontSizeCommand.Execute(null);
        else if (delta < 0)
            ViewModel.DecreaseFontSizeCommand.Execute(null);

        e.Handled = true;
    }

    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            if (SlashMenuPopupBorder.Visibility == Visibility.Visible)
            {
                HideSlashMenuPopup();
                e.Handled = true;
                return;
            }
            if (MentionPopupBorder.Visibility == Visibility.Visible)
            {
                HideMentionPopup();
                e.Handled = true;
                return;
            }
            if (SelectionToolbarBorder.Visibility == Visibility.Visible)
            {
                SelectionToolbarBorder.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            if (_isCommandPaletteOpen)
            {
                ShowCommandPalette(open: false);
                e.Handled = true;
                return;
            }
            if (DeleteFolderConfirmOverlay.Visibility == Visibility.Visible)
            {
                ShowDeleteFolderOverlay(open: false);
                e.Handled = true;
                return;
            }
            if (ViewModel.IsSettingsOpen)
            {
                ViewModel.IsSettingsOpen = false;
                e.Handled = true;
                return;
            }
            if (ViewModel.IsCreatingFolder)
            {
                ViewModel.CancelNewFolderCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        var ctrlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (!ctrlDown)
            return;

        switch (e.Key)
        {
            case VirtualKey.K:
                e.Handled = true;
                ToggleCommandPalette();
                break;

            case (VirtualKey)187: // Ctrl + Plus / Equals
            case VirtualKey.Add:
                e.Handled = true;
                ViewModel.IncreaseFontSizeCommand.Execute(null);
                break;

            case (VirtualKey)189: // Ctrl + Minus
            case VirtualKey.Subtract:
                e.Handled = true;
                ViewModel.DecreaseFontSizeCommand.Execute(null);
                break;

            case VirtualKey.Number0:
            case VirtualKey.NumberPad0:
                e.Handled = true;
                ViewModel.ResetZoomCommand.Execute(null);
                break;

            case VirtualKey.N when shiftDown:
                e.Handled = true;
                ViewModel.StartNewFolderCommand.Execute(null);
                NewFolderTextBox.Focus(FocusState.Programmatic);
                break;

            case VirtualKey.N:
                e.Handled = true;
                _ = ViewModel.CreateNoteInFolderAsync();
                break;

            case VirtualKey.E:
                e.Handled = true;
                ViewModel.CycleViewModeCommand.Execute(null);
                break;

            case VirtualKey.O:
                e.Handled = true;
                _ = PickAndImportMarkdownAsync();
                break;

            case VirtualKey.S when shiftDown:
                e.Handled = true;
                if (_liveSyncTimer.IsEnabled)
                    FlushSyncLiveBlocksToViewModel();
                _ = PickAndExportMarkdownAsync();
                break;

            case VirtualKey.S:
                e.Handled = true;
                if (_liveSyncTimer.IsEnabled)
                    FlushSyncLiveBlocksToViewModel();
                _ = ViewModel.ForceSaveAsync();
                break;

            case VirtualKey.Delete:
                e.Handled = true;
                _ = ViewModel.DeleteNoteAsync(ViewModel.SelectedNote);
                break;

            case (VirtualKey)188: // Ctrl+,
                e.Handled = true;
                ViewModel.IsSettingsOpen = !ViewModel.IsSettingsOpen;
                break;

            case (VirtualKey)220: // Ctrl+\
                e.Handled = true;
                ViewModel.IsSidebarOpen = !ViewModel.IsSidebarOpen;
                break;
        }
    }

    private void OnEditorPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

        if (!ctrlDown && SlashMenuPopupBorder.Visibility == Visibility.Visible && ViewModel.SlashMenuSuggestions.Count > 0)
        {
            if (e.Key == VirtualKey.Down)
            {
                e.Handled = true;
                var next = (SlashMenuListView.SelectedIndex + 1) % ViewModel.SlashMenuSuggestions.Count;
                SlashMenuListView.SelectedIndex = next;
                SlashMenuListView.ScrollIntoView(ViewModel.SlashMenuSuggestions[next]);
                return;
            }
            if (e.Key == VirtualKey.Up)
            {
                e.Handled = true;
                var prev = (SlashMenuListView.SelectedIndex - 1 + ViewModel.SlashMenuSuggestions.Count) % ViewModel.SlashMenuSuggestions.Count;
                SlashMenuListView.SelectedIndex = prev;
                SlashMenuListView.ScrollIntoView(ViewModel.SlashMenuSuggestions[prev]);
                return;
            }
            if (e.Key is VirtualKey.Enter or VirtualKey.Tab)
            {
                e.Handled = true;
                var selected = SlashMenuListView.SelectedItem as SlashCommandItem ?? ViewModel.SlashMenuSuggestions[0];
                ApplySlashCommand(selected);
                return;
            }
        }

        if (!ctrlDown && MentionPopupBorder.Visibility == Visibility.Visible && ViewModel.MentionSuggestions.Count > 0)
        {
            if (e.Key == VirtualKey.Down)
            {
                e.Handled = true;
                var next = (MentionListView.SelectedIndex + 1) % ViewModel.MentionSuggestions.Count;
                MentionListView.SelectedIndex = next;
                MentionListView.ScrollIntoView(ViewModel.MentionSuggestions[next]);
                return;
            }
            if (e.Key == VirtualKey.Up)
            {
                e.Handled = true;
                var prev = (MentionListView.SelectedIndex - 1 + ViewModel.MentionSuggestions.Count) % ViewModel.MentionSuggestions.Count;
                MentionListView.SelectedIndex = prev;
                MentionListView.ScrollIntoView(ViewModel.MentionSuggestions[prev]);
                return;
            }
            if (e.Key is VirtualKey.Enter or VirtualKey.Tab)
            {
                e.Handled = true;
                var selected = MentionListView.SelectedItem as NoteItemViewModel ?? ViewModel.MentionSuggestions[0];
                InsertMention(selected);
                return;
            }
        }

        if (ctrlDown)
        {
            switch (e.Key)
            {
                case VirtualKey.B:
                    e.Handled = true;
                    WrapEditorSelection("**", "**");
                    return;
                case VirtualKey.I:
                    e.Handled = true;
                    WrapEditorSelection("*", "*");
                    return;
                case VirtualKey.L:
                    e.Handled = true;
                    ToggleLinePrefix("- [ ] ");
                    return;
                case VirtualKey.Number1:
                    e.Handled = true;
                    ToggleHeadingPrefix("# ");
                    return;
                case VirtualKey.Number2:
                    e.Handled = true;
                    ToggleHeadingPrefix("## ");
                    return;
                case VirtualKey.Number3:
                    e.Handled = true;
                    ToggleHeadingPrefix("### ");
                    return;
            }
            return;
        }

        if (!ViewModel.IsTreeShortcutEnabled)
            return;

        if (e.Key == VirtualKey.Tab)
        {
            var text = EditorTextBox.Text ?? string.Empty;
            var caret = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
            var lineStart = FindLineStart(text, caret);
            var lineEnd = FindLineEnd(text, caret);
            var currentLine = text[lineStart..lineEnd];

            if (TryParseTreeLine(currentLine, out var indent, out _, out _))
            {
                e.Handled = true;
                if (shiftDown)
                {
                    if (indent.Length >= BranchIndent.Length)
                    {
                        var updated = text.Remove(lineStart, BranchIndent.Length);
                        ApplyEditorText(updated, Math.Max(lineStart, caret - BranchIndent.Length));
                    }
                }
                else
                {
                    var updated = text.Insert(lineStart, BranchIndent);
                    ApplyEditorText(updated, caret + BranchIndent.Length);
                }
            }
            return;
        }

        if (e.Key != VirtualKey.Enter)
            return;

        var fullText = EditorTextBox.Text ?? string.Empty;
        var caretPos = Math.Clamp(EditorTextBox.SelectionStart, 0, fullText.Length);
        if (HandleTreeEnterKey(fullText, caretPos, shiftDown, out var newText, out var newCaret))
        {
            e.Handled = true;
            ApplyEditorText(newText, newCaret);
        }
    }

    public static bool HandleTreeEnterKey(string text, int caret, bool isShiftDown, out string updatedText, out int newCaret)
    {
        updatedText = text;
        newCaret = caret;

        var lineStart = FindLineStart(text, caret);
        var lineEnd = FindLineEnd(text, caret);
        var currentLine = text[lineStart..lineEnd];
        var isTreeLine = TryParseTreeLine(currentLine, out var indent, out var isMidBranch, out var body);

        if (isShiftDown)
        {
            if (isTreeLine)
            {
                if (string.IsNullOrWhiteSpace(body))
                {
                    ExitEmptyTreeBranch(text, lineStart, lineEnd, indent, out updatedText, out newCaret);
                    return true;
                }

                if (!isMidBranch)
                {
                    var branchOffset = lineStart + indent.Length;
                    var insert = $"\r{indent}{BranchEnd}";
                    updatedText =
                        text[..branchOffset] +
                        BranchMid +
                        text[(branchOffset + BranchEnd.Length)..caret] +
                        insert +
                        text[caret..];
                    newCaret = caret - BranchEnd.Length + BranchMid.Length + insert.Length;
                    return true;
                }
                else
                {
                    var insert = $"\r{indent}{BranchEnd}";
                    updatedText = text.Insert(caret, insert);
                    newCaret = caret + insert.Length;
                    return true;
                }
            }

            var firstBranch = $"\r{BranchEnd}";
            updatedText = text.Insert(caret, firstBranch);
            newCaret = caret + firstBranch.Length;
            return true;
        }

        if (isTreeLine && string.IsNullOrWhiteSpace(body))
        {
            ExitEmptyTreeBranch(text, lineStart, lineEnd, indent, out updatedText, out newCaret);
            return true;
        }

        return false;
    }

    private static void ExitEmptyTreeBranch(
        string text,
        int lineStart,
        int lineEnd,
        string indent,
        out string updatedText,
        out int newCaret)
    {
        var working = text.Remove(lineStart, lineEnd - lineStart);
        newCaret = lineStart;

        if (lineStart > 0)
        {
            var prevLineEnd = lineStart - 1;
            if (prevLineEnd > 0 && working[prevLineEnd] == '\n' && working[prevLineEnd - 1] == '\r')
                prevLineEnd--;

            var prevLineStart = FindLineStart(working, prevLineEnd);
            var prevLine = working[prevLineStart..prevLineEnd];
            var targetMidPrefix = $"{indent}{BranchMid}";

            if (prevLine.StartsWith(targetMidPrefix, StringComparison.Ordinal))
            {
                var midIndex = prevLineStart + indent.Length;
                working = working[..midIndex] + BranchEnd + working[(midIndex + BranchMid.Length)..];
            }
        }

        updatedText = working;
    }

    public static bool TryParseTreeLine(string line, out string indent, out bool isMidBranch, out string body)
    {
        indent = string.Empty;
        isMidBranch = false;
        body = string.Empty;

        var idx = 0;
        while (idx < line.Length)
        {
            if (line[idx..].StartsWith(BranchIndent, StringComparison.Ordinal))
            {
                idx += BranchIndent.Length;
                continue;
            }
            if (line[idx..].StartsWith("    ", StringComparison.Ordinal))
            {
                idx += 4;
                continue;
            }
            break;
        }

        indent = line[..idx];
        var rest = line[idx..];

        if (rest.StartsWith(BranchMid, StringComparison.Ordinal))
        {
            isMidBranch = true;
            body = rest[BranchMid.Length..];
            return true;
        }
        if (rest.StartsWith(BranchEnd, StringComparison.Ordinal))
        {
            isMidBranch = false;
            body = rest[BranchEnd.Length..];
            return true;
        }
        if (rest is "├─" or "└──" or "└─")
        {
            isMidBranch = rest == "├─";
            body = string.Empty;
            return true;
        }

        return false;
    }

    private static bool IsEditableLiveBlock(LiveDocBlock block) =>
        block.Kind is not (LiveBlockKind.Divider or LiveBlockKind.TableBlock or LiveBlockKind.ImageBlock);

    private TextBox GetActiveEditorTextBox()
    {
        if (ViewModel.CurrentViewMode == EditorViewMode.Live && _activeLiveBlock?.PrimaryTextBox is { } liveTb)
            return liveTb;

        return EditorTextBox;
    }

    private void FocusActiveEditorPane()
    {
        if (ViewModel.CurrentViewMode == EditorViewMode.Live)
        {
            var idx = _activeLiveBlock is not null ? _liveBlocks.IndexOf(_activeLiveBlock) : -1;
            if (idx < 0)
                idx = _liveBlocks.FindIndex(IsEditableLiveBlock);
            if (idx >= 0)
                FocusLiveBlockAt(idx);
        }
        else
        {
            EditorTextBox.Focus(FocusState.Programmatic);
        }
    }

    private void OnLiveEditorScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_liveMaterializedCount < _liveBlocks.Count &&
            LiveEditorScrollViewer.VerticalOffset + LiveEditorScrollViewer.ViewportHeight >= LiveEditorScrollViewer.ExtentHeight - 420)
        {
            EnsureLiveBlocksMaterializedThrough(Math.Min(_liveBlocks.Count - 1, _liveMaterializedCount + 48));
        }
    }

    private void EnsureLiveRenderResources(bool refreshTitles = false)
    {
        var isDarkMode = ViewModel.IsDarkMode;
        if (_cachedIsDarkMode != isDarkMode)
        {
            _cachedIsDarkMode = isDarkMode;
            _cachedPrimaryBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 245, 245, 247) : ColorHelper.FromArgb(255, 28, 28, 30));
            _cachedSecondaryBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 142, 142, 147) : ColorHelper.FromArgb(255, 108, 108, 112));
            _cachedSurfaceBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(36, 255, 255, 255) : ColorHelper.FromArgb(22, 0, 0, 0));
            _cachedBorderBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(38, 255, 255, 255) : ColorHelper.FromArgb(28, 0, 0, 0));
        }

        if (_cachedBorderlessStyle is null &&
            Application.Current?.Resources.TryGetValue("BorderlessTextBoxStyle", out var styleObj) == true)
        {
            _cachedBorderlessStyle = styleObj as Style;
        }

        if (refreshTitles || (_cachedSortedNoteTitles.Count == 0 && ViewModel.AllNoteItems.Count > 0))
        {
            _cachedSortedNoteTitles = ViewModel.AllNoteItems
                .Select(n => n.Title)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .OrderByDescending(t => t.Length)
                .ToList();
        }
    }

    private void RecycleBlockTextBox(LiveDocBlock block)
    {
        if (block.Kind is LiveBlockKind.CodeBlock or LiveBlockKind.TableBlock or LiveBlockKind.Divider or LiveBlockKind.ImageBlock)
            return;

        if (block.PrimaryTextBox is not { } tb)
            return;

        tb.Tag = null;
        if (tb.Parent is Panel parentPanel)
            parentPanel.Children.Remove(tb);
        else if (tb.Parent is Border parentBorder)
            parentBorder.Child = null;

        block.PrimaryTextBox = null;
        if (_liveTextBoxPool.Count < 160)
            _liveTextBoxPool.Push(tb);
    }

    private void RecycleAllLiveBlocksToPool()
    {
        for (var i = 0; i < _liveBlocks.Count; i++)
            RecycleBlockTextBox(_liveBlocks[i]);
    }

    private TextBox RentLiveTextBox(LiveDocBlock block)
    {
        EnsureLiveRenderResources();

        if (_liveTextBoxPool.TryPop(out var pooledTb))
        {
            if (pooledTb.Parent is Panel parentPanel)
                parentPanel.Children.Remove(pooledTb);
            else if (pooledTb.Parent is Border parentBorder)
                parentBorder.Child = null;

            pooledTb.Tag = block;
            return pooledTb;
        }

        var tb = new TextBox
        {
            AcceptsReturn = false,
            IsSpellCheckEnabled = false,
            Background = TransparentBrush,
            SelectionHighlightColor = AccentBrush,
            SelectionHighlightColorWhenNotFocused = SelectionUnfocusedBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 1.0,
            Tag = block
        };
        if (_cachedBorderlessStyle is not null)
            tb.Style = _cachedBorderlessStyle;

        WirePooledLiveTextBoxEvents(tb);
        return tb;
    }

    private void GetBlockTypography(
        LiveDocBlock block,
        int blockIndex,
        out double fontSize,
        out Windows.UI.Text.FontWeight fontWeight,
        out Windows.UI.Text.FontStyle fontStyle,
        out bool isTreeLine,
        out Brush textForeground,
        out string placeholder)
    {
        EnsureLiveRenderResources();
        var baseFontSize = ViewModel.EditorFontSize;

        fontSize = block.Kind switch
        {
            LiveBlockKind.Heading1 => baseFontSize + 10,
            LiveBlockKind.Heading2 => baseFontSize + 5,
            LiveBlockKind.Heading3 => baseFontSize + 2,
            _ => block.IsBlockCode ? Math.Max(13, baseFontSize - 1) : baseFontSize
        };

        fontWeight = block.Kind is LiveBlockKind.Heading1 or LiveBlockKind.Heading2 or LiveBlockKind.Heading3
            ? FontWeights.SemiBold
            : (block.IsBlockBold ? FontWeights.Bold : FontWeights.Normal);

        fontStyle = (block.IsBlockItalic || block.Kind == LiveBlockKind.Quote)
            ? Windows.UI.Text.FontStyle.Italic
            : Windows.UI.Text.FontStyle.Normal;

        var content = block.Content ?? string.Empty;
        isTreeLine = content.Contains("├─", StringComparison.Ordinal) ||
                     content.Contains("└─", StringComparison.Ordinal) ||
                     content.TrimStart().StartsWith('│');

        textForeground = (block.Kind == LiveBlockKind.Checklist && block.IsChecked) || block.Kind == LiveBlockKind.Quote || block.IsBlockStrikethrough
            ? _cachedSecondaryBrush
            : _cachedPrimaryBrush;

        placeholder = block.Kind switch
        {
            LiveBlockKind.Heading1 => "Heading 1",
            LiveBlockKind.Heading2 => "Heading 2",
            LiveBlockKind.Heading3 => "Heading 3",
            LiveBlockKind.Checklist => "To-do",
            LiveBlockKind.Bullet => "List item",
            LiveBlockKind.Quote => "Quote...",
            _ => blockIndex == 0 && _liveBlocks.Count == 1 ? "Type '/' for commands, or start writing..." : string.Empty
        };
    }

    private void EnsureLiveBlocksMaterializedThrough(int targetIndex)
    {
        if (_liveBlocks.Count == 0)
            return;

        var clampedTarget = Math.Clamp(targetIndex, 0, _liveBlocks.Count - 1);
        if (_liveMaterializedCount > clampedTarget)
            return;

        EnsureLiveRenderResources();
        var prevSync = _isSyncingLiveEditor;
        _isSyncingLiveEditor = true;
        try
        {
            for (var i = _liveMaterializedCount; i <= clampedTarget; i++)
            {
                var block = _liveBlocks[i];
                var element = CreateLiveBlockElement(block, i);
                block.RootElement = element;
                LiveEditorPanel.Children.Add(element);
            }
            _liveMaterializedCount = clampedTarget + 1;
        }
        finally
        {
            _isSyncingLiveEditor = prevSync;
        }
    }

    private void ScheduleNextLiveBlocksChunk(int rebuildVersion)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (rebuildVersion != _liveRebuildVersion || _liveMaterializedCount >= _liveBlocks.Count)
                return;

            const int ChunkSize = 32;
            var targetEnd = Math.Min(_liveBlocks.Count - 1, _liveMaterializedCount + ChunkSize - 1);
            EnsureLiveBlocksMaterializedThrough(targetEnd);

            if (rebuildVersion == _liveRebuildVersion && _liveMaterializedCount < _liveBlocks.Count)
                ScheduleNextLiveBlocksChunk(rebuildVersion);
        });
    }

    private void OnLiveCanvasBackgroundPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, LiveCanvasRootGrid) && !ReferenceEquals(e.OriginalSource, LiveEditorPanel))
            return;

        if (_liveBlocks.Count == 0)
        {
            InsertLiveBlockAt(0, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
            FlushSyncLiveBlocksToViewModel();
            return;
        }

        var clickY = e.GetCurrentPoint(LiveEditorPanel).Position.Y;
        var count = Math.Min(LiveEditorPanel.Children.Count, _liveBlocks.Count);

        for (var i = 0; i < count; i++)
        {
            var child = LiveEditorPanel.Children[i];
            var bottom = child.ActualOffset.Y + child.ActualSize.Y + 4;
            if (clickY <= bottom)
            {
                if (IsEditableLiveBlock(_liveBlocks[i]))
                    FocusLiveBlockAt(i);
                return;
            }
        }

        EnsureLiveBlocksMaterializedThrough(_liveBlocks.Count - 1);

        var lastBlock = _liveBlocks[^1];
        if (lastBlock.Kind is LiveBlockKind.TableBlock or LiveBlockKind.Divider or LiveBlockKind.CodeBlock or LiveBlockKind.ImageBlock)
        {
            InsertLiveBlockAt(_liveBlocks.Count, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
            FlushSyncLiveBlocksToViewModel();
            return;
        }

        FocusLiveBlockAt(_liveBlocks.Count - 1);
    }

    private void RebuildLiveEditor(bool reparse = true, int? focusBlockIndex = null, int? focusCaretIndex = null)
    {
        var rebuildVersion = ++_liveRebuildVersion;
        EnsureLiveRenderResources(refreshTitles: true);

        var prevSync = _isSyncingLiveEditor;
        _isSyncingLiveEditor = true;
        try
        {
            RecycleAllLiveBlocksToPool();
            _activeLiveBlock = null;
            LiveEditorPanel.Children.Clear();
            _liveMaterializedCount = 0;

            if (reparse)
            {
                _lastRenderedLiveMarkdown = ViewModel.EditorText;
                _liveBlocks = MarkdownRenderer.ParseLiveBlocks(ViewModel.EditorText);
                LiveEditorScrollViewer.ChangeView(null, 0, null, true);
            }

            if (_liveBlocks.Count == 0)
                _liveBlocks.Add(new LiveDocBlock { Kind = LiveBlockKind.Paragraph });

            const int InitialViewportBatchSize = 28;
            var initialTarget = focusBlockIndex is int fIdx
                ? Math.Max(InitialViewportBatchSize - 1, fIdx)
                : InitialViewportBatchSize - 1;

            EnsureLiveBlocksMaterializedThrough(Math.Min(initialTarget, _liveBlocks.Count - 1));
        }
        finally
        {
            _isSyncingLiveEditor = prevSync;
        }

        if (focusBlockIndex is int targetIdx && targetIdx >= 0 && targetIdx < _liveBlocks.Count)
        {
            FocusLiveBlockAt(targetIdx, focusCaretIndex);
        }
        else if (_liveBlocks.Count == 1 && string.IsNullOrEmpty(_liveBlocks[0].Content))
        {
            FocusLiveBlockAt(0, 0);
        }
        else
        {
            _activeLiveBlock = _liveBlocks.FirstOrDefault();
        }

        if (_liveMaterializedCount < _liveBlocks.Count)
            ScheduleNextLiveBlocksChunk(rebuildVersion);
    }

    private void FocusLiveBlockAt(int index, int? focusCaret = null)
    {
        if (index < 0 || index >= _liveBlocks.Count)
            return;

        EnsureLiveBlocksMaterializedThrough(index);

        var targetBlock = _liveBlocks[index];
        _activeLiveBlock = targetBlock;

        if (targetBlock.PrimaryTextBox is not { } targetTb)
            return;

        var caret = focusCaret ?? (targetTb.Text ?? string.Empty).Length;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.CurrentViewMode != EditorViewMode.Live ||
                ViewModel.IsSettingsOpen ||
                !ReferenceEquals(_activeLiveBlock, targetBlock) ||
                !ReferenceEquals(targetTb.Tag, targetBlock))
            {
                return;
            }

            targetTb.Focus(FocusState.Programmatic);
            var clamped = Math.Clamp(caret, 0, (targetTb.Text ?? string.Empty).Length);
            targetTb.SelectionStart = clamped;
            targetTb.SelectionLength = 0;
        });
    }

    private void RefreshLiveBlockAt(int index, int? focusCaret = null)
    {
        if (index < 0 || index >= _liveBlocks.Count)
            return;

        EnsureLiveBlocksMaterializedThrough(index);

        var block = _liveBlocks[index];
        RecycleBlockTextBox(block);

        var prevSync = _isSyncingLiveEditor;
        _isSyncingLiveEditor = true;
        FrameworkElement element;
        try
        {
            element = CreateLiveBlockElement(block, index);
            block.RootElement = element;
        }
        finally
        {
            _isSyncingLiveEditor = prevSync;
        }

        if (index < LiveEditorPanel.Children.Count)
            LiveEditorPanel.Children[index] = element;
        else
            LiveEditorPanel.Children.Add(element);

        if (focusCaret.HasValue)
            FocusLiveBlockAt(index, focusCaret.Value);
    }

    private void InsertLiveBlockAt(int index, LiveDocBlock block, int? focusCaret = null)
    {
        var clampedIdx = Math.Clamp(index, 0, _liveBlocks.Count);
        if (clampedIdx > 0)
            EnsureLiveBlocksMaterializedThrough(clampedIdx - 1);

        _liveBlocks.Insert(clampedIdx, block);

        var prevSync = _isSyncingLiveEditor;
        _isSyncingLiveEditor = true;
        FrameworkElement element;
        try
        {
            element = CreateLiveBlockElement(block, clampedIdx);
            block.RootElement = element;
        }
        finally
        {
            _isSyncingLiveEditor = prevSync;
        }

        if (clampedIdx <= LiveEditorPanel.Children.Count)
            LiveEditorPanel.Children.Insert(clampedIdx, element);
        else
            LiveEditorPanel.Children.Add(element);

        _liveMaterializedCount = LiveEditorPanel.Children.Count;

        if (focusCaret.HasValue)
            FocusLiveBlockAt(clampedIdx, focusCaret.Value);
    }

    private void QueueSyncLiveBlocksToViewModel()
    {
        _liveSyncTimer.Stop();
        _liveSyncTimer.Start();
    }

    private void FlushSyncLiveBlocksToViewModel()
    {
        _liveSyncTimer.Stop();
        var markdown = MarkdownRenderer.SerializeLiveBlocks(_liveBlocks);
        _lastRenderedLiveMarkdown = markdown;
        _isSyncingLiveEditor = true;
        try
        {
            ViewModel.EditorText = markdown;
            if (ViewModel.CurrentViewMode == EditorViewMode.Source && EditorTextBox.Text != markdown)
                EditorTextBox.Text = markdown;
        }
        finally
        {
            _isSyncingLiveEditor = false;
        }
    }

    private FrameworkElement CreateLiveBlockElement(LiveDocBlock block, int blockIndex)
    {
        EnsureLiveRenderResources();

        var isDarkMode = ViewModel.IsDarkMode;
        var baseFontSize = ViewModel.EditorFontSize;
        var primaryBrush = _cachedPrimaryBrush;
        var secondaryBrush = _cachedSecondaryBrush;
        var surfaceBrush = _cachedSurfaceBrush;
        var borderBrush = _cachedBorderBrush;

        block.PrimaryTextBox = null;
        block.RichDisplayHost = null;
        block.CellHostGrid = null;
        block.DisplayTextBlock = null;

        if (block.Kind == LiveBlockKind.TableBlock)
        {
            return MarkdownRenderer.CreateLiveTableElement(
                block,
                isDarkMode,
                baseFontSize,
                QueueSyncLiveBlocksToViewModel,
                onRequestExitBelow: () =>
                {
                    var idx = _liveBlocks.IndexOf(block);
                    if (idx < 0)
                        return;

                    if (idx + 1 < _liveBlocks.Count)
                    {
                        EnsureLiveBlocksMaterializedThrough(idx + 1);
                        if (IsEditableLiveBlock(_liveBlocks[idx + 1]))
                        {
                            FocusLiveBlockAt(idx + 1, 0);
                            return;
                        }
                    }

                    InsertLiveBlockAt(idx + 1, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
                    FlushSyncLiveBlocksToViewModel();
                },
                onRequestDeleteTable: () =>
                {
                    var idx = _liveBlocks.IndexOf(block);
                    if (idx < 0)
                        return;

                    _liveBlocks.RemoveAt(idx);
                    if (idx < LiveEditorPanel.Children.Count)
                        LiveEditorPanel.Children.RemoveAt(idx);
                    _liveMaterializedCount = LiveEditorPanel.Children.Count;

                    if (_liveBlocks.Count == 0)
                        InsertLiveBlockAt(0, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
                    else
                        FocusLiveBlockAt(Math.Clamp(idx - 1, 0, _liveBlocks.Count - 1), 0);

                    FlushSyncLiveBlocksToViewModel();
                });
        }

        if (block.Kind == LiveBlockKind.Divider)
        {
            var dividerContainer = new Grid
            {
                Padding = new Thickness(0, 10, 0, 10),
                Background = TransparentBrush
            };
            dividerContainer.Children.Add(new Border
            {
                Height = 1,
                Background = borderBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            dividerContainer.PointerPressed += (_, _) =>
            {
                var idx = _liveBlocks.IndexOf(block);
                if (idx >= 0)
                {
                    InsertLiveBlockAt(idx + 1, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
                    FlushSyncLiveBlocksToViewModel();
                }
            };
            return dividerContainer;
        }

        if (block.Kind == LiveBlockKind.ImageBlock)
        {
            return MarkdownRenderer.CreateLiveImageElement(
                block,
                isDarkMode,
                baseFontSize,
                ViewModel.SelectedNote?.FolderPath,
                ViewModel.VaultDirectoryPath,
                onRequestEditRaw: () =>
                {
                    var idx = _liveBlocks.IndexOf(block);
                    if (idx >= 0)
                    {
                        block.Kind = LiveBlockKind.Paragraph;
                        RefreshLiveBlockAt(idx, (block.Content ?? string.Empty).Length);
                    }
                },
                onRequestClickBelow: () =>
                {
                    var idx = _liveBlocks.IndexOf(block);
                    if (idx < 0)
                        return;

                    if (idx + 1 < _liveBlocks.Count)
                    {
                        EnsureLiveBlocksMaterializedThrough(idx + 1);
                        if (IsEditableLiveBlock(_liveBlocks[idx + 1]))
                        {
                            FocusLiveBlockAt(idx + 1, 0);
                            return;
                        }
                    }

                    InsertLiveBlockAt(idx + 1, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
                    FlushSyncLiveBlocksToViewModel();
                });
        }

        if (block.Kind == LiveBlockKind.CodeBlock)
        {
            var codeTb = new TextBox
            {
                Text = block.Content,
                PlaceholderText = "Code block...",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                IsSpellCheckEnabled = false,
                FontFamily = MonoFontFamily,
                FontSize = Math.Max(13, baseFontSize - 2),
                Foreground = primaryBrush,
                Background = TransparentBrush,
                SelectionHighlightColor = AccentBrush,
                SelectionHighlightColorWhenNotFocused = SelectionUnfocusedBrush,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                MinHeight = 28,
                Opacity = 1.0,
                Tag = block
            };
            if (_cachedBorderlessStyle is not null)
                codeTb.Style = _cachedBorderlessStyle;

            block.PrimaryTextBox = codeTb;
            WirePooledLiveTextBoxEvents(codeTb);

            return new Border
            {
                Background = surfaceBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 4, 0, 6),
                Child = codeTb
            };
        }

        GetBlockTypography(
            block,
            blockIndex,
            out var fontSize,
            out var fontWeight,
            out var fontStyle,
            out var isTreeLine,
            out var textForeground,
            out var placeholder);

        var tb = RentLiveTextBox(block);
        tb.Text = block.Content ?? string.Empty;
        tb.PlaceholderText = placeholder;
        tb.TextWrapping = isTreeLine ? TextWrapping.NoWrap : TextWrapping.Wrap;
        tb.FontFamily = (block.IsBlockCode || isTreeLine) ? MonoFontFamily : DefaultFontFamily;
        tb.FontSize = fontSize;
        tb.FontWeight = fontWeight;
        tb.FontStyle = fontStyle;
        tb.Foreground = textForeground;
        tb.MinHeight = Math.Max(26, Math.Round(fontSize * 1.45));
        tb.Opacity = 1.0;
        tb.Visibility = Visibility.Visible;
        tb.IsHitTestVisible = true;

        block.PrimaryTextBox = tb;

        var cellGrid = new Grid
        {
            Background = TransparentBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        block.CellHostGrid = cellGrid;
        cellGrid.Children.Add(tb);

        if (block.IsBlockStrikethrough)
        {
            var strikeSizer = new TextBlock
            {
                Text = block.Content,
                FontSize = fontSize,
                FontWeight = fontWeight,
                FontStyle = fontStyle,
                FontFamily = tb.FontFamily,
                Padding = new Thickness(0, 2, 0, 2),
                Opacity = 0.0,
                IsHitTestVisible = false
            };
            var strikeLine = new Border
            {
                Height = 1.5,
                Background = textForeground,
                Opacity = 0.85,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            var strikeGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            strikeGrid.Children.Add(strikeSizer);
            strikeGrid.Children.Add(strikeLine);
            cellGrid.Children.Add(strikeGrid);
        }

        switch (block.Kind)
        {
            case LiveBlockKind.Checklist:
            {
                var row = new Grid
                {
                    Background = TransparentBrush,
                    Margin = new Thickness(0, 2, 0, 2),
                    ColumnSpacing = 8
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var checkBox = new CheckBox
                {
                    IsChecked = block.IsChecked,
                    MinWidth = 0,
                    Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkBox.Click += (_, _) =>
                {
                    EnsureLiveRenderResources();
                    block.IsChecked = checkBox.IsChecked == true;
                    tb.Foreground = block.IsChecked ? _cachedSecondaryBrush : _cachedPrimaryBrush;
                    FlushSyncLiveBlocksToViewModel();
                };

                Grid.SetColumn(checkBox, 0);
                Grid.SetColumn(cellGrid, 1);
                row.Children.Add(checkBox);
                row.Children.Add(cellGrid);
                return row;
            }

            case LiveBlockKind.Bullet:
            {
                var row = new Grid
                {
                    Background = TransparentBrush,
                    Margin = new Thickness(0, 2, 0, 2),
                    ColumnSpacing = 10
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var dot = new TextBlock
                {
                    Text = "•",
                    FontSize = baseFontSize,
                    Foreground = secondaryBrush,
                    Margin = new Thickness(4, 2, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };

                Grid.SetColumn(dot, 0);
                Grid.SetColumn(cellGrid, 1);
                row.Children.Add(dot);
                row.Children.Add(cellGrid);
                return row;
            }

            case LiveBlockKind.Quote:
            {
                return new Border
                {
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = AccentBrush,
                    Background = surfaceBrush,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 3, 0, 3),
                    Child = cellGrid
                };
            }

            case LiveBlockKind.Heading1:
                cellGrid.Margin = new Thickness(0, 10, 0, 4);
                return cellGrid;

            case LiveBlockKind.Heading2:
                cellGrid.Margin = new Thickness(0, 8, 0, 3);
                return cellGrid;

            case LiveBlockKind.Heading3:
                cellGrid.Margin = new Thickness(0, 6, 0, 2);
                return cellGrid;

            default:
            {
                if (block.IsBlockCode)
                {
                    return new Border
                    {
                        Background = surfaceBrush,
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(8, 2, 8, 2),
                        Margin = new Thickness(0, 2, 0, 2),
                        Child = cellGrid
                    };
                }

                cellGrid.Margin = new Thickness(0, 1, 0, 1);
                return cellGrid;
            }
        }
    }

    private void WirePooledLiveTextBoxEvents(TextBox tb)
    {
        tb.GotFocus += (_, _) =>
        {
            if (tb.Tag is LiveDocBlock block)
                _activeLiveBlock = block;
        };

        tb.TextChanged += (_, _) =>
        {
            if (tb.Tag is not LiveDocBlock block)
                return;

            if (block.IsBlockStrikethrough &&
                tb.Parent is Grid { Children.Count: > 1 } cellGrid &&
                cellGrid.Children[^1] is Grid { Children.Count: > 0 } strikeGrid &&
                strikeGrid.Children[0] is TextBlock strikeSizer)
            {
                strikeSizer.Text = tb.Text ?? string.Empty;
            }

            if (_isSyncingLiveEditor)
                return;

            block.Content = tb.Text ?? string.Empty;

            if (TryAutoConvertLiveBlock(block, tb))
                return;

            QueueSyncLiveBlocksToViewModel();
            UpdateSelectionToolbar();
            UpdateSlashMenuAutocomplete();
            UpdateMentionAutocomplete();
            UpdateActiveMentionJumpPill();

            if (ViewModel.IsFluidAnimationEnabled && tb.FocusState != FocusState.Unfocused)
            {
                var text = tb.Text ?? string.Empty;
                var caret = Math.Clamp(tb.SelectionStart, 0, text.Length);
                var activeWord = ExtractCurrentWord(text, caret);
                TriggerWordTypingAnimation(activeWord);
            }
        };

        tb.SelectionChanged += (_, _) =>
        {
            if (_isSyncingLiveEditor || tb.Tag is not LiveDocBlock block)
                return;

            _activeLiveBlock = block;
            UpdateSelectionToolbar();
            UpdateSlashMenuAutocomplete();
            UpdateMentionAutocomplete();
            UpdateActiveMentionJumpPill();
        };

        tb.PointerReleased += (_, _) =>
        {
            if (tb.Tag is not LiveDocBlock)
                return;

            var ctrlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            if (ctrlDown && TryGetMentionedNoteAtCaret(out var targetNote) && targetNote is not null)
                NavigateToNote(targetNote);
        };

        tb.PreviewKeyDown += (_, e) =>
        {
            if (tb.Tag is LiveDocBlock block)
                OnLiveBlockPreviewKeyDown(block, tb, e);
        };
    }

    private bool TryAutoConvertLiveBlock(LiveDocBlock block, TextBox tb)
    {
        var idx = _liveBlocks.IndexOf(block);
        if (idx < 0)
            return false;

        var text = tb.Text ?? string.Empty;

        if (block.Kind == LiveBlockKind.Paragraph)
        {
            if (text.StartsWith("### ", StringComparison.Ordinal))
            {
                block.Kind = LiveBlockKind.Heading3;
                block.Content = text[4..];
                FlushSyncLiveBlocksToViewModel();
                RefreshLiveBlockAt(idx, 0);
                return true;
            }
            if (text.StartsWith("## ", StringComparison.Ordinal))
            {
                block.Kind = LiveBlockKind.Heading2;
                block.Content = text[3..];
                FlushSyncLiveBlocksToViewModel();
                RefreshLiveBlockAt(idx, 0);
                return true;
            }
            if (text.StartsWith("# ", StringComparison.Ordinal))
            {
                block.Kind = LiveBlockKind.Heading1;
                block.Content = text[2..];
                FlushSyncLiveBlocksToViewModel();
                RefreshLiveBlockAt(idx, 0);
                return true;
            }
            if (text.StartsWith("- [ ] ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("[] ", StringComparison.OrdinalIgnoreCase))
            {
                var prefixLen = text.StartsWith("[] ", StringComparison.OrdinalIgnoreCase) ? 3 : 6;
                block.Kind = LiveBlockKind.Checklist;
                block.IsChecked = false;
                block.Content = text[prefixLen..];
                FlushSyncLiveBlocksToViewModel();
                RefreshLiveBlockAt(idx, 0);
                return true;
            }
            if (text.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
            {
                block.Kind = LiveBlockKind.Checklist;
                block.IsChecked = true;
                block.Content = text[6..];
                FlushSyncLiveBlocksToViewModel();
                RefreshLiveBlockAt(idx, 0);
                return true;
            }
            if (text.StartsWith("- ", StringComparison.Ordinal) ||
                text.StartsWith("* ", StringComparison.Ordinal))
            {
                block.Kind = LiveBlockKind.Bullet;
                block.Content = text[2..];
                FlushSyncLiveBlocksToViewModel();
                RefreshLiveBlockAt(idx, 0);
                return true;
            }
            if (text.StartsWith("> ", StringComparison.Ordinal))
            {
                block.Kind = LiveBlockKind.Quote;
                block.Content = text[2..];
                FlushSyncLiveBlocksToViewModel();
                RefreshLiveBlockAt(idx, 0);
                return true;
            }
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                block.Kind = LiveBlockKind.CodeBlock;
                block.Content = text[3..].TrimStart();
                FlushSyncLiveBlocksToViewModel();
                RefreshLiveBlockAt(idx, block.Content.Length);
                return true;
            }
            if (text is "---" or "***")
            {
                block.Kind = LiveBlockKind.Divider;
                block.Content = string.Empty;
                RefreshLiveBlockAt(idx);
                InsertLiveBlockAt(idx + 1, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
                FlushSyncLiveBlocksToViewModel();
                return true;
            }
            if (MarkdownRenderer.TryParseImageLine(text, out var parsedSrc, out _, out _, out _) &&
                MarkdownRenderer.TryResolveImageUri(parsedSrc, ViewModel.SelectedNote?.FolderPath, ViewModel.VaultDirectoryPath, out _))
            {
                block.Kind = LiveBlockKind.ImageBlock;
                block.Content = text;
                RefreshLiveBlockAt(idx);
                if (idx == _liveBlocks.Count - 1)
                    InsertLiveBlockAt(idx + 1, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
                FlushSyncLiveBlocksToViewModel();
                return true;
            }
        }

        if (block.Kind != LiveBlockKind.CodeBlock && block.Kind != LiveBlockKind.TableBlock)
        {
            if (text.Length >= 5 &&
                text.StartsWith("**", StringComparison.Ordinal) &&
                text.EndsWith("**", StringComparison.Ordinal) &&
                text.IndexOf("**", 2, StringComparison.Ordinal) == text.Length - 2)
            {
                block.IsBlockBold = true;
                block.Content = text[2..^2];
                FlushSyncLiveBlocksToViewModel();
                RefreshLiveBlockAt(idx, block.Content.Length);
                return true;
            }

            if (text.Length >= 5 &&
                text.StartsWith("~~", StringComparison.Ordinal) &&
                text.EndsWith("~~", StringComparison.Ordinal) &&
                text.IndexOf("~~", 2, StringComparison.Ordinal) == text.Length - 2)
            {
                block.IsBlockStrikethrough = true;
                block.Content = text[2..^2];
                FlushSyncLiveBlocksToViewModel();
                RefreshLiveBlockAt(idx, block.Content.Length);
                return true;
            }

            if (text.Length >= 3 &&
                text.StartsWith('`') &&
                text.EndsWith('`') &&
                text.IndexOf('`', 1) == text.Length - 1)
            {
                block.IsBlockCode = true;
                block.Content = text[1..^1];
                FlushSyncLiveBlocksToViewModel();
                RefreshLiveBlockAt(idx, block.Content.Length);
                return true;
            }
        }

        return false;
    }

    private void OnLiveBlockPreviewKeyDown(LiveDocBlock block, TextBox tb, KeyRoutedEventArgs e)
    {
        var ctrlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

        if (!ctrlDown && SlashMenuPopupBorder.Visibility == Visibility.Visible && ViewModel.SlashMenuSuggestions.Count > 0)
        {
            if (e.Key == VirtualKey.Down)
            {
                e.Handled = true;
                var next = (SlashMenuListView.SelectedIndex + 1) % ViewModel.SlashMenuSuggestions.Count;
                SlashMenuListView.SelectedIndex = next;
                SlashMenuListView.ScrollIntoView(ViewModel.SlashMenuSuggestions[next]);
                return;
            }
            if (e.Key == VirtualKey.Up)
            {
                e.Handled = true;
                var prev = (SlashMenuListView.SelectedIndex - 1 + ViewModel.SlashMenuSuggestions.Count) % ViewModel.SlashMenuSuggestions.Count;
                SlashMenuListView.SelectedIndex = prev;
                SlashMenuListView.ScrollIntoView(ViewModel.SlashMenuSuggestions[prev]);
                return;
            }
            if (e.Key is VirtualKey.Enter or VirtualKey.Tab)
            {
                e.Handled = true;
                var selected = SlashMenuListView.SelectedItem as SlashCommandItem ?? ViewModel.SlashMenuSuggestions[0];
                ApplySlashCommand(selected);
                return;
            }
        }

        if (!ctrlDown && MentionPopupBorder.Visibility == Visibility.Visible && ViewModel.MentionSuggestions.Count > 0)
        {
            if (e.Key == VirtualKey.Down)
            {
                e.Handled = true;
                var next = (MentionListView.SelectedIndex + 1) % ViewModel.MentionSuggestions.Count;
                MentionListView.SelectedIndex = next;
                MentionListView.ScrollIntoView(ViewModel.MentionSuggestions[next]);
                return;
            }
            if (e.Key == VirtualKey.Up)
            {
                e.Handled = true;
                var prev = (MentionListView.SelectedIndex - 1 + ViewModel.MentionSuggestions.Count) % ViewModel.MentionSuggestions.Count;
                MentionListView.SelectedIndex = prev;
                MentionListView.ScrollIntoView(ViewModel.MentionSuggestions[prev]);
                return;
            }
            if (e.Key is VirtualKey.Enter or VirtualKey.Tab)
            {
                e.Handled = true;
                var selected = MentionListView.SelectedItem as NoteItemViewModel ?? ViewModel.MentionSuggestions[0];
                InsertMention(selected);
                return;
            }
        }

        var blockIdx = _liveBlocks.IndexOf(block);
        if (blockIdx < 0)
            return;

        if (ctrlDown)
        {
            switch (e.Key)
            {
                case VirtualKey.B:
                    e.Handled = true;
                    ApplyLiveInlineFormat(block, tb, "**", "**", toggleBlockBold: true);
                    return;
                case VirtualKey.I:
                    e.Handled = true;
                    ApplyLiveInlineFormat(block, tb, "*", "*", toggleBlockItalic: true);
                    return;
                case VirtualKey.L:
                    e.Handled = true;
                    block.Kind = block.Kind == LiveBlockKind.Checklist ? LiveBlockKind.Paragraph : LiveBlockKind.Checklist;
                    FlushSyncLiveBlocksToViewModel();
                    RefreshLiveBlockAt(blockIdx, tb.SelectionStart);
                    return;
                case VirtualKey.Number1:
                    e.Handled = true;
                    block.Kind = block.Kind == LiveBlockKind.Heading1 ? LiveBlockKind.Paragraph : LiveBlockKind.Heading1;
                    FlushSyncLiveBlocksToViewModel();
                    RefreshLiveBlockAt(blockIdx, tb.SelectionStart);
                    return;
                case VirtualKey.Number2:
                    e.Handled = true;
                    block.Kind = block.Kind == LiveBlockKind.Heading2 ? LiveBlockKind.Paragraph : LiveBlockKind.Heading2;
                    FlushSyncLiveBlocksToViewModel();
                    RefreshLiveBlockAt(blockIdx, tb.SelectionStart);
                    return;
                case VirtualKey.Number3:
                    e.Handled = true;
                    block.Kind = block.Kind == LiveBlockKind.Heading3 ? LiveBlockKind.Paragraph : LiveBlockKind.Heading3;
                    FlushSyncLiveBlocksToViewModel();
                    RefreshLiveBlockAt(blockIdx, tb.SelectionStart);
                    return;
            }
            return;
        }

        var text = tb.Text ?? string.Empty;
        var caret = Math.Clamp(tb.SelectionStart, 0, text.Length);

        if (e.Key == VirtualKey.Tab && ViewModel.IsTreeShortcutEnabled && TryParseTreeLine(text, out var indent, out _, out _))
        {
            e.Handled = true;
            if (shiftDown)
            {
                if (indent.Length >= BranchIndent.Length)
                {
                    block.Content = text[BranchIndent.Length..];
                    tb.Text = block.Content;
                    tb.SelectionStart = Math.Max(0, caret - BranchIndent.Length);
                    QueueSyncLiveBlocksToViewModel();
                }
            }
            else
            {
                block.Content = BranchIndent + text;
                tb.Text = block.Content;
                tb.SelectionStart = caret + BranchIndent.Length;
                QueueSyncLiveBlocksToViewModel();
            }
            return;
        }

        if (e.Key == VirtualKey.Enter && block.Kind != LiveBlockKind.CodeBlock)
        {
            e.Handled = true;

            if (block.Kind == LiveBlockKind.Paragraph &&
                MarkdownRenderer.TryParseImageLine(text.Trim(), out _, out _, out _, out _))
            {
                block.Kind = LiveBlockKind.ImageBlock;
                block.Content = text;
                RefreshLiveBlockAt(blockIdx);
                InsertLiveBlockAt(blockIdx + 1, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
                FlushSyncLiveBlocksToViewModel();
                return;
            }

            if (shiftDown && ViewModel.IsTreeShortcutEnabled)
            {
                if (TryParseTreeLine(text, out var treeIndent, out var isMid, out var treeBody))
                {
                    if (string.IsNullOrWhiteSpace(treeBody))
                    {
                        block.Content = string.Empty;
                        RefreshLiveBlockAt(blockIdx, 0);
                        FlushSyncLiveBlocksToViewModel();
                        return;
                    }

                    if (!isMid)
                    {
                        var prefixOffset = treeIndent.Length;
                        block.Content = treeIndent + BranchMid + text[(prefixOffset + BranchEnd.Length)..];
                        tb.Text = block.Content;
                    }

                    var nextTreeLine = treeIndent + BranchEnd;
                    InsertLiveBlockAt(blockIdx + 1, new LiveDocBlock
                    {
                        Kind = LiveBlockKind.Paragraph,
                        Content = nextTreeLine
                    }, focusCaret: nextTreeLine.Length);
                    FlushSyncLiveBlocksToViewModel();
                    return;
                }

                InsertLiveBlockAt(blockIdx + 1, new LiveDocBlock
                {
                    Kind = LiveBlockKind.Paragraph,
                    Content = BranchEnd
                }, focusCaret: BranchEnd.Length);
                FlushSyncLiveBlocksToViewModel();
                return;
            }

            if (block.Kind is LiveBlockKind.Checklist or LiveBlockKind.Bullet or LiveBlockKind.Quote &&
                string.IsNullOrWhiteSpace(text))
            {
                block.Kind = LiveBlockKind.Paragraph;
                block.IsChecked = false;
                RefreshLiveBlockAt(blockIdx, 0);
                FlushSyncLiveBlocksToViewModel();
                return;
            }

            var before = text[..caret];
            var after = text[caret..];
            block.Content = before;
            if (tb.Text != before)
                tb.Text = before;

            var nextKind = block.Kind is LiveBlockKind.Checklist or LiveBlockKind.Bullet
                ? block.Kind
                : LiveBlockKind.Paragraph;

            InsertLiveBlockAt(blockIdx + 1, new LiveDocBlock
            {
                Kind = nextKind,
                Content = after
            }, focusCaret: 0);
            FlushSyncLiveBlocksToViewModel();
            return;
        }

        if (e.Key == VirtualKey.Back && tb.SelectionStart == 0 && tb.SelectionLength == 0)
        {
            if (block.Kind != LiveBlockKind.Paragraph || block.IsBlockBold || block.IsBlockItalic || block.IsBlockStrikethrough || block.IsBlockCode)
            {
                e.Handled = true;
                block.Kind = LiveBlockKind.Paragraph;
                block.IsChecked = false;
                block.IsBlockBold = false;
                block.IsBlockItalic = false;
                block.IsBlockStrikethrough = false;
                block.IsBlockCode = false;
                RefreshLiveBlockAt(blockIdx, 0);
                FlushSyncLiveBlocksToViewModel();
                return;
            }

            if (blockIdx > 0)
            {
                e.Handled = true;
                var prevBlock = _liveBlocks[blockIdx - 1];
                if (prevBlock.Kind is LiveBlockKind.Divider or LiveBlockKind.ImageBlock)
                {
                    _liveBlocks.RemoveAt(blockIdx - 1);
                    if (blockIdx - 1 < LiveEditorPanel.Children.Count)
                        LiveEditorPanel.Children.RemoveAt(blockIdx - 1);
                    _liveMaterializedCount = LiveEditorPanel.Children.Count;
                    FocusLiveBlockAt(blockIdx - 1, 0);
                    FlushSyncLiveBlocksToViewModel();
                    return;
                }

                if (prevBlock.Kind != LiveBlockKind.TableBlock)
                {
                    var prevLen = (prevBlock.Content ?? string.Empty).Length;
                    prevBlock.Content = (prevBlock.Content ?? string.Empty) + text;
                    RecycleBlockTextBox(_liveBlocks[blockIdx]);
                    _liveBlocks.RemoveAt(blockIdx);
                    if (blockIdx < LiveEditorPanel.Children.Count)
                        LiveEditorPanel.Children.RemoveAt(blockIdx);
                    _liveMaterializedCount = LiveEditorPanel.Children.Count;
                    RefreshLiveBlockAt(blockIdx - 1, prevLen);
                    FlushSyncLiveBlocksToViewModel();
                }
                return;
            }
        }

        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            SelectionToolbarBorder.Visibility = Visibility.Collapsed;
            tb.SelectionLength = 0;
            return;
        }

        if (e.Key == VirtualKey.Up && block.Kind != LiveBlockKind.CodeBlock)
        {
            for (var i = blockIdx - 1; i >= 0; i--)
            {
                if (IsEditableLiveBlock(_liveBlocks[i]))
                {
                    e.Handled = true;
                    FocusLiveBlockAt(i, caret);
                    return;
                }
            }
        }

        if (e.Key == VirtualKey.Down && block.Kind != LiveBlockKind.CodeBlock)
        {
            for (var i = blockIdx + 1; i < _liveBlocks.Count; i++)
            {
                EnsureLiveBlocksMaterializedThrough(i);
                if (IsEditableLiveBlock(_liveBlocks[i]))
                {
                    e.Handled = true;
                    FocusLiveBlockAt(i, caret);
                    return;
                }
            }
        }
    }

    private void ApplyLiveInlineFormat(
        LiveDocBlock block,
        TextBox tb,
        string prefix,
        string suffix,
        bool toggleBlockBold = false,
        bool toggleBlockItalic = false,
        bool toggleBlockStrikethrough = false,
        bool toggleBlockCode = false)
    {
        var idx = _liveBlocks.IndexOf(block);
        if (idx < 0)
            return;

        var caret = Math.Clamp(tb.SelectionStart + tb.SelectionLength, 0, (tb.Text ?? string.Empty).Length);

        if (toggleBlockBold)
            block.IsBlockBold = !block.IsBlockBold;
        else if (toggleBlockItalic)
            block.IsBlockItalic = !block.IsBlockItalic;
        else if (toggleBlockStrikethrough)
            block.IsBlockStrikethrough = !block.IsBlockStrikethrough;
        else if (toggleBlockCode)
            block.IsBlockCode = !block.IsBlockCode;

        SelectionToolbarBorder.Visibility = Visibility.Collapsed;
        FlushSyncLiveBlocksToViewModel();
        RefreshLiveBlockAt(idx, caret);
    }

    private void WrapEditorSelection(string prefix, string suffix)
    {
        if (ViewModel.CurrentViewMode == EditorViewMode.Live && _activeLiveBlock is { PrimaryTextBox: { } liveTb } liveBlock)
        {
            ApplyLiveInlineFormat(
                liveBlock,
                liveTb,
                prefix,
                suffix,
                toggleBlockBold: prefix == "**",
                toggleBlockItalic: prefix == "*",
                toggleBlockStrikethrough: prefix == "~~",
                toggleBlockCode: prefix == "`");
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var start = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
        var length = Math.Clamp(EditorTextBox.SelectionLength, 0, text.Length - start);
        var selected = length > 0 ? text.Substring(start, length) : "text";
        var replacement = $"{prefix}{selected}{suffix}";

        var updated = text[..start] + replacement + text[(start + length)..];
        EditorTextBox.Text = updated;
        ViewModel.EditorText = updated;
        EditorTextBox.SelectionStart = start + prefix.Length;
        EditorTextBox.SelectionLength = selected.Length;
    }

    private void ToggleLinePrefix(string prefix)
    {
        var text = EditorTextBox.Text ?? string.Empty;
        var caret = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
        var lineStart = FindLineStart(text, caret);
        var lineEnd = FindLineEnd(text, caret);
        var line = text[lineStart..lineEnd];

        if (line.StartsWith(prefix, StringComparison.Ordinal))
        {
            var updated = text.Remove(lineStart, prefix.Length);
            ApplyEditorText(updated, Math.Max(lineStart, caret - prefix.Length));
        }
        else
        {
            var updated = text.Insert(lineStart, prefix);
            ApplyEditorText(updated, caret + prefix.Length);
        }
    }

    private void ToggleHeadingPrefix(string headingPrefix)
    {
        var text = EditorTextBox.Text ?? string.Empty;
        var caret = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
        var lineStart = FindLineStart(text, caret);
        var lineEnd = FindLineEnd(text, caret);
        var line = text[lineStart..lineEnd];

        var stripped = line.TrimStart('#', ' ');
        var existingPrefixLen = line.Length - stripped.Length;

        if (line.StartsWith(headingPrefix, StringComparison.Ordinal))
        {
            var updated = text.Remove(lineStart, existingPrefixLen);
            ApplyEditorText(updated, Math.Max(lineStart, caret - existingPrefixLen));
        }
        else
        {
            var updated = text[..lineStart] + headingPrefix + stripped + text[lineEnd..];
            ApplyEditorText(updated, caret - existingPrefixLen + headingPrefix.Length);
        }
    }

    private void ApplyEditorText(string updatedText, int newCaretIndex)
    {
        EditorTextBox.Text = updatedText;
        ViewModel.EditorText = updatedText;
        EditorTextBox.SelectionStart = Math.Clamp(newCaretIndex, 0, updatedText.Length);
        EditorTextBox.SelectionLength = 0;
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingLiveEditor || ViewModel.CurrentViewMode != EditorViewMode.Source)
            return;

        UpdateSelectionToolbar();
        UpdateSlashMenuAutocomplete();
        UpdateMentionAutocomplete();
        UpdateActiveMentionJumpPill();

        if (!ViewModel.IsFluidAnimationEnabled || EditorTextBox.FocusState == FocusState.Unfocused)
            return;

        var text = EditorTextBox.Text ?? string.Empty;
        var caret = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
        var activeWord = ExtractCurrentWord(text, caret);
        TriggerWordTypingAnimation(activeWord);
    }

    private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncingLiveEditor || ViewModel.CurrentViewMode != EditorViewMode.Source)
            return;

        UpdateSelectionToolbar();
        UpdateSlashMenuAutocomplete();
        UpdateMentionAutocomplete();
        UpdateActiveMentionJumpPill();
    }

    private void OnEditorPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var ctrlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (ctrlDown && TryGetMentionedNoteAtCaret(out var targetNote) && targetNote is not null)
        {
            NavigateToNote(targetNote);
        }
    }

    private void UpdateSelectionToolbar()
    {
        var activeTb = GetActiveEditorTextBox();
        var hasSelection = activeTb.SelectionLength > 0;
        SelectionToolbarBorder.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSelectionBoldClick(object sender, RoutedEventArgs e)
    {
        WrapEditorSelection("**", "**");
        FocusActiveEditorPane();
    }

    private void OnSelectionItalicClick(object sender, RoutedEventArgs e)
    {
        WrapEditorSelection("*", "*");
        FocusActiveEditorPane();
    }

    private void OnSelectionStrikethroughClick(object sender, RoutedEventArgs e)
    {
        WrapEditorSelection("~~", "~~");
        FocusActiveEditorPane();
    }

    private void OnSelectionCodeClick(object sender, RoutedEventArgs e)
    {
        WrapEditorSelection("`", "`");
        FocusActiveEditorPane();
    }

    private void OnSelectionLinkClick(object sender, RoutedEventArgs e)
    {
        var activeTb = GetActiveEditorTextBox();
        var text = activeTb.Text ?? string.Empty;
        var start = Math.Clamp(activeTb.SelectionStart, 0, text.Length);
        var length = Math.Clamp(activeTb.SelectionLength, 0, text.Length - start);
        var selected = length > 0 ? text.Substring(start, length) : "link";
        const string defaultUrl = "https://";
        var replacement = $"[{selected}]({defaultUrl})";

        var updated = text[..start] + replacement + text[(start + length)..];
        activeTb.Text = updated;

        if (ViewModel.CurrentViewMode == EditorViewMode.Live && _activeLiveBlock is not null)
        {
            _activeLiveBlock.Content = updated;
            FlushSyncLiveBlocksToViewModel();
        }
        else
        {
            ViewModel.EditorText = updated;
        }

        activeTb.SelectionStart = start + selected.Length + 3;
        activeTb.SelectionLength = defaultUrl.Length;
        activeTb.Focus(FocusState.Programmatic);
    }

    private void UpdateSlashMenuAutocomplete()
    {
        var activeTb = GetActiveEditorTextBox();
        if (activeTb.FocusState == FocusState.Unfocused || activeTb.SelectionLength > 0)
        {
            HideSlashMenuPopup();
            return;
        }

        var text = activeTb.Text ?? string.Empty;
        var caret = Math.Clamp(activeTb.SelectionStart, 0, text.Length);

        if (TryGetActiveSlashTrigger(text, caret, out var slashIndex, out var query))
        {
            _activeSlashIndex = slashIndex;
            ViewModel.UpdateSlashSuggestions(query);
            if (ViewModel.SlashMenuSuggestions.Count > 0)
            {
                SlashMenuPopupBorder.Visibility = Visibility.Visible;
                if (SlashMenuListView.SelectedIndex < 0 || SlashMenuListView.SelectedIndex >= ViewModel.SlashMenuSuggestions.Count)
                    SlashMenuListView.SelectedIndex = 0;
                return;
            }
        }

        HideSlashMenuPopup();
    }

    private void HideSlashMenuPopup()
    {
        _activeSlashIndex = -1;
        if (SlashMenuPopupBorder.Visibility != Visibility.Collapsed)
            SlashMenuPopupBorder.Visibility = Visibility.Collapsed;
    }

    private static bool TryGetActiveSlashTrigger(string text, int caret, out int slashIndex, out string query)
    {
        slashIndex = -1;
        query = string.Empty;

        if (string.IsNullOrEmpty(text) || caret <= 0 || caret > text.Length)
            return false;

        var lineStart = FindLineStart(text, caret);
        for (var i = caret - 1; i >= lineStart; i--)
        {
            var ch = text[i];
            if (ch is '\r' or '\n' or ' ' or '\t')
                break;

            if (ch == '/')
            {
                var validStart = i == lineStart || char.IsWhiteSpace(text[i - 1]);
                if (!validStart)
                    return false;

                var candidate = text[(i + 1)..caret];
                if (candidate.Length > 20 || candidate.Contains('/'))
                    return false;

                slashIndex = i;
                query = candidate;
                return true;
            }
        }

        return false;
    }

    private void OnSlashMenuItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SlashCommandItem item)
            ApplySlashCommand(item);
    }

    private void ApplySlashCommand(SlashCommandItem item)
    {
        if (ViewModel.CurrentViewMode == EditorViewMode.Live && _activeLiveBlock is { PrimaryTextBox: { } liveTb } liveBlock)
        {
            ApplyLiveSlashCommand(liveBlock, liveTb, item);
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var caret = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
        var slashIndex = _activeSlashIndex;

        if (slashIndex < 0 || slashIndex >= caret || slashIndex >= text.Length || text[slashIndex] != '/')
        {
            if (!TryGetActiveSlashTrigger(text, caret, out slashIndex, out _))
            {
                HideSlashMenuPopup();
                return;
            }
        }

        var baseText = text[..slashIndex] + text[caret..];
        var lineStart = FindLineStart(baseText, slashIndex);
        var lineEnd = FindLineEnd(baseText, slashIndex);
        var lineContent = baseText[lineStart..lineEnd];

        string updated;
        int newCaret;

        switch (item.Id)
        {
            case "h1":
            case "h2":
            case "h3":
            {
                var prefix = item.Id switch
                {
                    "h1" => "# ",
                    "h2" => "## ",
                    _ => "### "
                };
                var stripped = StripBlockFormattingPrefix(lineContent);
                updated = baseText[..lineStart] + prefix + stripped + baseText[lineEnd..];
                newCaret = lineStart + prefix.Length + stripped.Length;
                break;
            }

            case "paragraph":
            {
                var stripped = StripBlockFormattingPrefix(lineContent);
                updated = baseText[..lineStart] + stripped + baseText[lineEnd..];
                newCaret = lineStart + stripped.Length;
                break;
            }

            case "checklist":
            {
                const string prefix = "- [ ] ";
                var stripped = StripBlockFormattingPrefix(lineContent);
                updated = baseText[..lineStart] + prefix + stripped + baseText[lineEnd..];
                newCaret = lineStart + prefix.Length + stripped.Length;
                break;
            }

            case "quote":
            {
                const string prefix = "> ";
                var stripped = StripBlockFormattingPrefix(lineContent);
                updated = baseText[..lineStart] + prefix + stripped + baseText[lineEnd..];
                newCaret = lineStart + prefix.Length + stripped.Length;
                break;
            }

            case "code":
            {
                var leadingNewline = slashIndex > lineStart && !string.IsNullOrWhiteSpace(baseText[lineStart..slashIndex]) ? "\r" : string.Empty;
                var block = $"{leadingNewline}```\r\r```";
                updated = baseText.Insert(slashIndex, block);
                newCaret = slashIndex + leadingNewline.Length + 4;
                break;
            }

            case "table":
            {
                var leadingNewline = slashIndex > lineStart && !string.IsNullOrWhiteSpace(baseText[lineStart..slashIndex]) ? "\r" : string.Empty;
                const string tableTemplate =
                    "| Column 1 | Column 2 | Column 3 |\r" +
                    "| --- | --- | --- |\r" +
                    "|  |  |  |\r" +
                    "|  |  |  |";
                var block = leadingNewline + tableTemplate;
                updated = baseText.Insert(slashIndex, block);
                newCaret = slashIndex + leadingNewline.Length + 2;
                break;
            }

            default:
                HideSlashMenuPopup();
                return;
        }

        HideSlashMenuPopup();
        ApplyEditorText(updated, newCaret);
        EditorTextBox.Focus(FocusState.Programmatic);
    }

    private void ApplyLiveSlashCommand(LiveDocBlock block, TextBox tb, SlashCommandItem item)
    {
        var idx = _liveBlocks.IndexOf(block);
        if (idx < 0)
        {
            HideSlashMenuPopup();
            return;
        }

        var text = tb.Text ?? string.Empty;
        var caret = Math.Clamp(tb.SelectionStart, 0, text.Length);
        var slashIndex = _activeSlashIndex;

        if (slashIndex < 0 || slashIndex >= caret || slashIndex >= text.Length || text[slashIndex] != '/')
        {
            if (!TryGetActiveSlashTrigger(text, caret, out slashIndex, out _))
                slashIndex = caret;
        }

        var cleaned = (text[..slashIndex] + text[caret..]).TrimStart();
        HideSlashMenuPopup();

        switch (item.Id)
        {
            case "h1":
                block.Kind = LiveBlockKind.Heading1;
                block.Content = cleaned;
                break;

            case "h2":
                block.Kind = LiveBlockKind.Heading2;
                block.Content = cleaned;
                break;

            case "h3":
                block.Kind = LiveBlockKind.Heading3;
                block.Content = cleaned;
                break;

            case "paragraph":
                block.Kind = LiveBlockKind.Paragraph;
                block.IsBlockBold = false;
                block.IsBlockItalic = false;
                block.IsBlockStrikethrough = false;
                block.IsBlockCode = false;
                block.Content = cleaned;
                break;

            case "checklist":
                block.Kind = LiveBlockKind.Checklist;
                block.IsChecked = false;
                block.Content = cleaned;
                break;

            case "quote":
                block.Kind = LiveBlockKind.Quote;
                block.Content = cleaned;
                break;

            case "code":
                block.Kind = LiveBlockKind.CodeBlock;
                block.Content = cleaned;
                break;

            case "table":
            {
                var tableMatrix = new List<List<string>>
                {
                    new() { "Column 1", "Column 2", "Column 3" },
                    new() { string.Empty, string.Empty, string.Empty },
                    new() { string.Empty, string.Empty, string.Empty }
                };

                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    block.Kind = LiveBlockKind.TableBlock;
                    block.Content = string.Empty;
                    block.TableMatrix = tableMatrix;
                    RefreshLiveBlockAt(idx);
                    if (idx == _liveBlocks.Count - 1)
                        InsertLiveBlockAt(idx + 1, new LiveDocBlock { Kind = LiveBlockKind.Paragraph });
                    FocusLiveBlockAt(idx, 0);
                }
                else
                {
                    block.Content = cleaned;
                    tb.Text = cleaned;
                    InsertLiveBlockAt(idx + 1, new LiveDocBlock
                    {
                        Kind = LiveBlockKind.TableBlock,
                        TableMatrix = tableMatrix
                    });
                    if (idx + 1 == _liveBlocks.Count - 1)
                        InsertLiveBlockAt(idx + 2, new LiveDocBlock { Kind = LiveBlockKind.Paragraph });
                    FocusLiveBlockAt(idx + 1, 0);
                }

                FlushSyncLiveBlocksToViewModel();
                return;
            }

            default:
                return;
        }

        FlushSyncLiveBlocksToViewModel();
        RefreshLiveBlockAt(idx, block.Content.Length);
    }

    private static string StripBlockFormattingPrefix(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("- [ ] ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
            return trimmed[6..];
        if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            return trimmed[4..];
        if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            return trimmed[3..];
        if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            return trimmed[2..];
        if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            return trimmed[2..];
        if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            return trimmed[2..];
        return line;
    }

    private void UpdateMentionAutocomplete()
    {
        var activeTb = GetActiveEditorTextBox();
        if (activeTb.FocusState == FocusState.Unfocused || SlashMenuPopupBorder.Visibility == Visibility.Visible)
        {
            HideMentionPopup();
            return;
        }

        var text = activeTb.Text ?? string.Empty;
        var caret = Math.Clamp(activeTb.SelectionStart, 0, text.Length);

        if (TryGetActiveMentionTrigger(text, caret, out var atIndex, out var query))
        {
            _activeMentionAtIndex = atIndex;
            ViewModel.UpdateMentionSuggestions(query);
            if (ViewModel.MentionSuggestions.Count > 0)
            {
                MentionPopupBorder.Visibility = Visibility.Visible;
                if (MentionListView.SelectedIndex < 0 || MentionListView.SelectedIndex >= ViewModel.MentionSuggestions.Count)
                    MentionListView.SelectedIndex = 0;
                return;
            }
        }

        HideMentionPopup();
    }

    private void HideMentionPopup()
    {
        _activeMentionAtIndex = -1;
        if (MentionPopupBorder.Visibility != Visibility.Collapsed)
            MentionPopupBorder.Visibility = Visibility.Collapsed;
    }

    private static bool TryGetActiveMentionTrigger(string text, int caret, out int atIndex, out string query)
    {
        atIndex = -1;
        query = string.Empty;

        if (string.IsNullOrEmpty(text) || caret <= 0 || caret > text.Length)
            return false;

        var lineStart = FindLineStart(text, caret);
        for (var i = caret - 1; i >= lineStart; i--)
        {
            var ch = text[i];
            if (ch is '\r' or '\n')
                break;

            if (ch == '@')
            {
                var validStart = i == lineStart || char.IsWhiteSpace(text[i - 1]) || text[i - 1] is '(' or '[' or '-' or '─';
                if (!validStart)
                    return false;

                var candidate = text[(i + 1)..caret];
                if (candidate.Length > 28 || candidate.Contains("  ", StringComparison.Ordinal))
                    return false;

                atIndex = i;
                query = candidate.TrimStart('[');
                return true;
            }
        }

        return false;
    }

    private void InsertMention(NoteItemViewModel note)
    {
        var activeTb = GetActiveEditorTextBox();
        var text = activeTb.Text ?? string.Empty;
        var caret = Math.Clamp(activeTb.SelectionStart, 0, text.Length);
        var atIndex = _activeMentionAtIndex;

        if (atIndex < 0 || atIndex >= caret || atIndex >= text.Length || text[atIndex] != '@')
        {
            if (!TryGetActiveMentionTrigger(text, caret, out atIndex, out _))
            {
                HideMentionPopup();
                return;
            }
        }

        var mentionText = $"@{note.Title} ";
        var updated = text[..atIndex] + mentionText + text[caret..];
        var newCaret = atIndex + mentionText.Length;

        HideMentionPopup();

        if (ViewModel.CurrentViewMode == EditorViewMode.Live && _activeLiveBlock is not null)
        {
            _activeLiveBlock.Content = updated;
            activeTb.Text = updated;
            activeTb.SelectionStart = Math.Clamp(newCaret, 0, updated.Length);
            activeTb.SelectionLength = 0;
            FlushSyncLiveBlocksToViewModel();
            activeTb.Focus(FocusState.Programmatic);
        }
        else
        {
            ApplyEditorText(updated, newCaret);
            EditorTextBox.Focus(FocusState.Programmatic);
        }
    }

    private void OnMentionItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is NoteItemViewModel note)
            InsertMention(note);
    }

    private void UpdateActiveMentionJumpPill()
    {
        if (TryGetMentionedNoteAtCaret(out var note) && note is not null)
        {
            ActiveMentionJumpButton.Tag = note;
            ActiveMentionJumpButton.Content = $"Open @{note.Title} →";
            ActiveMentionJumpButton.Visibility = Visibility.Visible;
        }
        else
        {
            ActiveMentionJumpButton.Tag = null;
            ActiveMentionJumpButton.Visibility = Visibility.Collapsed;
        }
    }

    private bool TryGetMentionedNoteAtCaret(out NoteItemViewModel? matchedNote)
    {
        matchedNote = null;
        var activeTb = GetActiveEditorTextBox();
        var text = activeTb.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text))
            return false;

        var caret = Math.Clamp(activeTb.SelectionStart, 0, text.Length);
        var lineStart = FindLineStart(text, caret);
        var lineEnd = FindLineEnd(text, caret);
        var line = text[lineStart..lineEnd];
        var caretInLine = caret - lineStart;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '@')
                continue;

            if (i + 1 < line.Length && line[i + 1] == '[')
            {
                var closeBracket = line.IndexOf(']', i + 2);
                if (closeBracket > i + 2)
                {
                    var spanEnd = closeBracket + 1;
                    if (caretInLine >= i && caretInLine <= spanEnd)
                    {
                        var bracketTitle = line[(i + 2)..closeBracket];
                        matchedNote = ViewModel.FindNoteByMention(bracketTitle);
                        if (matchedNote is not null)
                            return true;
                    }
                }
            }

            foreach (var note in ViewModel.AllNoteItems.OrderByDescending(n => n.Title.Length))
            {
                if (string.IsNullOrWhiteSpace(note.Title))
                    continue;

                if (i + 1 + note.Title.Length <= line.Length &&
                    line.AsSpan(i + 1, note.Title.Length).Equals(note.Title.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    var spanEnd = i + 1 + note.Title.Length;
                    if (caretInLine >= i && caretInLine <= spanEnd)
                    {
                        matchedNote = note;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void OnActiveMentionJumpClick(object sender, RoutedEventArgs e)
    {
        if (ActiveMentionJumpButton.Tag is NoteItemViewModel note)
            NavigateToNote(note);
    }

    private void OnMentionClicked(string mentionTitle)
    {
        var note = ViewModel.FindNoteByMention(mentionTitle);
        if (note is not null)
            NavigateToNote(note);
    }

    private void NavigateToNote(NoteItemViewModel note)
    {
        if (_liveSyncTimer.IsEnabled)
            FlushSyncLiveBlocksToViewModel();

        HideMentionPopup();
        HideSlashMenuPopup();
        ViewModel.SelectedNote = note;
        ViewModel.IsSettingsOpen = false;
        FocusActiveEditorPane();
    }

    private static string ExtractCurrentWord(string text, int caret)
    {
        if (string.IsNullOrEmpty(text) || caret <= 0)
            return string.Empty;

        var end = Math.Min(caret, text.Length);
        var start = end - 1;
        while (start >= 0 && !char.IsWhiteSpace(text[start]) && text[start] is not ('│' or '├' or '└' or '─'))
            start--;

        var word = text[(start + 1)..end].Trim();
        return word.Length > 22 ? word[^22..] : word;
    }

    private void TriggerWordTypingAnimation(string activeWord)
    {
        if (string.IsNullOrWhiteSpace(activeWord))
            return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        TypingWordText.Text = activeWord;
        _typingWordStoryboard?.Stop();

        var wordSb = new Storyboard();

        var fade = new DoubleAnimationUsingKeyFrames
        {
            FillBehavior = FillBehavior.Stop
        };
        fade.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)),
            Value = 0.4
        });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)),
            Value = 0.96,
            EasingFunction = ease
        });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)),
            Value = 0.96
        });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)),
            Value = 0.0,
            EasingFunction = ease
        });
        Storyboard.SetTarget(fade, TypingWordPopupBorder);
        Storyboard.SetTargetProperty(fade, "Opacity");

        wordSb.Children.Add(fade);
        wordSb.Completed += (_, _) => TypingWordPopupBorder.Opacity = 0.0;

        _typingWordStoryboard = wordSb;
        wordSb.Begin();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.SelectedNote):
                _liveSyncTimer.Stop();
                HideMentionPopup();
                HideSlashMenuPopup();
                SelectionToolbarBorder.Visibility = Visibility.Collapsed;
                if (ViewModel.CurrentViewMode == EditorViewMode.Live &&
                    (!string.Equals(_lastRenderedLiveMarkdown, ViewModel.EditorText, StringComparison.Ordinal) ||
                     LiveEditorPanel.Children.Count == 0))
                {
                    RebuildLiveEditor(reparse: true);
                }
                UpdateActiveMentionJumpPill();
                if (ViewModel.IsFluidAnimationEnabled)
                {
                    PlayFluidTransition(ViewModel.CurrentViewMode == EditorViewMode.Live
                        ? LiveEditorScrollViewer
                        : EditorTextBox);
                }
                break;

            case nameof(MainViewModel.EditorText):
                if (!_isSyncingLiveEditor &&
                    ViewModel.CurrentViewMode == EditorViewMode.Live &&
                    !string.Equals(_lastRenderedLiveMarkdown, ViewModel.EditorText, StringComparison.Ordinal))
                {
                    RebuildLiveEditor(reparse: true);
                }
                break;

            case nameof(MainViewModel.CurrentViewMode):
                if (_liveSyncTimer.IsEnabled)
                    FlushSyncLiveBlocksToViewModel();
                HideMentionPopup();
                HideSlashMenuPopup();
                SelectionToolbarBorder.Visibility = Visibility.Collapsed;
                if (ViewModel.CurrentViewMode == EditorViewMode.Live)
                    RebuildLiveEditor(reparse: true, focusBlockIndex: 0);
                else if (EditorTextBox.Text != ViewModel.EditorText)
                    EditorTextBox.Text = ViewModel.EditorText;
                AnimateViewModeChange();
                break;

            case nameof(MainViewModel.IsSidebarOpen):
                AnimateSidebar(ViewModel.IsSidebarOpen);
                break;

            case nameof(MainViewModel.IsSettingsOpen):
                if (_liveSyncTimer.IsEnabled)
                    FlushSyncLiveBlocksToViewModel();
                AnimateSettingsModal(ViewModel.IsSettingsOpen);
                break;

            case nameof(MainViewModel.CurrentThemeMode):
                AnimateThemeModePill();
                break;

            case nameof(MainViewModel.CurrentBackdropMode):
                ApplyWindowBackdrop();
                AnimateBackdropModePill();
                break;

            case nameof(MainViewModel.IsDarkMode):
            case nameof(MainViewModel.EditorFontSize):
                RebuildLiveEditor(reparse: false);
                break;
        }
    }

    private void AnimateSidebar(bool open)
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var targetWidth = open ? TargetSidebarWidth : 0.0;
        var targetOpacity = open ? 1.0 : 0.0;
        var targetIconX = open ? 0.0 : -2.0;

        var widthAnim = new DoubleAnimation
        {
            From = SidebarContainer.ActualWidth,
            To = targetWidth,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = ease,
            EnableDependentAnimation = true,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(widthAnim, SidebarContainer);
        Storyboard.SetTargetProperty(widthAnim, "Width");

        var fadeAnim = new DoubleAnimation
        {
            From = open ? 0.0 : 1.0,
            To = targetOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(170)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(fadeAnim, SidebarContainer);
        Storyboard.SetTargetProperty(fadeAnim, "Opacity");

        var iconAnim = new DoubleAnimation
        {
            To = targetIconX,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(iconAnim, SidebarIconTranslate);
        Storyboard.SetTargetProperty(iconAnim, "X");

        SidebarContainer.Width = targetWidth;
        SidebarContainer.Opacity = targetOpacity;
        SidebarIconTranslate.X = targetIconX;

        sb.Children.Add(widthAnim);
        sb.Children.Add(fadeAnim);
        sb.Children.Add(iconAnim);
        sb.Begin();
    }

    private void AnimateViewModeChange()
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var targetX = ViewModel.ModeIndicatorOffsetX;

        var pillSlide = new DoubleAnimation
        {
            From = ModeIndicatorTranslate.X,
            To = targetX,
            Duration = new Duration(TimeSpan.FromMilliseconds(190)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(pillSlide, ModeIndicatorTranslate);
        Storyboard.SetTargetProperty(pillSlide, "X");
        ModeIndicatorTranslate.X = targetX;

        sb.Children.Add(pillSlide);
        sb.Begin();

        if (ViewModel.LivePaneVisibility == Visibility.Visible)
            PlayFluidTransition(LiveEditorScrollViewer);
        if (ViewModel.SourcePaneVisibility == Visibility.Visible)
            PlayFluidTransition(EditorTextBox);
    }

    private void AnimateThemeModePill()
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var targetX = ViewModel.ThemeIndicatorOffsetX;

        var pillSlide = new DoubleAnimation
        {
            From = ThemeIndicatorTranslate.X,
            To = targetX,
            Duration = new Duration(TimeSpan.FromMilliseconds(190)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(pillSlide, ThemeIndicatorTranslate);
        Storyboard.SetTargetProperty(pillSlide, "X");
        ThemeIndicatorTranslate.X = targetX;

        sb.Children.Add(pillSlide);
        sb.Begin();
    }

    private void AnimateBackdropModePill()
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var targetX = ViewModel.BackdropIndicatorOffsetX;

        var pillSlide = new DoubleAnimation
        {
            From = BackdropIndicatorTranslate.X,
            To = targetX,
            Duration = new Duration(TimeSpan.FromMilliseconds(190)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(pillSlide, BackdropIndicatorTranslate);
        Storyboard.SetTargetProperty(pillSlide, "X");
        BackdropIndicatorTranslate.X = targetX;

        sb.Children.Add(pillSlide);
        sb.Begin();
    }

    private void AnimateSettingsModal(bool open)
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var targetOpacity = open ? 1.0 : 0.0;
        var targetAngle = open ? 180.0 : 0.0;

        if (open)
        {
            ThemeIndicatorTranslate.X = ViewModel.ThemeIndicatorOffsetX;
            BackdropIndicatorTranslate.X = ViewModel.BackdropIndicatorOffsetX;
            MainWorkspaceGrid.Visibility = Visibility.Collapsed;
            SettingsOverlayGrid.Visibility = Visibility.Visible;
        }
        else
        {
            MainWorkspaceGrid.Visibility = Visibility.Visible;
        }

        var gearSpin = new DoubleAnimation
        {
            From = SettingsGearRotate.Angle,
            To = targetAngle,
            Duration = new Duration(TimeSpan.FromMilliseconds(240)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(gearSpin, SettingsGearRotate);
        Storyboard.SetTargetProperty(gearSpin, "Angle");

        var backdropFade = new DoubleAnimation
        {
            From = open ? 0.0 : 1.0,
            To = targetOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(open ? 180 : 140)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(backdropFade, SettingsOverlayGrid);
        Storyboard.SetTargetProperty(backdropFade, "Opacity");

        SettingsGearRotate.Angle = targetAngle;
        SettingsOverlayGrid.Opacity = targetOpacity;

        sb.Children.Add(gearSpin);
        sb.Children.Add(backdropFade);

        if (!open)
        {
            sb.Completed += (_, _) =>
            {
                if (!ViewModel.IsSettingsOpen)
                    SettingsOverlayGrid.Visibility = Visibility.Collapsed;
            };
        }

        sb.Begin();
    }

    private void OnOpacitySliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (e.NewValue >= 0.0)
            ViewModel.SurfaceOpacityPercent = e.NewValue;
    }

    private void ToggleCommandPalette() =>
        ShowCommandPalette(!_isCommandPaletteOpen);

    private void ShowCommandPalette(bool open)
    {
        _isCommandPaletteOpen = open;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb = new Storyboard();

        if (open)
        {
            if (DeleteFolderConfirmOverlay.Visibility == Visibility.Visible)
                ShowDeleteFolderOverlay(open: false);

            ViewModel.CommandSearchQuery = string.Empty;
            ViewModel.RefreshCommandPaletteItems();
            CommandPaletteOverlay.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(160)),
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };
            Storyboard.SetTarget(fadeIn, CommandPaletteOverlay);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");

            var scaleX = new DoubleAnimation
            {
                From = 0.95,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };
            Storyboard.SetTarget(scaleX, CommandPaletteScale);
            Storyboard.SetTargetProperty(scaleX, "ScaleX");

            var scaleY = new DoubleAnimation
            {
                From = 0.95,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };
            Storyboard.SetTarget(scaleY, CommandPaletteScale);
            Storyboard.SetTargetProperty(scaleY, "ScaleY");

            CommandPaletteOverlay.Opacity = 1.0;
            CommandPaletteScale.ScaleX = 1.0;
            CommandPaletteScale.ScaleY = 1.0;

            sb.Children.Add(fadeIn);
            sb.Children.Add(scaleX);
            sb.Children.Add(scaleY);
            sb.Begin();

            CommandPaletteSearchBox.Focus(FocusState.Programmatic);
        }
        else
        {
            var fadeOut = new DoubleAnimation
            {
                From = CommandPaletteOverlay.Opacity,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(120)),
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };
            Storyboard.SetTarget(fadeOut, CommandPaletteOverlay);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");

            CommandPaletteOverlay.Opacity = 0.0;
            sb.Children.Add(fadeOut);
            sb.Completed += (_, _) =>
            {
                if (!_isCommandPaletteOpen)
                {
                    CommandPaletteOverlay.Visibility = Visibility.Collapsed;
                    if (!ViewModel.IsSettingsOpen)
                        FocusActiveEditorPane();
                }
            };
            sb.Begin();
        }
    }

    private void OnCommandPaletteBackdropPressed(object sender, PointerRoutedEventArgs e) =>
        ShowCommandPalette(open: false);

    private void OnCommandPaletteCardPointerPressed(object sender, PointerRoutedEventArgs e) =>
        e.Handled = true;

    private void OnCommandPaletteSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                e.Handled = true;
                ViewModel.MoveCommandSelection(1);
                if (CommandPaletteListView.SelectedItem is not null)
                    CommandPaletteListView.ScrollIntoView(CommandPaletteListView.SelectedItem);
                break;

            case VirtualKey.Up:
                e.Handled = true;
                ViewModel.MoveCommandSelection(-1);
                if (CommandPaletteListView.SelectedItem is not null)
                    CommandPaletteListView.ScrollIntoView(CommandPaletteListView.SelectedItem);
                break;

            case VirtualKey.Enter:
                e.Handled = true;
                if (CommandPaletteListView.SelectedItem is CommandPaletteItem selected)
                    ExecuteCommandPaletteItem(selected);
                else if (ViewModel.CommandPaletteItems.Count > 0)
                    ExecuteCommandPaletteItem(ViewModel.CommandPaletteItems[0]);
                break;

            case VirtualKey.Escape:
                e.Handled = true;
                ShowCommandPalette(open: false);
                break;
        }
    }

    private void OnCommandPaletteItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CommandPaletteItem item)
            ExecuteCommandPaletteItem(item);
    }

    private void ExecuteCommandPaletteItem(CommandPaletteItem item)
    {
        ShowCommandPalette(open: false);
        if (_liveSyncTimer.IsEnabled)
            FlushSyncLiveBlocksToViewModel();

        if (item.TargetNote is not null)
        {
            ViewModel.SelectedNote = item.TargetNote;
            ViewModel.IsSettingsOpen = false;
            FocusActiveEditorPane();
            return;
        }
        item.ExecuteAction?.Invoke();
    }

    private static void PlayFluidTransition(UIElement element)
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation
        {
            From = 0.78,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(95)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(fade, "Opacity");
        element.Opacity = 1.0;

        sb.Children.Add(fade);
        sb.Begin();
    }

    private void OnNewFolderTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            ViewModel.ConfirmNewFolderCommand.Execute(null);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            ViewModel.CancelNewFolderCommand.Execute(null);
        }
    }

    private void OnSidebarItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not NoteItemViewModel item)
            return;

        if (item.IsFolder)
        {
            ViewModel.ToggleFolderExpansion(item.FolderPath);
        }
        else
        {
            if (_liveSyncTimer.IsEnabled)
                FlushSyncLiveBlocksToViewModel();
            ViewModel.SelectedNote = item;
        }
    }

    private void OnSidebarRowPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid { Children.Count: > 0 } grid)
        {
            if (grid.Children[0] is Border hoverBorder)
            {
                var isAlreadySelected = grid.DataContext is NoteItemViewModel { IsFolder: false, IsSelected: true };
                hoverBorder.Opacity = isAlreadySelected ? 0.0 : 1.0;
            }

            if (grid.FindName("FolderColorBtn") is Button folderColorBtn)
                folderColorBtn.Opacity = 1.0;
            if (grid.FindName("NoteColorBtn") is Button noteColorBtn)
                noteColorBtn.Opacity = 1.0;
        }
    }

    private void OnSidebarRowPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid { Children.Count: > 0 } grid)
        {
            if (grid.Children[0] is Border hoverBorder)
                hoverBorder.Opacity = 0.0;

            var restingOpacity = (grid.DataContext as NoteItemViewModel)?.ColorDotRestingOpacity ?? 0.0;
            if (grid.FindName("FolderColorBtn") is Button folderColorBtn)
                folderColorBtn.Opacity = restingOpacity;
            if (grid.FindName("NoteColorBtn") is Button noteColorBtn)
                noteColorBtn.Opacity = restingOpacity;
        }
    }

    private void OnCreateNoteInFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folderPath })
            _ = ViewModel.CreateNoteInFolderAsync(folderPath);
    }

    private void OnCreateSubfolderInFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folderPath } && !string.IsNullOrWhiteSpace(folderPath))
        {
            ViewModel.StartNewSubfolder(folderPath);
            NewFolderTextBox.Focus(FocusState.Programmatic);
        }
    }

    private void OnItemColorDotClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NoteItemViewModel item } anchorButton)
            return;

        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Padding = new Thickness(4, 2, 4, 2)
        };

        foreach (var (name, hex) in PaletteColors)
        {
            var color = NoteItemViewModel.ParseHexColor(hex, Colors.DodgerBlue);
            var isCurrent = string.Equals(item.ColorHex, hex, StringComparison.OrdinalIgnoreCase);

            var swatchBtn = new Button
            {
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(isCurrent ? 2 : 0),
                BorderBrush = new SolidColorBrush(Colors.White)
            };
            ToolTipService.SetToolTip(swatchBtn, name);

            var capturedHex = hex;
            swatchBtn.Click += (_, _) =>
            {
                ViewModel.SetItemColor(item, capturedHex);
                flyout.Hide();
            };

            row.Children.Add(swatchBtn);
        }

        if (item.HasCustomColor)
        {
            var clearBtn = new Button
            {
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 142, 142, 147)),
                BorderThickness = new Thickness(0),
                Content = new FontIcon
                {
                    Glyph = "\uE711",
                    FontSize = 9
                }
            };
            ToolTipService.SetToolTip(clearBtn, "Reset color");
            clearBtn.Click += (_, _) =>
            {
                ViewModel.SetItemColor(item, null);
                flyout.Hide();
            };
            row.Children.Add(clearBtn);
        }

        flyout.Content = row;
        flyout.ShowAt(anchorButton);
    }

    private void OnDeleteFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string folderPath } || string.IsNullOrWhiteSpace(folderPath))
            return;

        _pendingDeleteFolderPath = folderPath;
        DeleteFolderConfirmTitle.Text = $"Delete \"{folderPath}\"?";
        ShowDeleteFolderOverlay(open: true);
    }

    private void OnCancelDeleteFolderClick(object sender, RoutedEventArgs e)
    {
        _pendingDeleteFolderPath = null;
        ShowDeleteFolderOverlay(open: false);
    }

    private async void OnConfirmDeleteFolderClick(object sender, RoutedEventArgs e)
    {
        var folderPath = _pendingDeleteFolderPath;
        _pendingDeleteFolderPath = null;
        ShowDeleteFolderOverlay(open: false);

        if (!string.IsNullOrWhiteSpace(folderPath))
            await ViewModel.DeleteFolderAsync(folderPath);
    }

    private void ShowDeleteFolderOverlay(bool open)
    {
        if (open)
            DeleteFolderConfirmOverlay.Visibility = Visibility.Visible;

        var targetOpacity = open ? 1.0 : 0.0;
        var sb = new Storyboard();
        var fade = new DoubleAnimation
        {
            From = open ? 0.0 : 1.0,
            To = targetOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(open ? 160 : 120)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(fade, DeleteFolderConfirmOverlay);
        Storyboard.SetTargetProperty(fade, "Opacity");
        DeleteFolderConfirmOverlay.Opacity = targetOpacity;
        sb.Children.Add(fade);

        if (!open)
        {
            sb.Completed += (_, _) =>
            {
                if (_pendingDeleteFolderPath is null)
                    DeleteFolderConfirmOverlay.Visibility = Visibility.Collapsed;
            };
        }

        sb.Begin();
    }

    private void OnTogglePinNoteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NoteItemViewModel item })
            ViewModel.TogglePinNote(item);
    }

    private void OnDeleteNoteItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NoteItemViewModel item })
            _ = ViewModel.DeleteNoteAsync(item);
    }

    private void OnPickUserAvatarClick(object sender, RoutedEventArgs e) =>
        _ = PickUserAvatarAsync();

    private void OnImportMarkdownClick(object sender, RoutedEventArgs e) =>
        _ = PickAndImportMarkdownAsync();

    private void OnExportMarkdownClick(object sender, RoutedEventArgs e) =>
        _ = PickAndExportMarkdownAsync();

    private void OnChangeVaultFolderClick(object sender, RoutedEventArgs e) =>
        _ = PickVaultFolderAsync();

    private async Task PickUserAvatarAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".bmp");

            var file = await picker.PickSingleFileAsync();
            if (file is not null)
                await ViewModel.SetUserAvatarAsync(file.Path);
        }
        catch
        {
        }
    }

    private async Task PickAndImportMarkdownAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".txt");

            var file = await picker.PickSingleFileAsync();
            if (file is not null)
                await ViewModel.ImportMarkdownFileAsync(file.Path);
        }
        catch
        {
        }
    }

    private async Task PickAndExportMarkdownAsync()
    {
        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("Markdown Document", new List<string> { ".md" });
            picker.SuggestedFileName = ViewModel.SelectedNote?.Title ?? "Note";

            var file = await picker.PickSaveFileAsync();
            if (file is not null)
                await ViewModel.ExportCurrentNoteAsync(file.Path);
        }
        catch
        {
        }
    }

    private async Task PickVaultFolderAsync()
    {
        try
        {
            var picker = new FolderPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
                await ViewModel.ChangeVaultDirectoryAsync(folder.Path);
        }
        catch
        {
        }
    }

    private void OnRootGridDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Import .md into Noti";
        }
    }

    private async void OnRootGridDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                if (item is StorageFile file &&
                    (file.FileType.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                     file.FileType.Equals(".txt", StringComparison.OrdinalIgnoreCase)))
                {
                    await ViewModel.ImportMarkdownFileAsync(file.Path);
                }
            }
        }
        catch
        {
        }
    }

    private static int FindLineStart(string text, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (text[i] is '\r' or '\n')
                return i + 1;
        }
        return 0;
    }

    private static int FindLineEnd(string text, int index)
    {
        for (var i = index; i < text.Length; i++)
        {
            if (text[i] is '\r' or '\n')
                return i;
        }
        return text.Length;
    }

    private void UpdateTitleBarPassthrough()
    {
        if (RootGrid.XamlRoot is null)
            return;

        try
        {
            var nonClientSource = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            var scale = RootGrid.XamlRoot.RasterizationScale;
            var regions = new List<RectInt32>();

            void AddElementRect(FrameworkElement el)
            {
                if (el.ActualWidth <= 0 || el.ActualHeight <= 0)
                    return;

                var transform = el.TransformToVisual(RootGrid);
                var bounds = transform.TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
                regions.Add(new RectInt32(
                    (int)Math.Round(bounds.X * scale),
                    (int)Math.Round(bounds.Y * scale),
                    (int)Math.Round(bounds.Width * scale),
                    (int)Math.Round(bounds.Height * scale)));
            }

            AddElementRect(SidebarToggleButton);
            AddElementRect(ViewSwitcherBorder);
            AddElementRect(UpdateTopBarButton);
            AddElementRect(ZoomBadgeButton);

            nonClientSource.SetRegionRects(NonClientRegionKind.Passthrough, regions.ToArray());
        }
        catch
        {
            // Ignore passthrough registration errors during initial window teardown/creation
        }
    }
}
