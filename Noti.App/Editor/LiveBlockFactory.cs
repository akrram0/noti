using Microsoft.UI.Input;
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
        tb.Background = _cachedInputBgBrush;
        tb.RequestedTheme = ViewModel.CurrentElementTheme;
        tb.TextAlignment = block.BlockAlignment;
        tb.MinWidth = 0;
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

        var rawContent = block.Content ?? string.Empty;
        if (MarkdownRenderer.HasInlineMarkdownFormatting(rawContent))
        {
            var isSubLine = rawContent.Contains("<sub>", StringComparison.OrdinalIgnoreCase);
            var displayFontSize = isSubLine ? Math.Max(12, fontSize - 2) : fontSize;
            var displayForeground = isSubLine ? secondaryBrush : textForeground;

            var displayTb = MarkdownRenderer.CreateFormattedTextBlock(
                rawContent,
                displayFontSize,
                fontWeight,
                displayForeground,
                AccentBrush,
                MonoFontFamily,
                new Thickness(0, 2, 0, 2),
                _cachedSortedNoteTitles,
                title =>
                {
                    var target = ViewModel.FindNoteByMention(title);
                    if (target is not null)
                        NavigateToNote(target);
                });
            displayTb.FontStyle = fontStyle;
            displayTb.LineHeight = Math.Max(22, Math.Round(displayFontSize * 1.45));
            displayTb.VerticalAlignment = VerticalAlignment.Center;
            displayTb.TextAlignment = block.BlockAlignment;
            displayTb.HorizontalAlignment = block.HorizontalBlockAlignment == HorizontalAlignment.Left
                ? HorizontalAlignment.Stretch
                : block.HorizontalBlockAlignment;
            displayTb.ContextRequested += OnEditorContextRequested;

            displayTb.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
            {
                if (e.GetCurrentPoint(displayTb).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased ||
                    !string.IsNullOrEmpty(displayTb.SelectedText))
                {
                    return;
                }

                displayTb.Visibility = Visibility.Collapsed;
                tb.Visibility = Visibility.Visible;
                var idx = _liveBlocks.IndexOf(block);
                if (idx >= 0)
                    FocusLiveBlockAt(idx);
            }), true);

            block.DisplayTextBlock = displayTb;
            tb.Visibility = Visibility.Collapsed;
            cellGrid.Children.Add(displayTb);
        }

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
                if (block.DisplayTextBlock is { } dtb)
                    dtb.Visibility = Visibility.Collapsed;
                tb.Visibility = Visibility.Visible;
                ScheduleSmoothCaretUpdate(tb);
            }
        };

        tb.LostFocus += (_, _) =>
        {
            HideSmoothCaretOverlay();
            if (_isSyncingLiveEditor || tb.Tag is not LiveDocBlock block)
                return;

            if (MarkdownRenderer.HasInlineMarkdownFormatting(block.Content))
            {
                var idx = _liveBlocks.IndexOf(block);
                if (idx >= 0)
                    RefreshLiveBlockAt(idx);
            }
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
            _activeEditorAlignment = block.BlockAlignment;

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
                        BlockAlignment = block.BlockAlignment,
                        Content = nextTreeLine
                    }, focusCaret: nextTreeLine.Length);
                    FlushSyncLiveBlocksToViewModel();
                    return;
                }

                InsertLiveBlockAt(blockIdx + 1, new LiveDocBlock
                {
                    Kind = LiveBlockKind.Paragraph,
                    BlockAlignment = block.BlockAlignment,
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
                BlockAlignment = block.BlockAlignment,
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
