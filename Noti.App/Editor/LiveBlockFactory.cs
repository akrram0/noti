using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noti.App.ViewModels;
using Windows.System;
using Windows.UI.Core;

namespace Noti.App;

public sealed partial class MainWindow
{
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

        if (block.Kind == LiveBlockKind.HtmlWrapper)
        {
            return new Border
            {
                Height = 0,
                Visibility = Visibility.Collapsed
            };
        }

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

                    for (var next = idx + 1; next < _liveBlocks.Count; next++)
                    {
                        EnsureLiveBlocksMaterializedThrough(next);
                        if (IsEditableLiveBlock(_liveBlocks[next]))
                        {
                            FocusLiveBlockAt(next, 0);
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
            dividerContainer.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(dividerContainer).Properties.IsRightButtonPressed)
                    return;

                e.Handled = true;
                var savedOffset = LiveEditorScrollViewer.VerticalOffset;
                var idx = _liveBlocks.IndexOf(block);
                if (idx >= 0)
                    FocusOrInsertParagraphAfter(idx);
                if (savedOffset > 4.0 && LiveEditorScrollViewer.VerticalOffset < 1.0)
                    LiveEditorScrollViewer.ChangeView(null, savedOffset, null, true);
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
                onImageMutated: QueueSyncLiveBlocksToViewModel,
                onRequestClickBelow: () =>
                {
                    var idx = _liveBlocks.IndexOf(block);
                    if (idx >= 0)
                        FocusOrInsertParagraphAfter(idx);
                },
                onRequestDeleteImage: () =>
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
                        FocusOrInsertParagraphAfter(Math.Max(0, idx - 1));

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
                Background = _cachedInputBgBrush,
                RequestedTheme = ViewModel.CurrentElementTheme,
                SelectionHighlightColor = AccentBrush,
                SelectionHighlightColorWhenNotFocused = SelectionUnfocusedBrush,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                MinHeight = 28,
                Opacity = 1.0,
                ContextFlyout = null,
                SelectionFlyout = null,
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
            out var fontFamily,
            out var isTreeLine,
            out var textForeground,
            out var placeholder);

        var tb = RentLiveTextBox(block);
        tb.Text = block.Content ?? string.Empty;
        tb.PlaceholderText = placeholder;
        tb.TextWrapping = isTreeLine ? TextWrapping.NoWrap : TextWrapping.Wrap;
        tb.FontFamily = fontFamily;
        tb.FontSize = fontSize;
        tb.FontWeight = fontWeight;
        tb.FontStyle = fontStyle;
        tb.Foreground = textForeground;
        tb.Background = _cachedInputBgBrush;
        tb.RequestedTheme = ViewModel.CurrentElementTheme;
        tb.IsReadOnly = ViewModel.IsActiveNoteLocked;
        tb.TextAlignment = block.BlockAlignment;
        tb.MinWidth = 0;
        tb.MinHeight = Math.Max(26, Math.Round(fontSize * 1.6));
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

        if (block.InlineSpans.Any(s => s.IsBold) || block.IsBlockBold)
            tb.FontWeight = FontWeights.Bold;
        if (block.InlineSpans.Any(s => s.IsItalic) || block.IsBlockItalic)
            tb.FontStyle = Windows.UI.Text.FontStyle.Italic;
        if (block.IsBlockCode)
            tb.FontFamily = MonoFontFamily;

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
                HorizontalAlignment = block.HorizontalBlockAlignment,
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
                var isAligned = block.BlockAlignment != TextAlignment.Left;
                if (isAligned)
                {
                    tb.TextAlignment = TextAlignment.Left;
                    tb.MinWidth = 40;
                }

                var row = new Grid
                {
                    Background = TransparentBrush,
                    Margin = new Thickness(0, 2, 0, 2),
                    ColumnSpacing = 8,
                    HorizontalAlignment = isAligned ? block.HorizontalBlockAlignment : HorizontalAlignment.Stretch
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = isAligned ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });

                var checkBox = new CheckBox
                {
                    IsChecked = block.IsChecked,
                    MinWidth = 0,
                    Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkBox.Click += (_, _) =>
                {
                    if (ViewModel.IsActiveNoteLocked)
                    {
                        checkBox.IsChecked = block.IsChecked;
                        TriggerLockedNoteAccidentalEditFeedback();
                        return;
                    }
                    EnsureLiveRenderResources();
                    block.IsChecked = checkBox.IsChecked == true;
                    tb.Foreground = block.IsChecked ? _cachedSecondaryBrush : _cachedPrimaryBrush;
                    if (block.DisplayTextBlock is { } dtb)
                        dtb.Foreground = tb.Foreground;
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
                var isAligned = block.BlockAlignment != TextAlignment.Left;
                if (isAligned)
                {
                    tb.TextAlignment = TextAlignment.Left;
                    tb.MinWidth = 40;
                }

                var row = new Grid
                {
                    Background = TransparentBrush,
                    Margin = new Thickness(0, 2, 0, 2),
                    ColumnSpacing = 10,
                    HorizontalAlignment = isAligned ? block.HorizontalBlockAlignment : HorizontalAlignment.Stretch
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = isAligned ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });

                var dot = new TextBlock
                {
                    Text = "•",
                    FontFamily = BodyFontFamily,
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
            {
                _activeLiveBlock = block;
                if (!string.IsNullOrEmpty(block.Content) || block.BlockAlignment != TextAlignment.Left)
                    _activeEditorAlignment = block.BlockAlignment;
                ScheduleSmoothCaretUpdate(tb);
            }
        };

        tb.LostFocus += (_, _) =>
        {
            HideSmoothCaretOverlay();
            HideSelectionToolbar();
            if (_isSyncingLiveEditor || tb.Tag is not LiveDocBlock block)
                return;
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
            _isContextSlashMenuOpen = false;

            if (TryAutoConvertLiveBlock(block, tb))
                return;

            if (TryAutoConvertInlineMarkdown(block, tb))
                return;

            QueueSyncLiveBlocksToViewModel();
            UpdateSelectionToolbar();
            UpdateSlashMenuAutocomplete();
            UpdateMentionAutocomplete();
            UpdateActiveMentionJumpPill();
            ScheduleSmoothCaretUpdate(tb);

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
            ScheduleSmoothCaretUpdate(tb);
        };

        tb.PointerReleased += (_, _) =>
        {
            if (tb.Tag is not LiveDocBlock)
                return;

            var ctrlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            if (ctrlDown && TryGetMentionedNoteAtCaret(out var targetNote) && targetNote is not null)
                NavigateToNote(targetNote);
        };

        tb.ContextRequested += OnEditorContextRequested;
        tb.Paste += OnEditorTextBoxPaste;

        tb.PreviewKeyDown += (_, e) =>
        {
            if (tb.Tag is LiveDocBlock block)
                OnLiveBlockPreviewKeyDown(block, tb, e);
        };
    }

    private bool TryAutoConvertInlineMarkdown(LiveDocBlock block, TextBox tb)
    {
        var text = tb.Text ?? string.Empty;
        var caret = Math.Clamp(tb.SelectionStart, 0, text.Length);
        if (text.Length < 3 || caret < 2)
            return false;

        var before = text[..caret];

        // 1. Bold (**text** ) or (**text**)
        if (TryExtractInlineTrigger(before, "**", "**", out var boldStart, out var boldInner))
        {
            var replacement = boldInner;
            var endsWithSpace = before.EndsWith(' ');
            var newBefore = before[..boldStart] + replacement + (endsWithSpace ? " " : string.Empty);
            var after = text[caret..];
            var updated = newBefore + after;

            block.InlineSpans.Add(new LiveInlineSpan
            {
                Start = boldStart,
                Length = boldInner.Length,
                IsBold = true
            });
            if (updated.Trim() == boldInner.Trim())
                block.IsBlockBold = true;

            var newCaret = boldStart + replacement.Length + (endsWithSpace ? 1 : 0);
            block.Content = updated;
            tb.Text = updated;
            tb.FontWeight = FontWeights.Bold;
            tb.SelectionStart = Math.Clamp(newCaret, 0, updated.Length);
            tb.SelectionLength = 0;
            FlushSyncLiveBlocksToViewModel();
            return true;
        }

        // 2. Strikethrough (~~text~~ )
        if (TryExtractInlineTrigger(before, "~~", "~~", out var strikeStart, out var strikeInner))
        {
            var replacement = strikeInner;
            var endsWithSpace = before.EndsWith(' ');
            var newBefore = before[..strikeStart] + replacement + (endsWithSpace ? " " : string.Empty);
            var after = text[caret..];
            var updated = newBefore + after;

            block.InlineSpans.Add(new LiveInlineSpan
            {
                Start = strikeStart,
                Length = strikeInner.Length,
                IsStrikethrough = true
            });
            if (updated.Trim() == strikeInner.Trim())
                block.IsBlockStrikethrough = true;

            var newCaret = strikeStart + replacement.Length + (endsWithSpace ? 1 : 0);
            block.Content = updated;
            tb.Text = updated;
            tb.SelectionStart = Math.Clamp(newCaret, 0, updated.Length);
            tb.SelectionLength = 0;
            FlushSyncLiveBlocksToViewModel();
            RefreshLiveBlockAt(_liveBlocks.IndexOf(block), newCaret);
            return true;
        }

        // 3. Italic (*text* )
        if (TryExtractInlineTrigger(before, "*", "*", out var italicStart, out var italicInner))
        {
            var replacement = italicInner;
            var endsWithSpace = before.EndsWith(' ');
            var newBefore = before[..italicStart] + replacement + (endsWithSpace ? " " : string.Empty);
            var after = text[caret..];
            var updated = newBefore + after;

            block.InlineSpans.Add(new LiveInlineSpan
            {
                Start = italicStart,
                Length = italicInner.Length,
                IsItalic = true
            });
            if (updated.Trim() == italicInner.Trim())
                block.IsBlockItalic = true;

            var newCaret = italicStart + replacement.Length + (endsWithSpace ? 1 : 0);
            block.Content = updated;
            tb.Text = updated;
            tb.FontStyle = Windows.UI.Text.FontStyle.Italic;
            tb.SelectionStart = Math.Clamp(newCaret, 0, updated.Length);
            tb.SelectionLength = 0;
            FlushSyncLiveBlocksToViewModel();
            return true;
        }

        // 4. Code (`code` )
        if (TryExtractInlineTrigger(before, "`", "`", out var codeStart, out var codeInner))
        {
            var replacement = codeInner;
            var endsWithSpace = before.EndsWith(' ');
            var newBefore = before[..codeStart] + replacement + (endsWithSpace ? " " : string.Empty);
            var after = text[caret..];
            var updated = newBefore + after;

            block.InlineSpans.Add(new LiveInlineSpan
            {
                Start = codeStart,
                Length = codeInner.Length,
                IsCode = true
            });
            if (updated.Trim() == codeInner.Trim())
                block.IsBlockCode = true;

            var newCaret = codeStart + replacement.Length + (endsWithSpace ? 1 : 0);
            block.Content = updated;
            tb.Text = updated;
            tb.FontFamily = MonoFontFamily;
            tb.SelectionStart = Math.Clamp(newCaret, 0, updated.Length);
            tb.SelectionLength = 0;
            FlushSyncLiveBlocksToViewModel();
            return true;
        }

        return false;
    }

    private static bool TryExtractInlineTrigger(
        string beforeCaret,
        string openDelimiter,
        string closeDelimiter,
        out int triggerStart,
        out string innerText)
    {
        triggerStart = -1;
        innerText = string.Empty;

        var endsWithSpace = beforeCaret.EndsWith(' ');
        var trimmedBefore = endsWithSpace ? beforeCaret[..^1] : beforeCaret;

        if (!trimmedBefore.EndsWith(closeDelimiter, StringComparison.Ordinal))
            return false;

        var closeIndex = trimmedBefore.Length - closeDelimiter.Length;
        var openIndex = trimmedBefore.LastIndexOf(openDelimiter, closeIndex - 1, StringComparison.Ordinal);
        if (openIndex < 0 || openIndex >= closeIndex - 1)
            return false;

        if (openDelimiter == "*" && openIndex > 0 && trimmedBefore[openIndex - 1] == '*')
            return false;
        if (openDelimiter == "**" && openIndex > 0 && trimmedBefore[openIndex - 1] == '*')
            return false;

        var candidate = trimmedBefore[(openIndex + openDelimiter.Length)..closeIndex];
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        triggerStart = openIndex;
        innerText = candidate;
        return true;
    }

    private static bool IsStartOfLineCheckboxTrigger(string text, out bool isChecked)
    {
        isChecked = false;
        if (string.IsNullOrEmpty(text))
            return false;

        // Strictly matches: ^- \[[ xX]?\] $ or ^\[[ xX]?\] $ (at index 0, entire text is the trigger + trailing space)
        if (text is "- [ ] " or "- [] " or "[ ] " or "[] ")
        {
            isChecked = false;
            return true;
        }

        if (text is "- [x] " or "- [X] " or "[x] " or "[X] ")
        {
            isChecked = true;
            return true;
        }

        return false;
    }

    private bool TryAutoConvertLiveBlock(LiveDocBlock block, TextBox tb)
    {
        var idx = _liveBlocks.IndexOf(block);
        if (idx < 0)
        {
            idx = _liveBlocks.FindIndex(b => ReferenceEquals(b.PrimaryTextBox, tb));
            if (idx >= 0)
            {
                block = _liveBlocks[idx];
                tb.Tag = block;
            }
            else
            {
                return false;
            }
        }

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
            if (IsStartOfLineCheckboxTrigger(text, out var isChecked))
            {
                block.Kind = LiveBlockKind.Checklist;
                block.IsChecked = isChecked;
                block.Content = string.Empty;
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
                FocusOrInsertParagraphAfter(idx);
                FlushSyncLiveBlocksToViewModel();
                return true;
            }
            if (MarkdownRenderer.TryParseImageLine(text, out var parsedSrc, out _, out _, out _) &&
                MarkdownRenderer.TryResolveImageUri(parsedSrc, ViewModel.SelectedNote?.FolderPath, ViewModel.VaultDirectoryPath, out _))
            {
                block.Kind = LiveBlockKind.ImageBlock;
                block.Content = text;
                RefreshLiveBlockAt(idx);
                FocusOrInsertParagraphAfter(idx);
                FlushSyncLiveBlocksToViewModel();
                return true;
            }
        }

        if (block.Kind == LiveBlockKind.Bullet && (text is "[ ] " or "[] " or "[x] " or "[X] "))
        {
            block.Kind = LiveBlockKind.Checklist;
            block.IsChecked = text.Contains('x', StringComparison.OrdinalIgnoreCase);
            block.Content = string.Empty;
            FlushSyncLiveBlocksToViewModel();
            RefreshLiveBlockAt(idx, 0);
            return true;
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
        {
            blockIdx = _liveBlocks.FindIndex(b => ReferenceEquals(b.PrimaryTextBox, tb));
            if (blockIdx >= 0)
            {
                block = _liveBlocks[blockIdx];
                tb.Tag = block;
            }
            else
            {
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
            if (shiftDown && e.Key == VirtualKey.X)
            {
                e.Handled = true;
                ApplyLiveInlineFormat(block, tb, "~~", "~~", toggleBlockStrikethrough: true);
                return;
            }

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
                case VirtualKey.U:
                    e.Handled = true;
                    ApplyLiveInlineFormat(block, tb, "<u>", "</u>");
                    return;
                case VirtualKey.K:
                    e.Handled = true;
                    OnSelectionLinkClick(this, new RoutedEventArgs());
                    return;
                case VirtualKey.L:
                    e.Handled = true;
                    ViewModel.ToggleActiveNoteLock();
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
            _activeEditorAlignment = block.BlockAlignment;

            if (shiftDown)
            {
                // Shift+Enter: soft newline inside the current block without splitting
                var curText = tb.Text ?? string.Empty;
                var curCaret = Math.Clamp(tb.SelectionStart, 0, curText.Length);
                var curSelLen = Math.Clamp(tb.SelectionLength, 0, curText.Length - curCaret);
                var updated = curText[..curCaret] + "\n" + curText[(curCaret + curSelLen)..];
                block.Content = updated;
                tb.Text = updated;
                tb.SelectionStart = curCaret + 1;
                tb.SelectionLength = 0;
                ScheduleSmoothCaretUpdate(tb);
                QueueSyncLiveBlocksToViewModel();
                return;
            }

            if (block.Kind == LiveBlockKind.Paragraph &&
                MarkdownRenderer.TryParseImageLine(text.Trim(), out _, out _, out _, out _))
            {
                block.Kind = LiveBlockKind.ImageBlock;
                block.Content = text;
                RefreshLiveBlockAt(blockIdx);
                InsertLiveBlockAt(blockIdx + 1, new LiveDocBlock
                {
                    Kind = LiveBlockKind.Paragraph,
                    BlockAlignment = block.BlockAlignment
                }, focusCaret: 0);
                FlushSyncLiveBlocksToViewModel();
                return;
            }

            // 2. Checklist Behavior & Enter Continuation:
            if (block.Kind == LiveBlockKind.Checklist)
            {
                // If empty checkbox: revert to normal paragraph (exit checklist mode)
                if (string.IsNullOrWhiteSpace(text))
                {
                    block.Kind = LiveBlockKind.Paragraph;
                    block.IsChecked = false;
                    block.Content = string.Empty;
                    RefreshLiveBlockAt(blockIdx, 0);
                    FlushSyncLiveBlocksToViewModel();
                    return;
                }

                // If has text: split into headText and tailText, and create a NEW checklist block item below
                var head = text[..caret];
                var selLen = Math.Clamp(tb.SelectionLength, 0, text.Length - caret);
                var tail = (caret + selLen < text.Length) ? text[(caret + selLen)..] : string.Empty;

                block.Content = head;
                if (tb.Text != head)
                    tb.Text = head;

                var newChecklist = new LiveDocBlock
                {
                    Kind = LiveBlockKind.Checklist,
                    IsChecked = false,
                    BlockAlignment = block.BlockAlignment,
                    Content = tail
                };
                InsertLiveBlockAt(blockIdx + 1, newChecklist, focusCaret: 0);
                FlushSyncLiveBlocksToViewModel();
                return;
            }

            // Bullet continuation or exit if empty:
            if (block.Kind == LiveBlockKind.Bullet)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    block.Kind = LiveBlockKind.Paragraph;
                    block.Content = string.Empty;
                    RefreshLiveBlockAt(blockIdx, 0);
                    FlushSyncLiveBlocksToViewModel();
                    return;
                }

                var head = text[..caret];
                var selLen = Math.Clamp(tb.SelectionLength, 0, text.Length - caret);
                var tail = (caret + selLen < text.Length) ? text[(caret + selLen)..] : string.Empty;

                block.Content = head;
                if (tb.Text != head)
                    tb.Text = head;

                var newBullet = new LiveDocBlock
                {
                    Kind = LiveBlockKind.Bullet,
                    BlockAlignment = block.BlockAlignment,
                    Content = tail
                };
                InsertLiveBlockAt(blockIdx + 1, newBullet, focusCaret: 0);
                FlushSyncLiveBlocksToViewModel();
                return;
            }

            // Quote exit if empty:
            if (block.Kind == LiveBlockKind.Quote && string.IsNullOrWhiteSpace(text))
            {
                block.Kind = LiveBlockKind.Paragraph;
                block.Content = string.Empty;
                RefreshLiveBlockAt(blockIdx, 0);
                FlushSyncLiveBlocksToViewModel();
                return;
            }

            // 1. Robust Block Splitting (Paragraphs, Headings, Quotes with content):
            var headText = text[..caret];
            var selectionLength = Math.Clamp(tb.SelectionLength, 0, text.Length - caret);
            var tailText = (caret + selectionLength < text.Length) ? text[(caret + selectionLength)..] : string.Empty;

            block.Content = headText;
            if (tb.Text != headText)
                tb.Text = headText;

            var newParagraphBlock = new LiveDocBlock
            {
                Kind = LiveBlockKind.Paragraph,
                BlockAlignment = block.BlockAlignment,
                Content = tailText
            };

            InsertLiveBlockAt(blockIdx + 1, newParagraphBlock, focusCaret: 0);
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
                if (prevBlock.Kind is LiveBlockKind.Divider or LiveBlockKind.ImageBlock or LiveBlockKind.HtmlWrapper)
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
            HideSlashMenuPopup();
            HideMentionPopup();
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
}
