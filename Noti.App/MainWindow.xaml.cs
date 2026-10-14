using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Noti.App.Services;
using Noti.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
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
    private Storyboard? _smoothCaretStoryboard;
    private bool _hasSmoothCaretPosition;
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
    private TextAlignment _activeEditorAlignment = TextAlignment.Left;
    private ulong _lastCanvasClickTimestamp;
    private readonly BackdropService _backdropService;
    private readonly WindowChromeService _chromeService;
    private Storyboard? _sidebarContextStoryboard;

    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush AccentBrush = new(ColorHelper.FromArgb(255, 10, 132, 255));
    private static readonly SolidColorBrush SelectionUnfocusedBrush = new(ColorHelper.FromArgb(140, 10, 132, 255));
    public static readonly FontFamily BodyFontFamily = new("Segoe UI Variable Text, Segoe UI, -apple-system, SF Pro Text, Inter, sans-serif");
    public static readonly FontFamily HeadingFontFamily = new("Segoe UI Variable Display, Segoe UI, -apple-system, SF Pro Display, Inter, sans-serif");
    public static readonly FontFamily MonoFontFamily = new("Cascadia Mono, Consolas, Courier New, monospace");
    private static readonly FontFamily DefaultFontFamily = BodyFontFamily;

    private bool? _cachedIsDarkMode;
    private SolidColorBrush _cachedPrimaryBrush = new(ColorHelper.FromArgb(255, 245, 245, 247));
    private SolidColorBrush _cachedSecondaryBrush = new(ColorHelper.FromArgb(255, 142, 142, 147));
    private SolidColorBrush _cachedSurfaceBrush = new(ColorHelper.FromArgb(36, 255, 255, 255));
    private SolidColorBrush _cachedBorderBrush = new(ColorHelper.FromArgb(38, 255, 255, 255));
    private SolidColorBrush _cachedInputBgBrush = TransparentBrush;
    private Style? _cachedBorderlessStyle;
    private List<string> _cachedSortedNoteTitles = [];
    private bool _isContextSlashMenuOpen;
    private long _lastSlashPopupOpenTick;
    private Storyboard? _slashPopupStoryboard;
    private Storyboard? _selectionToolbarStoryboard;

    private bool _isCleanedUp;

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        ViewModel.RequestExportMarkdown = () => _ = PickAndExportMarkdownAsync();
        ViewModel.RequestPrepareForUpdateExit = CleanupWindowResources;

        _backdropService = new BackdropService(this);
        _chromeService   = new WindowChromeService(this);

        InitializeComponent();
        _chromeService.Configure();
        _backdropService.Apply(ViewModel.CurrentBackdropMode, ViewModel.IsDarkMode, ViewModel.SurfaceOpacityPercent);
        Closed += OnWindowClosed;

        _liveSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _liveSyncTimer.Tick += (_, _) => FlushSyncLiveBlocksToViewModel();

        RootGrid.Loaded += (_, _) =>
        {
            _backdropService.EnsurePostActivationBinding(
                ViewModel.CurrentBackdropMode,
                ViewModel.IsDarkMode,
                ViewModel.SurfaceOpacityPercent);
            NoteItemViewModel.IsLightTheme = !ViewModel.IsDarkMode;
            foreach (var n in ViewModel.FilteredNotes)
                n.NotifyVisualStateChanged();
            ApplyResponsiveLayout();
            UpdateTitleBarChromeRegions();
            LiveCanvasRootGrid.MinHeight = Math.Max(0, LiveEditorScrollViewer.ActualHeight);
            ThemeIndicatorTranslate.X = ViewModel.ThemeIndicatorOffsetX;
            BackdropIndicatorTranslate.X = ViewModel.BackdropIndicatorOffsetX;
            SettingsTabIndicatorTranslate.X = GetSettingsTabIndicatorTargetX();
            ModeIndicatorTranslate.X = ViewModel.ModeIndicatorOffsetX;
            ApplyEditorTextBoxTheme();
            RebuildLiveEditor(reparse: true);
        };
        RootGrid.SizeChanged += (_, _) =>
        {
            ApplyResponsiveLayout();
            UpdateTitleBarChromeRegions();
        };
        TopTitleBarGrid.LayoutUpdated += OnTopTitleBarLayoutUpdated;
        LiveEditorScrollViewer.SizeChanged += (_, _) =>
        {
            LiveCanvasRootGrid.MinHeight = Math.Max(0, LiveEditorScrollViewer.ActualHeight);
        };

        LiveEditorScrollViewer.ViewChanged += OnLiveEditorScrollViewerViewChanged;
        LiveEditorScrollViewer.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnLiveCanvasBackgroundPointerPressed), true);
        EditorPaneGrid.AddHandler(UIElement.ContextRequestedEvent, new TypedEventHandler<UIElement, ContextRequestedEventArgs>(OnEditorContextRequested), true);
        EditorPaneGrid.AddHandler(UIElement.RightTappedEvent, new RightTappedEventHandler(OnEditorPaneRightTapped), true);
        EditorTextBox.ContextRequested += OnEditorContextRequested;
        EditorTextBox.Paste += OnEditorTextBoxPaste;
        MainWorkspaceGrid.AddHandler(UIElement.DragOverEvent, new DragEventHandler(OnRootGridDragOver), true);
        MainWorkspaceGrid.AddHandler(UIElement.DropEvent, new DragEventHandler(OnRootGridDrop), true);
        RootGrid.PreviewKeyDown += OnRootPreviewKeyDown;
        EditorTextBox.PreviewKeyDown += OnEditorPreviewKeyDown;
        EditorTextBox.TextChanged += OnEditorTextChanged;
        EditorTextBox.GotFocus += (_, _) => ScheduleSmoothCaretUpdate(EditorTextBox);
        EditorTextBox.LostFocus += (_, _) =>
        {
            HideSmoothCaretOverlay();
            HideSelectionToolbar();
        };
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.ActiveNoteLockChanged = OnActiveNoteLockChanged;
        ViewModel.RequestShowLockDiscoveryTip = ShowLockDiscoveryCallout;
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => DismissLockDiscoveryCallout()), true);
    }

    private void OnTopTitleBarLayoutUpdated(object? sender, object e) =>
        UpdateTitleBarChromeRegions();

    private void CleanupWindowResources()
    {
        if (_isCleanedUp)
            return;

        _isCleanedUp = true;

        try
        {
            TopTitleBarGrid.LayoutUpdated -= OnTopTitleBarLayoutUpdated;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            if (_liveSyncTimer.IsEnabled)
                FlushSyncLiveBlocksToViewModel();
            _liveSyncTimer.Stop();
            if (ViewModel.SelectedNote?.Model is not null)
            {
                try
                {
                    Task.Run(async () => await ViewModel.ForceSaveAsync()).Wait(TimeSpan.FromSeconds(2));
                }
                catch
                {
                }
            }
            ViewModel.Shutdown();
        }
        catch
        {
        }

        _chromeService.Dispose();
        _backdropService.Dispose();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        CleanupWindowResources();
        try
        {
            Application.Current.Exit();
        }
        catch
        {
        }
    }

    public MainViewModel ViewModel { get; }

    private double TargetSidebarWidth => RootGrid.ActualWidth switch
    {
        > 0 and < 640 => 208.0,
        >= 640 and < 860 => 236.0,
        _ => 284.0
    };

    private void ApplyWindowBackdrop() =>
        _backdropService.Apply(ViewModel.CurrentBackdropMode, ViewModel.IsDarkMode, ViewModel.SurfaceOpacityPercent);

    private void UpdateAcrylicControllerProperties() =>
        _backdropService.UpdateOpacity(ViewModel.IsDarkMode, ViewModel.SurfaceOpacityPercent);

    private void ApplyResponsiveLayout()
    {
        var width = RootGrid.ActualWidth;
        if (width <= 0)
            return;

        var isCompact = width < 860;
        var isSmall   = width < 640;
        var horizPad  = isSmall ? 16.0 : (isCompact ? 24.0 : 44.0);
        var vertPad   = isCompact ? 18.0 : 28.0;

        ActiveFolderBadgeBorder.Visibility = isSmall ? Visibility.Collapsed : Visibility.Visible;
        BrandTitleText.Visibility = width < 560 ? Visibility.Collapsed : Visibility.Visible;

        EditorTextBox.Padding = new Thickness(horizPad, vertPad, horizPad, 16);
        LiveEditorPanel.Padding = new Thickness(horizPad, vertPad, horizPad, 80);
        StatusBarGrid.Padding = new Thickness(horizPad, 0, horizPad, 0);

        var settingsHorizPad = isSmall ? 16.0 : (isCompact ? 24.0 : 32.0);
        var settingsVertPad  = isCompact ? 16.0 : 20.0;
        SettingsScrollViewer.Padding = new Thickness(settingsHorizPad, settingsVertPad, settingsHorizPad, 32);

        if (ViewModel.IsSidebarOpen)
        {
            if (ViewModel.IsSettingsOpen && isSmall)
            {
                SidebarContainer.Width = 0;
                SidebarContainer.Opacity = 0;
            }
            else
            {
                SidebarContainer.Width = TargetSidebarWidth;
                SidebarContainer.Opacity = 1;
            }
        }
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
        DismissLockDiscoveryCallout();
        if (e.Key == VirtualKey.Escape)
        {
            if (SidebarContextOverlay.Visibility == Visibility.Visible)
            {
                HideSidebarContextPopup();
                e.Handled = true;
                return;
            }
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
                HideSelectionToolbar();
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

            case VirtualKey.L:
                e.Handled = true;
                ViewModel.ToggleActiveNoteLock();
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

        if (ctrlDown && e.Key == VirtualKey.L)
        {
            e.Handled = true;
            ViewModel.ToggleActiveNoteLock();
            return;
        }

        if (ViewModel.IsActiveNoteLocked)
        {
            if (ctrlDown && (e.Key is VirtualKey.C or VirtualKey.A or VirtualKey.L))
            {
                // allow copy, select all, lock toggle
            }
            else if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or
                     VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown)
            {
                // allow caret navigation
            }
            else
            {
                e.Handled = true;
                TriggerLockedNoteAccidentalEditFeedback();
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
                    ViewModel.ToggleActiveNoteLock();
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

        var text = tb.Text ?? string.Empty;
        var selStart = Math.Clamp(tb.SelectionStart, 0, text.Length);
        var selLen = Math.Clamp(tb.SelectionLength, 0, text.Length - selStart);

        if (selLen > 0 && selLen < text.Length)
        {
            var existing = block.InlineSpans.FirstOrDefault(s => s.Start == selStart && s.Length == selLen);
            if (existing is not null)
            {
                if (toggleBlockBold) existing.IsBold = !existing.IsBold;
                if (toggleBlockItalic) existing.IsItalic = !existing.IsItalic;
                if (toggleBlockStrikethrough) existing.IsStrikethrough = !existing.IsStrikethrough;
                if (toggleBlockCode) existing.IsCode = !existing.IsCode;
                if (!existing.IsBold && !existing.IsItalic && !existing.IsStrikethrough && !existing.IsCode)
                    block.InlineSpans.Remove(existing);
            }
            else
            {
                block.InlineSpans.Add(new LiveInlineSpan
                {
                    Start = selStart,
                    Length = selLen,
                    IsBold = toggleBlockBold,
                    IsItalic = toggleBlockItalic,
                    IsStrikethrough = toggleBlockStrikethrough,
                    IsCode = toggleBlockCode
                });
            }
        }
        else
        {
            if (toggleBlockBold)
                block.IsBlockBold = !block.IsBlockBold;
            else if (toggleBlockItalic)
                block.IsBlockItalic = !block.IsBlockItalic;
            else if (toggleBlockStrikethrough)
                block.IsBlockStrikethrough = !block.IsBlockStrikethrough;
            else if (toggleBlockCode)
                block.IsBlockCode = !block.IsBlockCode;
        }

        if (toggleBlockBold)
            tb.FontWeight = (block.IsBlockBold || block.InlineSpans.Any(s => s.IsBold)) ? FontWeights.Bold : FontWeights.Normal;
        if (toggleBlockItalic)
            tb.FontStyle = (block.IsBlockItalic || block.InlineSpans.Any(s => s.IsItalic)) ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
        if (toggleBlockCode)
            tb.FontFamily = block.IsBlockCode ? MonoFontFamily : BodyFontFamily;

        FlushSyncLiveBlocksToViewModel();

        if (toggleBlockStrikethrough)
        {
            RefreshLiveBlockAt(idx, selStart);
            if (block.PrimaryTextBox is { } newTb)
            {
                newTb.SelectionStart = selStart;
                newTb.SelectionLength = selLen;
                newTb.Focus(FocusState.Programmatic);
            }
        }
        else
        {
            tb.Focus(FocusState.Programmatic);
            tb.SelectionStart = selStart;
            tb.SelectionLength = selLen;
            UpdateSelectionToolbar();
        }
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

        _isContextSlashMenuOpen = false;
        UpdateSelectionToolbar();
        UpdateSlashMenuAutocomplete();
        UpdateMentionAutocomplete();
        UpdateActiveMentionJumpPill();
        ScheduleSmoothCaretUpdate(EditorTextBox);

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
        ScheduleSmoothCaretUpdate(EditorTextBox);
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
        if (activeTb is null ||
            activeTb.SelectionLength <= 0 ||
            activeTb.Visibility != Visibility.Visible ||
            activeTb.FocusState == FocusState.Unfocused ||
            EditorPaneGrid.ActualWidth <= 0 ||
            EditorPaneGrid.ActualHeight <= 0)
        {
            HideSelectionToolbar();
            return;
        }

        var text = activeTb.Text ?? string.Empty;
        var selStart = Math.Clamp(activeTb.SelectionStart, 0, text.Length);
        var selLen = Math.Clamp(activeTb.SelectionLength, 0, text.Length - selStart);
        if (selLen <= 0)
        {
            HideSelectionToolbar();
            return;
        }

        var selEnd = selStart + selLen;

        try
        {
            var startRect = activeTb.GetRectFromCharacterIndex(selStart, trailingEdge: false);
            var endRect = activeTb.GetRectFromCharacterIndex(Math.Max(selStart, selEnd - 1), trailingEdge: true);

            if (double.IsNaN(startRect.X) || double.IsNaN(startRect.Y) || double.IsNaN(endRect.X) || double.IsNaN(endRect.Y))
            {
                HideSelectionToolbar();
                return;
            }

            var selMinX = Math.Min(startRect.X, endRect.X);
            var selMaxX = Math.Max(startRect.Right, endRect.Right);
            var selMinY = Math.Min(startRect.Y, endRect.Y);
            var selMaxY = Math.Max(startRect.Bottom, endRect.Bottom);

            if (activeTb.Padding.Left > 0 && selMinX < activeTb.Padding.Left - 1.0)
            {
                selMinX += activeTb.Padding.Left;
                selMaxX += activeTb.Padding.Left;
                selMinY += activeTb.Padding.Top;
                selMaxY += activeTb.Padding.Top;
            }

            var transform = activeTb.TransformToVisual(EditorPaneGrid);
            var topLeft = transform.TransformPoint(new Point(selMinX, selMinY));
            var bottomRight = transform.TransformPoint(new Point(selMaxX, selMaxY));

            var selPaneLeft = topLeft.X;
            var selPaneRight = bottomRight.X;
            var selPaneTop = topLeft.Y;
            var selPaneBottom = bottomRight.Y;

            var selCenterX = (selPaneLeft + selPaneRight) * 0.5;

            var toolbarWidth = SelectionToolbarBorder.ActualWidth > 50 ? SelectionToolbarBorder.ActualWidth : 340.0;
            var toolbarHeight = SelectionToolbarBorder.ActualHeight > 20 ? SelectionToolbarBorder.ActualHeight : 36.0;

            var targetX = selCenterX - (toolbarWidth * 0.5);
            var maxTargetX = Math.Max(16.0, EditorPaneGrid.ActualWidth - toolbarWidth - 16.0);
            targetX = Math.Clamp(targetX, 16.0, maxTargetX);

            var targetY = selPaneTop - toolbarHeight - 8.0;
            if (targetY < 8.0)
            {
                targetY = selPaneBottom + 8.0;
            }

            var maxTargetY = Math.Max(8.0, EditorPaneGrid.ActualHeight - toolbarHeight - 8.0);
            targetY = Math.Clamp(targetY, 8.0, maxTargetY);

            ShowSelectionToolbar(targetX, targetY);
        }
        catch
        {
            HideSelectionToolbar();
        }
    }

    private void ShowSelectionToolbar(double targetX, double targetY)
    {
        _selectionToolbarStoryboard?.Stop();

        var isAlreadyVisible = SelectionToolbarBorder.Visibility == Visibility.Visible && SelectionToolbarBorder.Opacity > 0.05;

        if (!isAlreadyVisible)
        {
            SelectionToolbarBorder.Visibility = Visibility.Visible;
            SelectionToolbarTranslate.X = targetX;
            SelectionToolbarTranslate.Y = targetY + 4.0;
            SelectionToolbarBorder.Opacity = 0.0;

            var sb = new Storyboard();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(140)),
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };
            Storyboard.SetTarget(fadeIn, SelectionToolbarBorder);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");

            var slideY = new DoubleAnimation
            {
                From = targetY + 4.0,
                To = targetY,
                Duration = new Duration(TimeSpan.FromMilliseconds(140)),
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };
            Storyboard.SetTarget(slideY, SelectionToolbarTranslate);
            Storyboard.SetTargetProperty(slideY, "Y");

            SelectionToolbarBorder.Opacity = 1.0;
            SelectionToolbarTranslate.Y = targetY;

            sb.Children.Add(fadeIn);
            sb.Children.Add(slideY);
            _selectionToolbarStoryboard = sb;
            sb.Begin();
        }
        else
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var sb = new Storyboard();

            var slideX = new DoubleAnimation
            {
                From = SelectionToolbarTranslate.X,
                To = targetX,
                Duration = new Duration(TimeSpan.FromMilliseconds(90)),
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };
            Storyboard.SetTarget(slideX, SelectionToolbarTranslate);
            Storyboard.SetTargetProperty(slideX, "X");

            var slideY = new DoubleAnimation
            {
                From = SelectionToolbarTranslate.Y,
                To = targetY,
                Duration = new Duration(TimeSpan.FromMilliseconds(90)),
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };
            Storyboard.SetTarget(slideY, SelectionToolbarTranslate);
            Storyboard.SetTargetProperty(slideY, "Y");

            SelectionToolbarTranslate.X = targetX;
            SelectionToolbarTranslate.Y = targetY;

            sb.Children.Add(slideX);
            sb.Children.Add(slideY);
            _selectionToolbarStoryboard = sb;
            sb.Begin();
        }
    }

    private void HideSelectionToolbar(bool immediate = false)
    {
        if (SelectionToolbarBorder.Visibility == Visibility.Collapsed)
            return;

        _selectionToolbarStoryboard?.Stop();

        if (immediate)
        {
            SelectionToolbarBorder.Opacity = 0.0;
            SelectionToolbarBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeOut = new DoubleAnimation
        {
            From = SelectionToolbarBorder.Opacity,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(120)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(fadeOut, SelectionToolbarBorder);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");

        sb.Children.Add(fadeOut);
        sb.Completed += (_, _) =>
        {
            SelectionToolbarBorder.Opacity = 0.0;
            SelectionToolbarBorder.Visibility = Visibility.Collapsed;
        };
        _selectionToolbarStoryboard = sb;
        sb.Begin();
    }

    private void OnSelectionBoldClick(object sender, RoutedEventArgs e) =>
        WrapEditorSelection("**", "**");

    private void OnSelectionItalicClick(object sender, RoutedEventArgs e) =>
        WrapEditorSelection("*", "*");

    private void OnSelectionStrikethroughClick(object sender, RoutedEventArgs e) =>
        WrapEditorSelection("~~", "~~");

    private void OnSelectionCodeClick(object sender, RoutedEventArgs e) =>
        WrapEditorSelection("`", "`");

    private void OnSelectionLinkClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentViewMode == EditorViewMode.Live && _activeLiveBlock is { PrimaryTextBox: { } liveTb } block)
        {
            var text = liveTb.Text ?? string.Empty;
            var start = Math.Clamp(liveTb.SelectionStart, 0, text.Length);
            var length = Math.Clamp(liveTb.SelectionLength, 0, text.Length - start);

            if (length == 0)
            {
                const string insertedText = "link";
                var updated = text.Insert(start, insertedText);
                block.Content = updated;
                liveTb.Text = updated;
                length = insertedText.Length;
            }

            const string defaultUrl = "https://";
            var existingSpan = block.InlineSpans.FirstOrDefault(s => s.Start <= start && s.Start + s.Length >= start + length);
            if (existingSpan is not null)
            {
                existingSpan.LinkUrl = defaultUrl;
            }
            else
            {
                block.InlineSpans.Add(new LiveInlineSpan
                {
                    Start = start,
                    Length = length,
                    LinkUrl = defaultUrl
                });
            }

            FlushSyncLiveBlocksToViewModel();
            liveTb.Focus(FocusState.Programmatic);
            liveTb.SelectionStart = start;
            liveTb.SelectionLength = length;
            UpdateSelectionToolbar();
            return;
        }

        var sourceTb = EditorTextBox;
        var srcText = sourceTb.Text ?? string.Empty;
        var srcStart = Math.Clamp(sourceTb.SelectionStart, 0, srcText.Length);
        var srcLength = Math.Clamp(sourceTb.SelectionLength, 0, srcText.Length - srcStart);
        var selected = srcLength > 0 ? srcText.Substring(srcStart, srcLength) : "link";
        const string fallbackUrl = "https://";
        var replacement = $"[{selected}]({fallbackUrl})";

        var updatedSrc = srcText[..srcStart] + replacement + srcText[(srcStart + srcLength)..];
        sourceTb.Text = updatedSrc;
        ViewModel.EditorText = updatedSrc;

        sourceTb.SelectionStart = srcStart + selected.Length + 3;
        sourceTb.SelectionLength = fallbackUrl.Length;
        sourceTb.Focus(FocusState.Programmatic);
    }

    private void OnAlignLeftClick(object sender, RoutedEventArgs e) => SetEditorBlockAlignment(TextAlignment.Left);

    private void OnAlignCenterClick(object sender, RoutedEventArgs e) => SetEditorBlockAlignment(TextAlignment.Center);

    private void OnAlignRightClick(object sender, RoutedEventArgs e) => SetEditorBlockAlignment(TextAlignment.Right);

    private void SetEditorBlockAlignment(TextAlignment alignment)
    {
        HideSlashMenuPopup();
        _activeEditorAlignment = alignment;

        if (ViewModel.CurrentViewMode == EditorViewMode.Live)
        {
            var block = _activeLiveBlock ?? _liveBlocks.FirstOrDefault();
            if (block is null)
                return;

            var idx = _liveBlocks.IndexOf(block);
            if (idx < 0)
                return;

            block.BlockAlignment = alignment;
            for (var i = idx + 1; i < _liveBlocks.Count; i++)
            {
                if (_liveBlocks[i].Kind == LiveBlockKind.Paragraph && string.IsNullOrEmpty(_liveBlocks[i].Content))
                {
                    _liveBlocks[i].BlockAlignment = alignment;
                    if (_liveBlocks[i].PrimaryTextBox is { } trailingTb)
                        trailingTb.TextAlignment = alignment;
                }
            }

            var caret = block.PrimaryTextBox?.SelectionStart ?? (block.Content?.Length ?? 0);
            FlushSyncLiveBlocksToViewModel();
            RefreshLiveBlockAt(idx, IsEditableLiveBlock(block) ? caret : null);
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var caretPos = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
        var lineStart = FindLineStart(text, caretPos);
        var lineEnd = FindLineEnd(text, caretPos);
        var line = text[lineStart..lineEnd].Trim();

        if (line.StartsWith("<div", StringComparison.OrdinalIgnoreCase) &&
            line.EndsWith("</div>", StringComparison.OrdinalIgnoreCase))
        {
            var firstClose = line.IndexOf('>');
            if (firstClose > 0 && firstClose < line.Length - 6)
                line = line[(firstClose + 1)..^6].Trim();
        }

        var replaced = alignment switch
        {
            TextAlignment.Center => $"<div align=\"center\">{line}</div>",
            TextAlignment.Right => $"<div align=\"right\">{line}</div>",
            _ => line
        };

        var updated = text[..lineStart] + replaced + text[lineEnd..];
        ApplyEditorText(updated, lineStart + replaced.Length);
        EditorTextBox.Focus(FocusState.Programmatic);
    }

    private void OnEditorContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;

        if (ViewModel.CurrentViewMode == EditorViewMode.Live)
        {
            if (sender is TextBox tb && tb.Tag is LiveDocBlock block)
            {
                _activeLiveBlock = block;
                if (tb.FocusState == FocusState.Unfocused)
                    tb.Focus(FocusState.Programmatic);
            }
            else if (e.TryGetPosition(LiveEditorPanel, out var livePos))
            {
                FocusNearestLiveBlockAtY(livePos.Y);
            }
        }
        else if (EditorTextBox.FocusState == FocusState.Unfocused)
        {
            EditorTextBox.Focus(FocusState.Programmatic);
        }

        if (!e.TryGetPosition(EditorPaneGrid, out var pos))
            pos = GetDefaultSlashPopupAnchor();

        OpenSlashMenuFromContext(pos);
    }

    private void OnEditorPaneRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;

        if (ViewModel.CurrentViewMode == EditorViewMode.Live)
        {
            var clickY = e.GetPosition(LiveEditorPanel).Y;
            FocusNearestLiveBlockAtY(clickY);
        }
        else if (EditorTextBox.FocusState == FocusState.Unfocused)
        {
            EditorTextBox.Focus(FocusState.Programmatic);
        }

        OpenSlashMenuFromContext(e.GetPosition(EditorPaneGrid));
    }

    private void OpenSlashMenuFromContext(Point anchorPos)
    {
        var now = Environment.TickCount64;
        if (SlashMenuPopupBorder.Visibility == Visibility.Visible && now - _lastSlashPopupOpenTick < 90)
            return;

        _lastSlashPopupOpenTick = now;
        _isContextSlashMenuOpen = true;
        _activeSlashIndex = -1;
        ViewModel.UpdateSlashSuggestions(string.Empty);
        if (ViewModel.SlashMenuSuggestions.Count > 0)
        {
            SlashMenuListView.SelectedIndex = 0;
            ShowSlashMenuPopup(anchorPos, forceAnimate: true);
        }
    }

    private Point GetDefaultSlashPopupAnchor()
    {
        if (ViewModel.CurrentViewMode == EditorViewMode.Live &&
            _activeLiveBlock?.RootElement is { } rootEl)
        {
            try
            {
                return rootEl.TransformToVisual(EditorPaneGrid).TransformPoint(new Point(0, rootEl.ActualHeight + 6));
            }
            catch
            {
            }
        }

        return new Point(44, 68);
    }

    private void ShowSlashMenuPopup(Point? anchorPos = null, bool forceAnimate = false)
    {
        var pos = anchorPos ?? GetDefaultSlashPopupAnchor();
        var maxW = Math.Max(320.0, EditorPaneGrid.ActualWidth);
        var maxH = Math.Max(360.0, EditorPaneGrid.ActualHeight);
        var clampedLeft = Math.Clamp(pos.X, 16.0, Math.Max(16.0, maxW - 304.0));
        var clampedTop = Math.Clamp(pos.Y, 12.0, Math.Max(12.0, maxH - 332.0));
        SlashMenuPopupBorder.Margin = new Thickness(clampedLeft, clampedTop, 0, 0);

        var wasCollapsed = SlashMenuPopupBorder.Visibility != Visibility.Visible;
        SlashMenuPopupBorder.Visibility = Visibility.Visible;

        if (!wasCollapsed && !forceAnimate)
            return;

        _slashPopupStoryboard?.Stop();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb = new Storyboard();

        var fade = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(fade, SlashMenuPopupBorder);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var scaleX = new DoubleAnimation
        {
            From = 0.94,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(165)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(scaleX, SlashMenuScale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");

        var scaleY = new DoubleAnimation
        {
            From = 0.94,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(165)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(scaleY, SlashMenuScale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");

        var slideY = new DoubleAnimation
        {
            From = -6.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(165)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(slideY, SlashMenuTranslate);
        Storyboard.SetTargetProperty(slideY, "Y");

        SlashMenuPopupBorder.Opacity = 1.0;
        SlashMenuScale.ScaleX = 1.0;
        SlashMenuScale.ScaleY = 1.0;
        SlashMenuTranslate.Y = 0.0;

        sb.Children.Add(fade);
        sb.Children.Add(scaleX);
        sb.Children.Add(scaleY);
        sb.Children.Add(slideY);

        _slashPopupStoryboard = sb;
        sb.Begin();
    }

    private void UpdateSlashMenuAutocomplete()
    {
        var activeTb = GetActiveEditorTextBox();
        if (activeTb.FocusState == FocusState.Unfocused || activeTb.SelectionLength > 0)
        {
            if (!_isContextSlashMenuOpen)
                HideSlashMenuPopup();
            return;
        }

        var text = activeTb.Text ?? string.Empty;
        var caret = Math.Clamp(activeTb.SelectionStart, 0, text.Length);

        if (TryGetActiveSlashTrigger(text, caret, out var slashIndex, out var query))
        {
            _isContextSlashMenuOpen = false;
            _activeSlashIndex = slashIndex;
            ViewModel.UpdateSlashSuggestions(query);
            if (ViewModel.SlashMenuSuggestions.Count > 0)
            {
                ShowSlashMenuPopup();
                if (SlashMenuListView.SelectedIndex < 0 || SlashMenuListView.SelectedIndex >= ViewModel.SlashMenuSuggestions.Count)
                    SlashMenuListView.SelectedIndex = 0;
                return;
            }
        }

        if (_isContextSlashMenuOpen)
            return;

        HideSlashMenuPopup();
    }

    private void HideSlashMenuPopup()
    {
        _isContextSlashMenuOpen = false;
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
                slashIndex = caret;
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

            case "image":
                HideSlashMenuPopup();
                ApplyEditorText(baseText, slashIndex);
                _ = PickAndInsertNoteImageAsync();
                return;

            case "left":
            case "center":
            case "right":
                ApplyEditorText(baseText, slashIndex);
                SetEditorBlockAlignment(item.Id switch
                {
                    "center" => TextAlignment.Center,
                    "right" => TextAlignment.Right,
                    _ => TextAlignment.Left
                });
                return;

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

            case "image":
                block.Content = cleaned;
                tb.Text = cleaned;
                FlushSyncLiveBlocksToViewModel();
                _ = PickAndInsertNoteImageAsync(block);
                return;

            case "left":
            case "center":
            case "right":
            {
                var targetAlign = item.Id switch
                {
                    "center" => TextAlignment.Center,
                    "right" => TextAlignment.Right,
                    _ => TextAlignment.Left
                };
                _activeEditorAlignment = targetAlign;
                block.Content = cleaned;
                block.BlockAlignment = targetAlign;
                for (var i = idx + 1; i < _liveBlocks.Count; i++)
                {
                    if (_liveBlocks[i].Kind == LiveBlockKind.Paragraph && string.IsNullOrEmpty(_liveBlocks[i].Content))
                    {
                        _liveBlocks[i].BlockAlignment = targetAlign;
                        if (_liveBlocks[i].PrimaryTextBox is { } trailingTb)
                            trailingTb.TextAlignment = targetAlign;
                    }
                }
                break;
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

    private void HideSmoothCaretOverlay()
    {
        _smoothCaretStoryboard?.Stop();
        _hasSmoothCaretPosition = false;
        if (SmoothCaretOverlayBorder.Visibility != Visibility.Collapsed)
        {
            SmoothCaretOverlayBorder.Opacity = 0.0;
            SmoothCaretOverlayBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void ScheduleSmoothCaretUpdate(TextBox? targetTb = null)
    {
        if (ViewModel.IsSettingsOpen)
        {
            HideSmoothCaretOverlay();
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            var tb = targetTb ?? GetActiveEditorTextBox();
            if (ViewModel.IsSettingsOpen ||
                tb.FocusState == FocusState.Unfocused ||
                tb.Visibility != Visibility.Visible ||
                tb.SelectionLength > 0)
            {
                HideSmoothCaretOverlay();
                return;
            }

            if (!TryComputeTargetCaretRect(tb, out var targetX, out var targetY, out var targetHeight))
            {
                HideSmoothCaretOverlay();
                return;
            }

            EnsureLiveRenderResources();
            SmoothCaretOverlayBorder.Height = targetHeight;
            SmoothCaretOverlayBorder.Background = _cachedPrimaryBrush;

            var fromX = SmoothCaretTranslate.X;
            var fromY = SmoothCaretTranslate.Y;
            var dx = Math.Abs(targetX - fromX);
            var dy = Math.Abs(targetY - fromY);

            var isFluid = ViewModel.IsFluidAnimationEnabled;
            if (!isFluid ||
                !_hasSmoothCaretPosition ||
                SmoothCaretOverlayBorder.Visibility != Visibility.Visible ||
                dx > 360.0 ||
                dy > 220.0)
            {
                _hasSmoothCaretPosition = true;
                fromX = targetX;
                fromY = targetY;
                SmoothCaretTranslate.X = targetX;
                SmoothCaretTranslate.Y = targetY;
                SmoothCaretOverlayBorder.Visibility = Visibility.Visible;
            }

            _smoothCaretStoryboard?.Stop();
            var sb = new Storyboard();

            if (isFluid)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                if (dx >= 0.4)
                {
                    var moveX = new DoubleAnimation
                    {
                        From = fromX,
                        To = targetX,
                        Duration = new Duration(TimeSpan.FromMilliseconds(85)),
                        EasingFunction = ease,
                        FillBehavior = FillBehavior.Stop
                    };
                    Storyboard.SetTarget(moveX, SmoothCaretTranslate);
                    Storyboard.SetTargetProperty(moveX, "X");
                    sb.Children.Add(moveX);
                }

                if (dy >= 0.4)
                {
                    var moveY = new DoubleAnimation
                    {
                        From = fromY,
                        To = targetY,
                        Duration = new Duration(TimeSpan.FromMilliseconds(85)),
                        EasingFunction = ease,
                        FillBehavior = FillBehavior.Stop
                    };
                    Storyboard.SetTarget(moveY, SmoothCaretTranslate);
                    Storyboard.SetTargetProperty(moveY, "Y");
                    sb.Children.Add(moveY);
                }
            }

            SmoothCaretTranslate.X = targetX;
            SmoothCaretTranslate.Y = targetY;

            var blinkAnim = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            blinkAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)),
                Value = 1.0
            });
            blinkAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(530)),
                Value = 0.0
            });
            blinkAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1060)),
                Value = 1.0
            });
            Storyboard.SetTarget(blinkAnim, SmoothCaretOverlayBorder);
            Storyboard.SetTargetProperty(blinkAnim, "Opacity");
            sb.Children.Add(blinkAnim);

            SmoothCaretOverlayBorder.Opacity = 1.0;
            SmoothCaretOverlayBorder.Visibility = Visibility.Visible;

            _smoothCaretStoryboard = sb;
            sb.Begin();
        });
    }

    private bool TryComputeTargetCaretRect(TextBox tb, out double targetX, out double targetY, out double targetHeight)
    {
        targetX = 0;
        targetY = 0;
        targetHeight = 20;

        if (!tb.IsLoaded || tb.ActualWidth <= 0 || tb.ActualHeight <= 0 || EditorPaneGrid.ActualWidth <= 0 || EditorPaneGrid.ActualHeight <= 0)
            return false;

        try
        {
            var text = tb.Text ?? string.Empty;
            var caret = Math.Clamp(tb.SelectionStart, 0, text.Length);
            double localX;
            double localY;
            var defaultLineHeight = Math.Clamp(Math.Round(tb.FontSize * 1.3), 15.0, 42.0);

            if (text.Length == 0)
            {
                localY = tb.Padding.Top + Math.Max(0.0, (tb.ActualHeight - tb.Padding.Top - tb.Padding.Bottom - defaultLineHeight) * 0.5);
                targetHeight = defaultLineHeight;
                localX = tb.TextAlignment switch
                {
                    TextAlignment.Center => tb.ActualWidth * 0.5,
                    TextAlignment.Right => Math.Max(tb.Padding.Left, tb.ActualWidth - tb.Padding.Right - 2.0),
                    _ => tb.Padding.Left
                };
            }
            else
            {
                Windows.Foundation.Rect charRect;
                if (caret == 0)
                    charRect = tb.GetRectFromCharacterIndex(0, trailingEdge: false);
                else if (caret < text.Length && text[caret - 1] is '\r' or '\n')
                    charRect = tb.GetRectFromCharacterIndex(caret, trailingEdge: false);
                else
                    charRect = tb.GetRectFromCharacterIndex(caret - 1, trailingEdge: true);

                if (double.IsNaN(charRect.X) || double.IsNaN(charRect.Y) || (charRect.Height <= 0 && charRect.Width <= 0 && caret > 0))
                    return false;

                localX = charRect.X;
                localY = charRect.Y;
                if (tb.Padding.Left > 0 && localX < tb.Padding.Left - 1.0)
                {
                    localX += tb.Padding.Left;
                    localY += tb.Padding.Top;
                }

                targetHeight = Math.Clamp(charRect.Height > 4.0 ? charRect.Height : defaultLineHeight, 15.0, 42.0);
            }

            var panePt = tb.TransformToVisual(EditorPaneGrid).TransformPoint(new Point(localX, localY));
            targetX = panePt.X;
            targetY = panePt.Y;

            return targetX >= 0 &&
                   targetY >= 0 &&
                   targetX <= EditorPaneGrid.ActualWidth - 2 &&
                   targetY + targetHeight <= EditorPaneGrid.ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.SelectedNote):
                _liveSyncTimer.Stop();
                HideSmoothCaretOverlay();
                HideSidebarContextPopup();
                HideMentionPopup();
                HideSlashMenuPopup();
                HideSelectionToolbar(immediate: true);
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
                HideSmoothCaretOverlay();
                HideSidebarContextPopup();
                HideMentionPopup();
                HideSlashMenuPopup();
                HideSelectionToolbar(immediate: true);
                if (ViewModel.CurrentViewMode == EditorViewMode.Live)
                    RebuildLiveEditor(reparse: true, focusBlockIndex: 0);
                else if (EditorTextBox.Text != ViewModel.EditorText)
                    EditorTextBox.Text = ViewModel.EditorText;
                AnimateViewModeChange();
                break;

            case nameof(MainViewModel.IsSidebarOpen):
                HideSidebarContextPopup();
                AnimateSidebar(ViewModel.IsSidebarOpen);
                break;

            case nameof(MainViewModel.IsSettingsOpen):
                HideSmoothCaretOverlay();
                HideSidebarContextPopup();
                if (_liveSyncTimer.IsEnabled)
                    FlushSyncLiveBlocksToViewModel();
                AnimateSettingsModal(ViewModel.IsSettingsOpen);
                break;

            case nameof(MainViewModel.IsFluidAnimationEnabled):
                if (!ViewModel.IsFluidAnimationEnabled)
                    HideSmoothCaretOverlay();
                break;

            case nameof(MainViewModel.CurrentSettingsSection):
                AnimateSettingsTabPill();
                break;

            case nameof(MainViewModel.CurrentThemeMode):
                AnimateThemeModePill();
                ApplyEditorTextBoxTheme();
                break;

            case nameof(MainViewModel.CurrentBackdropMode):
                ApplyWindowBackdrop();
                AnimateBackdropModePill();
                break;

            case nameof(MainViewModel.SurfaceOpacityPercent):
                UpdateAcrylicControllerProperties();
                break;

            case nameof(MainViewModel.IsDarkMode):
                NoteItemViewModel.IsLightTheme = !ViewModel.IsDarkMode;
                foreach (var n in ViewModel.FilteredNotes)
                    n.NotifyVisualStateChanged();
                UpdateAcrylicControllerProperties();
                RecycleAllLiveBlocksToPool();
                _liveTextBoxPool.Clear();
                ApplyEditorTextBoxTheme();
                RebuildLiveEditor(reparse: false);
                break;

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

    private void OnSidebarTogglePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        var targetX = ViewModel.IsSidebarOpen ? -1.2 : -0.8;
        AnimateDoubleProperty(SidebarIconTranslate, "X", SidebarIconTranslate.X, targetX, 140);
        SidebarIconTranslate.X = targetX;
    }

    private void OnSidebarTogglePointerExited(object sender, PointerRoutedEventArgs e)
    {
        var targetX = ViewModel.IsSidebarOpen ? 0.0 : -2.0;
        AnimateDoubleProperty(SidebarIconTranslate, "X", SidebarIconTranslate.X, targetX, 140);
        SidebarIconTranslate.X = targetX;
    }

    private void OnAddNotePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateDoubleProperty(AddNoteIconScale, "ScaleX", AddNoteIconScale.ScaleX, 1.12, 150);
        AnimateDoubleProperty(AddNoteIconScale, "ScaleY", AddNoteIconScale.ScaleY, 1.12, 150);
        AnimateDoubleProperty(AddNoteIconRotate, "Angle", AddNoteIconRotate.Angle, 90.0, 180);
        AddNoteIconScale.ScaleX = 1.12;
        AddNoteIconScale.ScaleY = 1.12;
        AddNoteIconRotate.Angle = 90.0;
    }

    private void OnAddNotePointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateDoubleProperty(AddNoteIconScale, "ScaleX", AddNoteIconScale.ScaleX, 1.0, 150);
        AnimateDoubleProperty(AddNoteIconScale, "ScaleY", AddNoteIconScale.ScaleY, 1.0, 150);
        AnimateDoubleProperty(AddNoteIconRotate, "Angle", AddNoteIconRotate.Angle, 0.0, 180);
        AddNoteIconScale.ScaleX = 1.0;
        AddNoteIconScale.ScaleY = 1.0;
        AddNoteIconRotate.Angle = 0.0;
    }

    private void OnAddFolderPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateDoubleProperty(AddFolderIconScale, "ScaleX", AddFolderIconScale.ScaleX, 1.08, 150);
        AnimateDoubleProperty(AddFolderIconScale, "ScaleY", AddFolderIconScale.ScaleY, 1.08, 150);
        AnimateDoubleProperty(AddFolderPlusScale, "ScaleX", AddFolderPlusScale.ScaleX, 1.2, 160);
        AnimateDoubleProperty(AddFolderPlusScale, "ScaleY", AddFolderPlusScale.ScaleY, 1.2, 160);
        AddFolderIconScale.ScaleX = 1.08;
        AddFolderIconScale.ScaleY = 1.08;
        AddFolderPlusScale.ScaleX = 1.2;
        AddFolderPlusScale.ScaleY = 1.2;
    }

    private void OnAddFolderPointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateDoubleProperty(AddFolderIconScale, "ScaleX", AddFolderIconScale.ScaleX, 1.0, 150);
        AnimateDoubleProperty(AddFolderIconScale, "ScaleY", AddFolderIconScale.ScaleY, 1.0, 150);
        AnimateDoubleProperty(AddFolderPlusScale, "ScaleX", AddFolderPlusScale.ScaleX, 1.0, 160);
        AnimateDoubleProperty(AddFolderPlusScale, "ScaleY", AddFolderPlusScale.ScaleY, 1.0, 160);
        AddFolderIconScale.ScaleX = 1.0;
        AddFolderIconScale.ScaleY = 1.0;
        AddFolderPlusScale.ScaleX = 1.0;
        AddFolderPlusScale.ScaleY = 1.0;
    }

    private void OnSettingsGearPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        var baseAngle = ViewModel.IsSettingsOpen ? 180.0 : 0.0;
        var targetAngle = baseAngle + 45.0;
        AnimateDoubleProperty(SettingsGearRotate, "Angle", SettingsGearRotate.Angle, targetAngle, 180);
        SettingsGearRotate.Angle = targetAngle;
    }

    private void OnSettingsGearPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var targetAngle = ViewModel.IsSettingsOpen ? 180.0 : 0.0;
        AnimateDoubleProperty(SettingsGearRotate, "Angle", SettingsGearRotate.Angle, targetAngle, 180);
        SettingsGearRotate.Angle = targetAngle;
    }

    private static void AnimateDoubleProperty(DependencyObject target, string propertyPath, double from, double to, int durationMs)
    {
        var sb = new Storyboard();
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, propertyPath);
        sb.Children.Add(anim);
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

    private double GetSettingsTabIndicatorTargetX()
    {
        var step = SettingsTabBarGrid.ActualWidth > 0
            ? SettingsTabBarGrid.ActualWidth / 4.0
            : ViewModel.SettingsSectionIndicatorOffsetX;
        return (int)ViewModel.CurrentSettingsSection * step;
    }

    private void OnSettingsTabBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SettingsTabIndicatorTranslate.X = GetSettingsTabIndicatorTargetX();
    }

    private void AnimateSettingsTabPill()
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var targetX = GetSettingsTabIndicatorTargetX();

        var pillSlide = new DoubleAnimation
        {
            From = SettingsTabIndicatorTranslate.X,
            To = targetX,
            Duration = new Duration(TimeSpan.FromMilliseconds(190)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(pillSlide, SettingsTabIndicatorTranslate);
        Storyboard.SetTargetProperty(pillSlide, "X");
        SettingsTabIndicatorTranslate.X = targetX;

        sb.Children.Add(pillSlide);
        sb.Begin();
    }

    private void AnimateSettingsModal(bool open)
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var targetOpacity = open ? 1.0 : 0.0;
        var targetAngle = open ? 180.0 : 0.0;

        ApplyResponsiveLayout();

        if (open)
        {
            ThemeIndicatorTranslate.X = ViewModel.ThemeIndicatorOffsetX;
            BackdropIndicatorTranslate.X = ViewModel.BackdropIndicatorOffsetX;
            SettingsTabIndicatorTranslate.X = GetSettingsTabIndicatorTargetX();
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
        HideSidebarContextPopup();
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
        if (sender is FrameworkElement { DataContext: NoteItemViewModel item })
            item.IsHovered = true;
    }

    private void OnSidebarRowPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NoteItemViewModel item })
            item.IsHovered = false;
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

    private static bool IsSupportedNoteImageExtension(string? ext) =>
        VaultStorageService.IsSupportedImageExtension(ext);

    private async Task PickAndInsertNoteImageAsync(LiveDocBlock? targetLiveBlock = null)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".svg");

            var file = await picker.PickSingleFileAsync();
            if (file is not null)
                await InsertImageFileIntoCurrentNoteAsync(file.Path, targetLiveBlock);
        }
        catch
        {
        }
    }

    private async Task InsertImageFileIntoCurrentNoteAsync(string sourceFilePath, LiveDocBlock? targetLiveBlock = null)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            return;

        try
        {
            if (ViewModel.SelectedNote is null || ViewModel.SelectedNote.IsFolder)
            {
                var targetFolder = ViewModel.ActiveFolderPath;
                if (targetFolder.Equals("assets", StringComparison.OrdinalIgnoreCase) ||
                    targetFolder.EndsWith("/assets", StringComparison.OrdinalIgnoreCase))
                {
                    targetFolder = string.Empty;
                }
                await ViewModel.CreateNoteInFolderAsync(targetFolder);
            }

            var markdownImage = await VaultStorageService.CopyImageToAssetsAndFormatMarkdownAsync(
                sourceFilePath,
                _activeEditorAlignment);

            if (ViewModel.CurrentViewMode == EditorViewMode.Live)
            {
                var targetBlock = targetLiveBlock ?? _activeLiveBlock;
                var idx = targetBlock is not null ? _liveBlocks.IndexOf(targetBlock) : _liveBlocks.Count - 1;
                if (idx < 0)
                    idx = Math.Max(0, _liveBlocks.Count - 1);

                if (_liveBlocks.Count == 0)
                {
                    _liveBlocks.Add(new LiveDocBlock
                    {
                        Kind = LiveBlockKind.ImageBlock,
                        BlockAlignment = _activeEditorAlignment,
                        Content = markdownImage
                    });
                    _liveBlocks.Add(new LiveDocBlock
                    {
                        Kind = LiveBlockKind.Paragraph,
                        BlockAlignment = _activeEditorAlignment
                    });
                    RebuildLiveEditor(reparse: false, focusBlockIndex: 1, focusCaretIndex: 0);
                }
                else if (_liveBlocks[idx].Kind == LiveBlockKind.Paragraph &&
                         string.IsNullOrWhiteSpace(_liveBlocks[idx].Content))
                {
                    _liveBlocks[idx].Kind = LiveBlockKind.ImageBlock;
                    _liveBlocks[idx].BlockAlignment = _activeEditorAlignment;
                    _liveBlocks[idx].Content = markdownImage;
                    RefreshLiveBlockAt(idx);
                    FocusOrInsertParagraphAfter(idx);
                }
                else
                {
                    InsertLiveBlockAt(idx + 1, new LiveDocBlock
                    {
                        Kind = LiveBlockKind.ImageBlock,
                        BlockAlignment = _activeEditorAlignment,
                        Content = markdownImage
                    });
                    FocusOrInsertParagraphAfter(idx + 1);
                }

                FlushSyncLiveBlocksToViewModel();
                return;
            }

            var text = EditorTextBox.Text ?? string.Empty;
            var caret = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
            var lineStart = FindLineStart(text, caret);
            var leadingNewline = caret > lineStart && !string.IsNullOrWhiteSpace(text[lineStart..caret]) ? "\r" : string.Empty;
            var insertBlock = $"{leadingNewline}{markdownImage}\r";
            var updated = text.Insert(caret, insertBlock);
            ApplyEditorText(updated, caret + insertBlock.Length);
            EditorTextBox.Focus(FocusState.Programmatic);
        }
        catch
        {
        }
    }

    private async void OnEditorTextBoxPaste(object sender, TextControlPasteEventArgs e)
    {
        try
        {
            var dataView = Clipboard.GetContent();
            if (dataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await dataView.GetStorageItemsAsync();
                var targetBlock = (sender as TextBox)?.Tag as LiveDocBlock ?? _activeLiveBlock;
                foreach (var item in items)
                {
                    if (item is StorageFile file && IsSupportedNoteImageExtension(Path.GetExtension(file.Path)))
                    {
                        e.Handled = true;
                        await InsertImageFileIntoCurrentNoteAsync(file.Path, targetBlock);
                        return;
                    }
                }
            }

            if (dataView.Contains(StandardDataFormats.Bitmap))
            {
                e.Handled = true;
                var targetBlock = (sender as TextBox)?.Tag as LiveDocBlock ?? _activeLiveBlock;
                var bmpRef = await dataView.GetBitmapAsync();
                if (bmpRef is not null)
                    await InsertClipboardBitmapIntoCurrentNoteAsync(bmpRef, targetBlock);
            }
        }
        catch
        {
        }
    }

    private async Task InsertClipboardBitmapIntoCurrentNoteAsync(
        Windows.Storage.Streams.RandomAccessStreamReference bitmapRef,
        LiveDocBlock? targetLiveBlock = null)
    {
        try
        {
            if (ViewModel.SelectedNote is null || ViewModel.SelectedNote.IsFolder)
            {
                var targetFolder = ViewModel.ActiveFolderPath;
                if (targetFolder.Equals("assets", StringComparison.OrdinalIgnoreCase) ||
                    targetFolder.EndsWith("/assets", StringComparison.OrdinalIgnoreCase))
                {
                    targetFolder = string.Empty;
                }
                await ViewModel.CreateNoteInFolderAsync(targetFolder);
            }

            var destPath = await VaultStorageService.SaveClipboardBitmapToAssetsAsync(bitmapRef);
            await InsertImageFileIntoCurrentNoteAsync(destPath, targetLiveBlock);
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
            e.DragUIOverride.Caption = "Insert photo or import .md";
            e.Handled = true;
        }
    }

    private async void OnRootGridDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        e.Handled = true;

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                if (item is not StorageFile file)
                    continue;

                if (file.FileType.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                    file.FileType.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    await ViewModel.ImportMarkdownFileAsync(file.Path);
                }
                else if (IsSupportedNoteImageExtension(file.FileType))
                {
                    await InsertImageFileIntoCurrentNoteAsync(file.Path);
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

    private DispatcherTimer? _warningToastTimer;
    private DispatcherTimer? _lockShakeTimer;
    private int _shakeStep;
    private DispatcherTimer? _calloutDismissTimer;
    private DispatcherTimer? _calloutPulseTimer;
    private int _pulseStep;

    private void OnActiveNoteLockChanged(bool isLocked)
    {
        EditorTextBox.IsReadOnly = isLocked;

        if (_liveBlocks is not null)
        {
            foreach (var block in _liveBlocks)
            {
                if (block.PrimaryTextBox is { } tb)
                    tb.IsReadOnly = isLocked;
            }
        }

        if (isLocked)
        {
            HideSelectionToolbar();
            HideSlashMenuPopup();
            HideMentionPopup();
        }
    }

    private void OnToggleNoteLockClicked(object sender, RoutedEventArgs e)
    {
        DismissLockDiscoveryCallout();
        ViewModel.ToggleActiveNoteLock();
    }

    private void TriggerLockedNoteAccidentalEditFeedback()
    {
        _shakeStep = 0;
        _lockShakeTimer?.Stop();
        _lockShakeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
        _lockShakeTimer.Tick += (_, _) =>
        {
            _shakeStep++;
            switch (_shakeStep)
            {
                case 1: LockContainerTransform.TranslateX = -4; break;
                case 2: LockContainerTransform.TranslateX = 4; break;
                case 3: LockContainerTransform.TranslateX = -2; break;
                case 4: LockContainerTransform.TranslateX = 2; break;
                case 5:
                default:
                    LockContainerTransform.TranslateX = 0;
                    _lockShakeTimer?.Stop();
                    break;
            }
        };
        _lockShakeTimer.Start();

        LockedNoteWarningToast.Opacity = 1.0;
        LockedWarningToastTranslate.Y = 0;

        _warningToastTimer?.Stop();
        _warningToastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _warningToastTimer.Tick += (_, _) =>
        {
            _warningToastTimer?.Stop();
            LockedNoteWarningToast.Opacity = 0.0;
            LockedWarningToastTranslate.Y = -8;
        };
        _warningToastTimer.Start();
    }

    private void ShowLockDiscoveryCallout()
    {
        LockDiscoveryCalloutPopup.IsOpen = true;

        _pulseStep = 0;
        _calloutPulseTimer?.Stop();
        _calloutPulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _calloutPulseTimer.Tick += (_, _) =>
        {
            _pulseStep++;
            double scale;
            if (_pulseStep <= 10)
                scale = 1.0 + (0.08 * (_pulseStep / 10.0));
            else if (_pulseStep <= 20)
                scale = 1.08 - (0.08 * ((_pulseStep - 10) / 10.0));
            else
            {
                scale = 1.0;
                _calloutPulseTimer?.Stop();
            }
            LockContainerTransform.ScaleX = scale;
            LockContainerTransform.ScaleY = scale;
        };
        _calloutPulseTimer.Start();

        _calloutDismissTimer?.Stop();
        _calloutDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _calloutDismissTimer.Tick += (_, _) =>
        {
            _calloutDismissTimer?.Stop();
            DismissLockDiscoveryCallout();
        };
        _calloutDismissTimer.Start();
    }

    private void DismissLockDiscoveryCallout()
    {
        _calloutDismissTimer?.Stop();
        _calloutPulseTimer?.Stop();
        if (LockContainerTransform is not null)
        {
            LockContainerTransform.ScaleX = 1.0;
            LockContainerTransform.ScaleY = 1.0;
        }
        if (LockDiscoveryCalloutPopup is not null && LockDiscoveryCalloutPopup.IsOpen)
            LockDiscoveryCalloutPopup.IsOpen = false;
    }

    private void UpdateTitleBarChromeRegions() =>
        _chromeService.UpdateTitleBarRegions(
            TopTitleBarGrid,
            SidebarToggleButton,
            ViewSwitcherBorder,
            NoteUnlockButton,
            NoteLockButton,
            UpdateTopBarButton,
            ZoomBadgeButton);
}
