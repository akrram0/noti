using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Noti.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Noti.App;

/// <summary>
/// Cross-block text selection for the Live canvas. Every line/paragraph is its own <see cref="TextBox"/>, so
/// native selection cannot span blocks. This partial adds a light-weight selection model (anchor + focus
/// block/offset) that mirrors the range onto each block's <c>SelectionStart</c>/<c>SelectionLength</c>, plus:
/// pointer drag across blocks, Shift+click, Shift+Up/Down extension, Ctrl+A, Ctrl+C/X, Backspace/Delete and
/// type-over-selection (via <c>BeforeTextChanging</c> diff + cancel), Enter-over-selection splitting, Escape
/// clearing, and auto-scroll while dragging. Native in-block selection remains fully authoritative whenever a
/// range is confined to a single block.
/// </summary>
public sealed partial class MainWindow
{
    private int _liveSelAnchorBlock = -1;
    private int _liveSelAnchorOffset;
    private int _liveSelFocusBlock = -1;
    private int _liveSelFocusOffset;
    private int _liveSelAppliedStart = -1;
    private int _liveSelAppliedEnd = -1;
    private bool _isLiveDragSelecting;
    private bool _isApplyingLiveSelection;
    private string? _pendingLiveReplaceText;

    private bool HasLiveMultiSelection =>
        _liveSelAnchorBlock >= 0 &&
        _liveSelFocusBlock >= 0 &&
        _liveSelFocusBlock != _liveSelAnchorBlock;

    private void WireLiveMultiSelection()
    {
        LiveEditorPanel.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnLiveSelectionPointerPressed), true);
        LiveEditorPanel.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnLiveSelectionPointerMoved), true);
        LiveEditorPanel.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnLiveSelectionPointerReleased), true);
        LiveEditorPanel.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnLiveSelectionPointerCanceled), true);
    }

    private void OnLiveSelectionPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(LiveEditorPanel);
        if (!point.Properties.IsLeftButtonPressed)
            return;
        if (ViewModel.CurrentViewMode != EditorViewMode.Live || ViewModel.IsSettingsOpen)
            return;

        if (!TryHitTestEditableLiveBlock(point.Position, out var blockIdx, out var charIdx))
        {
            ClearLiveMultiSelection();
            return;
        }

        var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (shiftDown && _liveSelAnchorBlock >= 0 && _liveSelAnchorBlock < _liveBlocks.Count)
        {
            _liveSelFocusBlock = blockIdx;
            _liveSelFocusOffset = charIdx;
            DispatcherQueue.TryEnqueue(ApplyLiveSelectionToBlocks);
        }
        else
        {
            if (HasLiveMultiSelection)
                ClearLiveSelectionVisuals(skipBlock: blockIdx);

            _liveSelAnchorBlock = blockIdx;
            _liveSelAnchorOffset = charIdx;
            _liveSelFocusBlock = blockIdx;
            _liveSelFocusOffset = charIdx;
            _liveSelAppliedStart = -1;
            _liveSelAppliedEnd = -1;
        }

        _isLiveDragSelecting = true;
    }

    private void OnLiveSelectionPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isLiveDragSelecting)
            return;
        if (ViewModel.CurrentViewMode != EditorViewMode.Live || ViewModel.IsSettingsOpen)
            return;

        var point = e.GetCurrentPoint(LiveEditorPanel);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _isLiveDragSelecting = false;
            return;
        }

        ScrollLiveCanvasForDragSelect(point.Position);

        if (!TryHitTestEditableLiveBlock(point.Position, out var blockIdx, out var charIdx))
            return;

        if (blockIdx == _liveSelAnchorBlock)
        {
            // Dragged back into the anchor block: hand selection back to the native TextBox.
            if (_liveSelFocusBlock != _liveSelAnchorBlock || _liveSelAppliedStart >= 0)
            {
                ClearLiveSelectionVisuals(skipBlock: _liveSelAnchorBlock);
                _liveSelFocusBlock = _liveSelAnchorBlock;
                _liveSelFocusOffset = charIdx;
                _liveSelAppliedStart = -1;
                _liveSelAppliedEnd = -1;
            }

            return;
        }

        _liveSelFocusBlock = blockIdx;
        _liveSelFocusOffset = charIdx;
        ApplyLiveSelectionToBlocks();
    }

    private void OnLiveSelectionPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isLiveDragSelecting = false;
        if (HasLiveMultiSelection)
        {
            HideSelectionToolbar();
            HideSmoothCaretOverlay();
        }
    }

    private void OnLiveSelectionPointerCanceled(object sender, PointerRoutedEventArgs e) =>
        _isLiveDragSelecting = false;

    private void ScrollLiveCanvasForDragSelect(Point panelPoint)
    {
        var viewportHeight = LiveEditorScrollViewer.ViewportHeight;
        if (viewportHeight <= 0)
            return;

        var top = LiveEditorScrollViewer.VerticalOffset;
        var bottom = top + viewportHeight;
        if (panelPoint.Y < top + 8)
            LiveEditorScrollViewer.ChangeView(null, Math.Max(0, top - 24), null, true);
        else if (panelPoint.Y > bottom - 8)
            LiveEditorScrollViewer.ChangeView(null, top + 24, null, true);
    }

    private bool TryHitTestEditableLiveBlock(Point panelPoint, out int blockIdx, out int charIdx)
    {
        blockIdx = -1;
        charIdx = 0;

        var count = Math.Min(_liveMaterializedCount, _liveBlocks.Count);
        var bestIdx = -1;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < count; i++)
        {
            if (!IsEditableLiveBlock(_liveBlocks[i]) || _liveBlocks[i].PrimaryTextBox is not { } tb)
                continue;
            if (tb.Visibility != Visibility.Visible || tb.ActualHeight <= 0)
                continue;

            var rect = tb.TransformToVisual(LiveEditorPanel)
                .TransformBounds(new Rect(0, 0, tb.ActualWidth, tb.ActualHeight));

            if (panelPoint.Y >= rect.Top && panelPoint.Y <= rect.Bottom)
            {
                bestIdx = i;
                bestDistance = 0;
                break;
            }

            var distance = panelPoint.Y < rect.Top ? rect.Top - panelPoint.Y : panelPoint.Y - rect.Bottom;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIdx = i;
            }
        }

        if (bestIdx < 0 || _liveBlocks[bestIdx].PrimaryTextBox is not { } targetTb)
            return false;

        var local = LiveEditorPanel.TransformToVisual(targetTb).TransformPoint(panelPoint);
        var textLength = (targetTb.Text ?? string.Empty).Length;
        charIdx = Math.Clamp(targetTb.GetCharacterIndexFromPoint(local, true), 0, textLength);
        blockIdx = bestIdx;
        return true;
    }

    private void ApplyLiveSelectionToBlocks()
    {
        if (!TryGetLiveSelectionBounds(out var startBlock, out var startOffset, out var endBlock, out var endOffset))
        {
            ClearLiveSelectionVisuals();
            ResetLiveMultiSelectionState();
            return;
        }

        _isApplyingLiveSelection = true;
        try
        {
            // Clear any block that fell out of the range since the last apply.
            if (_liveSelAppliedStart >= 0)
            {
                for (var i = _liveSelAppliedStart; i <= _liveSelAppliedEnd && i < _liveBlocks.Count; i++)
                {
                    if (i >= startBlock && i <= endBlock)
                        continue;
                    if (_liveBlocks[i].PrimaryTextBox is { } stale && stale.SelectionLength != 0)
                    {
                        stale.SelectionStart = Math.Clamp(stale.SelectionStart, 0, (stale.Text ?? string.Empty).Length);
                        stale.SelectionLength = 0;
                    }
                }
            }

            // Mirror the range onto every materialized editable block inside it.
            for (var i = startBlock; i <= endBlock && i < _liveBlocks.Count; i++)
            {
                if (!IsEditableLiveBlock(_liveBlocks[i]) || _liveBlocks[i].PrimaryTextBox is not { } tb)
                    continue;

                var textLength = (tb.Text ?? string.Empty).Length;
                int selectionStart;
                int selectionLength;
                if (i == startBlock && i == endBlock)
                {
                    selectionStart = Math.Clamp(startOffset, 0, textLength);
                    selectionLength = Math.Clamp(endOffset, selectionStart, textLength) - selectionStart;
                }
                else if (i == startBlock)
                {
                    selectionStart = Math.Clamp(startOffset, 0, textLength);
                    selectionLength = textLength - selectionStart;
                }
                else if (i == endBlock)
                {
                    selectionStart = 0;
                    selectionLength = Math.Clamp(endOffset, 0, textLength);
                }
                else
                {
                    selectionStart = 0;
                    selectionLength = textLength;
                }

                tb.SelectionStart = selectionStart;
                tb.SelectionLength = selectionLength;
            }
        }
        finally
        {
            _isApplyingLiveSelection = false;
        }

        _liveSelAppliedStart = startBlock;
        _liveSelAppliedEnd = endBlock;

        HideSelectionToolbar();
        HideSmoothCaretOverlay();
    }

    private void ClearLiveMultiSelection()
    {
        ClearLiveSelectionVisuals();
        ResetLiveMultiSelectionState();
    }

    private void ClearLiveSelectionVisuals(int skipBlock = -1)
    {
        if (_liveSelAppliedStart < 0 || _liveSelAppliedEnd < _liveSelAppliedStart)
            return;

        _isApplyingLiveSelection = true;
        try
        {
            for (var i = _liveSelAppliedStart; i <= _liveSelAppliedEnd && i < _liveBlocks.Count; i++)
            {
                if (i == skipBlock)
                    continue;
                if (_liveBlocks[i].PrimaryTextBox is { } tb && tb.SelectionLength != 0)
                {
                    tb.SelectionStart = Math.Clamp(tb.SelectionStart, 0, (tb.Text ?? string.Empty).Length);
                    tb.SelectionLength = 0;
                }
            }
        }
        finally
        {
            _isApplyingLiveSelection = false;
        }
    }

    private void ResetLiveMultiSelectionState()
    {
        _liveSelAnchorBlock = -1;
        _liveSelAnchorOffset = 0;
        _liveSelFocusBlock = -1;
        _liveSelFocusOffset = 0;
        _liveSelAppliedStart = -1;
        _liveSelAppliedEnd = -1;
        _isLiveDragSelecting = false;
        _pendingLiveReplaceText = null;
    }

    private bool TryGetLiveSelectionBounds(out int startBlock, out int startOffset, out int endBlock, out int endOffset)
    {
        startBlock = 0;
        startOffset = 0;
        endBlock = 0;
        endOffset = 0;
        if (!HasLiveMultiSelection)
            return false;
        if (_liveSelAnchorBlock >= _liveBlocks.Count || _liveSelFocusBlock >= _liveBlocks.Count)
            return false;

        startBlock = Math.Min(_liveSelAnchorBlock, _liveSelFocusBlock);
        endBlock = Math.Max(_liveSelAnchorBlock, _liveSelFocusBlock);
        startOffset = startBlock == _liveSelAnchorBlock ? _liveSelAnchorOffset : _liveSelFocusOffset;
        endOffset = endBlock == _liveSelAnchorBlock ? _liveSelAnchorOffset : _liveSelFocusOffset;
        return true;
    }

    private void ExtendLiveSelectionToBlock(int targetIndex, int column)
    {
        if (_liveSelAnchorBlock < 0)
        {
            var (focusedIndex, focusedOffset) = GetFocusedLiveBlock();
            if (focusedIndex < 0)
            {
                FocusLiveBlockAt(targetIndex, column);
                return;
            }

            _liveSelAnchorBlock = focusedIndex;
            _liveSelAnchorOffset = focusedOffset;
        }

        _liveSelFocusBlock = targetIndex;
        _liveSelFocusOffset = column;

        FocusLiveBlockAt(targetIndex, column);
        ApplyLiveSelectionToBlocks();
        DispatcherQueue.TryEnqueue(ApplyLiveSelectionToBlocks);
    }

    private (int Index, int Offset) GetFocusedLiveBlock()
    {
        var count = Math.Min(_liveMaterializedCount, _liveBlocks.Count);
        for (var i = 0; i < count; i++)
        {
            if (_liveBlocks[i].PrimaryTextBox is { } tb && tb.FocusState != FocusState.Unfocused)
                return (i, Math.Clamp(tb.SelectionStart, 0, (tb.Text ?? string.Empty).Length));
        }

        return (-1, 0);
    }

    private void SelectAllLiveBlocks()
    {
        var first = _liveBlocks.FindIndex(IsEditableLiveBlock);
        if (first < 0)
            return;

        var last = _liveBlocks.FindLastIndex(IsEditableLiveBlock);
        if (first == last)
        {
            if (_liveBlocks[first].PrimaryTextBox is { } tb)
            {
                tb.SelectionStart = 0;
                tb.SelectionLength = (tb.Text ?? string.Empty).Length;
            }

            return;
        }

        _liveSelAnchorBlock = first;
        _liveSelAnchorOffset = 0;
        _liveSelFocusBlock = last;
        _liveSelFocusOffset = (_liveBlocks[last].Content ?? string.Empty).Length;

        ApplyLiveSelectionToBlocks();
    }

    private void CopyLiveSelectionToClipboard(bool cut)
    {
        var text = BuildLiveSelectionText();
        if (!string.IsNullOrEmpty(text))
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }

        if (cut)
            ReplaceLiveSelection(string.Empty);
    }

    private string BuildLiveSelectionText()
    {
        if (!TryGetLiveSelectionBounds(out var startBlock, out var startOffset, out var endBlock, out var endOffset))
            return string.Empty;

        var lines = new List<string>(endBlock - startBlock + 1);
        for (var i = startBlock; i <= endBlock && i < _liveBlocks.Count; i++)
        {
            var content = _liveBlocks[i].Content ?? string.Empty;
            if (i == startBlock && i == endBlock)
                lines.Add(ClampedSlice(content, startOffset, endOffset));
            else if (i == startBlock)
                lines.Add(ClampedSlice(content, startOffset, content.Length));
            else if (i == endBlock)
                lines.Add(ClampedSlice(content, 0, endOffset));
            else
                lines.Add(content);
        }

        return string.Join("\n", lines);
    }

    private void ReplaceLiveSelection(string insertText)
    {
        if (!TryGetLiveSelectionBounds(out var startBlock, out var startOffset, out var endBlock, out var endOffset))
        {
            ResetLiveMultiSelectionState();
            return;
        }

        var startModel = _liveBlocks[startBlock];
        var endModel = _liveBlocks[endBlock];
        var head = ClampedSlice(startModel.Content, 0, startOffset);
        var tail = ClampedSlice(endModel.Content, endOffset, (endModel.Content ?? string.Empty).Length);

        var prevSync = _isSyncingLiveEditor;
        _isSyncingLiveEditor = true;
        try
        {
            RemoveLiveBlocksAfter(startBlock, endBlock);
            startModel.Content = head + insertText + tail;
            ResetLiveMultiSelectionState();
            RefreshLiveBlockAt(startBlock, Math.Clamp(startOffset + insertText.Length, 0, startModel.Content.Length));
            FlushSyncLiveBlocksToViewModel();
        }
        finally
        {
            _isSyncingLiveEditor = prevSync;
        }
    }

    private void SplitLiveSelection()
    {
        if (!TryGetLiveSelectionBounds(out var startBlock, out var startOffset, out var endBlock, out var endOffset))
        {
            ResetLiveMultiSelectionState();
            return;
        }

        var startModel = _liveBlocks[startBlock];
        var endModel = _liveBlocks[endBlock];
        var head = ClampedSlice(startModel.Content, 0, startOffset);
        var tail = ClampedSlice(endModel.Content, endOffset, (endModel.Content ?? string.Empty).Length);

        var prevSync = _isSyncingLiveEditor;
        _isSyncingLiveEditor = true;
        try
        {
            RemoveLiveBlocksAfter(startBlock, endBlock);
            startModel.Content = head;
            ResetLiveMultiSelectionState();

            var newBlock = new LiveDocBlock
            {
                Kind = LiveBlockKind.Paragraph,
                BlockAlignment = startModel.BlockAlignment,
                Content = tail
            };

            InsertLiveBlockAt(startBlock + 1, newBlock, focusCaret: 0);
            RefreshLiveBlockAt(startBlock);
            FlushSyncLiveBlocksToViewModel();
        }
        finally
        {
            _isSyncingLiveEditor = prevSync;
        }
    }

    private void RemoveLiveBlocksAfter(int startBlock, int endBlock)
    {
        for (var i = endBlock; i > startBlock; i--)
        {
            if (i >= _liveBlocks.Count)
                continue;

            RecycleBlockTextBox(_liveBlocks[i]);
            _liveBlocks.RemoveAt(i);
            if (i < LiveEditorPanel.Children.Count)
                LiveEditorPanel.Children.RemoveAt(i);
        }

        _liveMaterializedCount = LiveEditorPanel.Children.Count;
    }

    private void OnLiveTextBoxBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        if (_isSyncingLiveEditor || _isApplyingLiveSelection || !HasLiveMultiSelection)
            return;

        var oldText = sender.Text ?? string.Empty;
        var newText = args.NewText ?? string.Empty;
        var inserted = ExtractInsertedSegment(oldText, newText);
        args.Cancel = true;
        QueueLiveSelectionReplacement(inserted);
    }

    private void QueueLiveSelectionReplacement(string insertedText)
    {
        if (_pendingLiveReplaceText is null)
        {
            _pendingLiveReplaceText = insertedText;
            DispatcherQueue.TryEnqueue(() =>
            {
                var pending = _pendingLiveReplaceText ?? string.Empty;
                _pendingLiveReplaceText = null;
                ReplaceLiveSelection(pending);
            });
        }
        else
        {
            _pendingLiveReplaceText += insertedText;
        }
    }

    private static string ExtractInsertedSegment(string oldText, string newText)
    {
        var maxPrefix = Math.Min(oldText.Length, newText.Length);
        var prefix = 0;
        while (prefix < maxPrefix && oldText[prefix] == newText[prefix])
            prefix++;

        var maxSuffix = Math.Min(oldText.Length, newText.Length) - prefix;
        var suffix = 0;
        while (suffix < maxSuffix && oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix])
            suffix++;

        return newText.Substring(prefix, newText.Length - prefix - suffix);
    }

    private static string ClampedSlice(string? content, int start, int end)
    {
        var text = content ?? string.Empty;
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        return text[start..end];
    }
}
