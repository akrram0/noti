using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Noti.App.ViewModels;

namespace Noti.App;

public sealed partial class MainWindow
{
    private static bool IsEditableLiveBlock(LiveDocBlock block) =>
        block.Kind is not (LiveBlockKind.Divider or LiveBlockKind.TableBlock or LiveBlockKind.ImageBlock or LiveBlockKind.HtmlWrapper);

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
        HideSmoothCaretOverlay();
        if (_liveMaterializedCount < _liveBlocks.Count &&
            LiveEditorScrollViewer.VerticalOffset + LiveEditorScrollViewer.ViewportHeight >= LiveEditorScrollViewer.ExtentHeight - 420)
        {
            EnsureLiveBlocksMaterializedThrough(Math.Min(_liveBlocks.Count - 1, _liveMaterializedCount + 48));
        }
    }

    private void ApplyEditorTextBoxTheme()
    {
        EnsureLiveRenderResources();
        EditorTextBox.RequestedTheme = ViewModel.CurrentElementTheme;
        EditorTextBox.Background = _cachedInputBgBrush;
        EditorTextBox.ContextFlyout = null;
        EditorTextBox.SelectionFlyout = null;
    }

    private void EnsureLiveRenderResources(bool refreshTitles = false)
    {
        var isDarkMode = ViewModel.IsDarkMode;
        if (_cachedIsDarkMode != isDarkMode)
        {
            _cachedIsDarkMode = isDarkMode;
            _cachedPrimaryBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 245, 245, 247) : ColorHelper.FromArgb(255, 28, 28, 30));
            _cachedSecondaryBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 142, 142, 147) : ColorHelper.FromArgb(255, 63, 63, 70));
            _cachedSurfaceBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(36, 255, 255, 255) : ColorHelper.FromArgb(135, 255, 255, 255));
            _cachedBorderBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(38, 255, 255, 255) : ColorHelper.FromArgb(28, 0, 0, 0));
            _cachedInputBgBrush = TransparentBrush;
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
        if (block.PrimaryTextBox is not { } tb)
            return;

        tb.Tag = null;
        if (tb.Parent is Panel parentPanel)
            parentPanel.Children.Remove(tb);
        else if (tb.Parent is Border parentBorder)
            parentBorder.Child = null;

        block.PrimaryTextBox = null;
        if (!tb.AcceptsReturn && _liveTextBoxPool.Count < 160)
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

            pooledTb.Background = _cachedInputBgBrush;
            pooledTb.RequestedTheme = ViewModel.CurrentElementTheme;
            pooledTb.ContextFlyout = null;
            pooledTb.SelectionFlyout = null;
            pooledTb.Tag = block;
            return pooledTb;
        }

        var tb = new TextBox
        {
            AcceptsReturn = false,
            IsSpellCheckEnabled = false,
            Background = _cachedInputBgBrush,
            RequestedTheme = ViewModel.CurrentElementTheme,
            SelectionHighlightColor = AccentBrush,
            SelectionHighlightColorWhenNotFocused = SelectionUnfocusedBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 1.0,
            ContextFlyout = null,
            SelectionFlyout = null,
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

        var isAfterVisualBlock = blockIndex > 0 &&
                                 blockIndex < _liveBlocks.Count &&
                                 _liveBlocks[blockIndex - 1].Kind is LiveBlockKind.ImageBlock or LiveBlockKind.TableBlock or LiveBlockKind.Divider;

        placeholder = block.Kind switch
        {
            LiveBlockKind.Heading1 => "Heading 1",
            LiveBlockKind.Heading2 => "Heading 2",
            LiveBlockKind.Heading3 => "Heading 3",
            LiveBlockKind.Checklist => "To-do",
            LiveBlockKind.Bullet => "List item",
            LiveBlockKind.Quote => "Quote...",
            _ => (blockIndex == 0 && _liveBlocks.Count == 1) || (isAfterVisualBlock && blockIndex == _liveBlocks.Count - 1)
                ? "Type '/' for commands, or start writing..."
                : string.Empty
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
        var rootPt = e.GetCurrentPoint(null);
        if (rootPt.Properties.IsRightButtonPressed)
            return;

        if (rootPt.Timestamp != 0 && rootPt.Timestamp == _lastCanvasClickTimestamp)
            return;

        if (_isContextSlashMenuOpen)
            HideSlashMenuPopup();

        var cur = e.OriginalSource as DependencyObject;
        while (cur is not null && !ReferenceEquals(cur, LiveEditorScrollViewer))
        {
            if (cur is TextBox or Button or CheckBox or ScrollBar or Thumb)
                return;

            if (cur is FrameworkElement fe)
            {
                for (var i = 0; i < _liveBlocks.Count; i++)
                {
                    var blk = _liveBlocks[i];
                    if (ReferenceEquals(blk.RootElement, fe) &&
                        blk.Kind is LiveBlockKind.ImageBlock or LiveBlockKind.TableBlock or LiveBlockKind.Divider)
                    {
                        return;
                    }
                }
            }

            cur = VisualTreeHelper.GetParent(cur);
        }

        _lastCanvasClickTimestamp = rootPt.Timestamp;
        e.Handled = true;
        FocusNearestLiveBlockAtY(e.GetCurrentPoint(LiveEditorPanel).Position.Y);
    }

    private void FocusOrInsertParagraphAfter(int blockIndex)
    {
        if (_liveBlocks.Count == 0)
        {
            InsertLiveBlockAt(0, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
            FlushSyncLiveBlocksToViewModel();
            return;
        }

        var clamped = Math.Clamp(blockIndex, 0, _liveBlocks.Count - 1);
        for (var next = clamped + 1; next < _liveBlocks.Count; next++)
        {
            if (_liveBlocks[next].Kind == LiveBlockKind.HtmlWrapper)
                continue;

            EnsureLiveBlocksMaterializedThrough(next);
            if (IsEditableLiveBlock(_liveBlocks[next]))
            {
                FocusLiveBlockAt(next, 0);
                return;
            }
            break;
        }

        InsertLiveBlockAt(clamped + 1, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
        FlushSyncLiveBlocksToViewModel();
    }

    private void FocusNearestLiveBlockAtY(double clickY)
    {
        if (_liveBlocks.Count == 0)
        {
            InsertLiveBlockAt(0, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
            FlushSyncLiveBlocksToViewModel();
            return;
        }

        var count = Math.Min(LiveEditorPanel.Children.Count, _liveBlocks.Count);

        for (var i = 0; i < count; i++)
        {
            if (_liveBlocks[i].Kind == LiveBlockKind.HtmlWrapper)
                continue;

            var child = LiveEditorPanel.Children[i];
            if (child.Visibility == Visibility.Collapsed)
                continue;

            var height = child is FrameworkElement fe && fe.ActualHeight > 0 ? fe.ActualHeight : child.ActualSize.Y;
            var bottom = child.ActualOffset.Y + height + 4;
            if (clickY <= bottom)
            {
                if (IsEditableLiveBlock(_liveBlocks[i]))
                    FocusLiveBlockAt(i);
                else
                    FocusOrInsertParagraphAfter(i);
                return;
            }
        }

        EnsureLiveBlocksMaterializedThrough(_liveBlocks.Count - 1);

        for (var i = _liveBlocks.Count - 1; i >= 0; i--)
        {
            if (_liveBlocks[i].Kind == LiveBlockKind.HtmlWrapper)
                continue;

            if (IsEditableLiveBlock(_liveBlocks[i]) && _liveBlocks[i].Kind != LiveBlockKind.CodeBlock)
            {
                FocusLiveBlockAt(i);
                return;
            }
            break;
        }

        InsertLiveBlockAt(_liveBlocks.Count, new LiveDocBlock { Kind = LiveBlockKind.Paragraph }, focusCaret: 0);
        FlushSyncLiveBlocksToViewModel();
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
                _activeEditorAlignment = _liveBlocks.LastOrDefault(b => b.Kind != LiveBlockKind.HtmlWrapper)?.BlockAlignment ?? TextAlignment.Left;
                LiveEditorScrollViewer.ChangeView(null, 0, null, true);
            }

            if (_liveBlocks.Count == 0)
            {
                _liveBlocks.Add(new LiveDocBlock
                {
                    Kind = LiveBlockKind.Paragraph,
                    BlockAlignment = _activeEditorAlignment
                });
            }

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

        var savedOffset = LiveEditorScrollViewer.VerticalOffset;
        EnsureLiveBlocksMaterializedThrough(index);

        var targetBlock = _liveBlocks[index];
        _activeLiveBlock = targetBlock;
        if (!string.IsNullOrEmpty(targetBlock.Content) || targetBlock.BlockAlignment != TextAlignment.Left)
            _activeEditorAlignment = targetBlock.BlockAlignment;

        if (targetBlock.DisplayTextBlock is { } dtb)
            dtb.Visibility = Visibility.Collapsed;

        if (targetBlock.PrimaryTextBox is not { } targetTb)
            return;

        targetTb.Visibility = Visibility.Visible;
        LiveEditorPanel.UpdateLayout();

        var caret = focusCaret ?? (targetTb.Text ?? string.Empty).Length;
        var initialClamped = Math.Clamp(caret, 0, (targetTb.Text ?? string.Empty).Length);
        targetTb.Focus(FocusState.Programmatic);
        targetTb.SelectionStart = initialClamped;
        targetTb.SelectionLength = 0;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.CurrentViewMode != EditorViewMode.Live ||
                ViewModel.IsSettingsOpen ||
                !ReferenceEquals(_activeLiveBlock, targetBlock) ||
                !ReferenceEquals(targetTb.Tag, targetBlock))
            {
                return;
            }

            if (targetBlock.DisplayTextBlock is { } d)
                d.Visibility = Visibility.Collapsed;
            targetTb.Visibility = Visibility.Visible;
            LiveEditorPanel.UpdateLayout();
            targetTb.Focus(FocusState.Programmatic);
            var clamped = Math.Clamp(caret, 0, (targetTb.Text ?? string.Empty).Length);
            targetTb.SelectionStart = clamped;
            targetTb.SelectionLength = 0;

            if (savedOffset > 4.0 && LiveEditorScrollViewer.VerticalOffset < 1.0 && index > 1)
                LiveEditorScrollViewer.ChangeView(null, savedOffset, null, true);
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
        if (block.BlockAlignment == TextAlignment.Left && _activeEditorAlignment != TextAlignment.Left)
            block.BlockAlignment = _activeEditorAlignment;

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
}
