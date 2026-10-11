using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;

        InitializeComponent();
        ConfigureWindowChrome();

        RootGrid.Loaded += (_, _) =>
        {
            UpdateTitleBarPassthrough();
            RefreshMarkdownPreview();
        };
        RootGrid.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
        SidebarToggleButton.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
        ViewSwitcherBorder.SizeChanged += (_, _) => UpdateTitleBarPassthrough();

        RootGrid.PreviewKeyDown += OnRootPreviewKeyDown;
        EditorTextBox.PreviewKeyDown += OnEditorPreviewKeyDown;
        EditorTextBox.TextChanged += OnEditorTextChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public MainViewModel ViewModel { get; }

    private void ConfigureWindowChrome()
    {
        AppWindow.Resize(new SizeInt32(1100, 680));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Noti.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

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

    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
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
                _ = PickAndExportMarkdownAsync();
                break;

            case VirtualKey.S:
                e.Handled = true;
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
                case VirtualKey.K:
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

        // Tab / Shift+Tab tree branch indentation
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
        if ( HandleTreeEnterKey(fullText, caretPos, shiftDown, out var newText, out var newCaret))
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
                // Pressing Shift+Enter on an empty branch closes the tree cleanly instead of stacking blank branches
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

            // Starting a new tree from a plain sentence
            var firstBranch = $"\r{BranchEnd}";
            updatedText = text.Insert(caret, firstBranch);
            newCaret = caret + firstBranch.Length;
            return true;
        }

        // Plain Enter on an empty tree branch exits tree mode cleanly
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

        // If the previous line was a sibling "├─ ", convert it back to "└── " so the tree has a closed bottom corner
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

    private void WrapEditorSelection(string prefix, string suffix)
    {
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
        if (ViewModel.CurrentViewMode != EditorViewMode.Write)
            RefreshMarkdownPreview();

        if (!ViewModel.IsFluidAnimationEnabled || EditorTextBox.FocusState == FocusState.Unfocused)
            return;

        var text = EditorTextBox.Text ?? string.Empty;
        var caret = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
        var activeWord = ExtractCurrentWord(text, caret);
        TriggerWordTypingAnimation(activeWord);
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
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // Subtle vertical micro-spring on the editor text and word count
        var microSb = new Storyboard();
        var editorSpring = new DoubleAnimation
        {
            From = 1.2,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(110)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(editorSpring, EditorTranslate);
        Storyboard.SetTargetProperty(editorSpring, "Y");

        var countSpring = new DoubleAnimation
        {
            From = 2.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(140)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(countSpring, WordCountTranslate);
        Storyboard.SetTargetProperty(countSpring, "Y");

        microSb.Children.Add(editorSpring);
        microSb.Children.Add(countSpring);
        microSb.Begin();

        if (string.IsNullOrWhiteSpace(activeWord))
            return;

        TypingWordText.Text = activeWord;
        _typingWordStoryboard?.Stop();

        var wordSb = new Storyboard();

        var scaleX = new DoubleAnimation
        {
            From = 0.86,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(scaleX, TypingWordScale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");

        var scaleY = new DoubleAnimation
        {
            From = 0.86,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(scaleY, TypingWordScale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");

        var slideY = new DoubleAnimation
        {
            From = 5.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(slideY, TypingWordTranslate);
        Storyboard.SetTargetProperty(slideY, "Y");

        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)),
            Value = 0.35
        });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)),
            Value = 0.95,
            EasingFunction = ease
        });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)),
            Value = 0.95
        });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650)),
            Value = 0.0,
            EasingFunction = ease
        });
        Storyboard.SetTarget(fade, TypingWordPopupBorder);
        Storyboard.SetTargetProperty(fade, "Opacity");

        wordSb.Children.Add(scaleX);
        wordSb.Children.Add(scaleY);
        wordSb.Children.Add(slideY);
        wordSb.Children.Add(fade);

        _typingWordStoryboard = wordSb;
        wordSb.Begin();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.SelectedNote):
                RefreshMarkdownPreview();
                if (ViewModel.IsFluidAnimationEnabled)
                {
                    PlayFluidTransition(EditorTextBox, EditorTranslate, 6);
                    PlayFluidTransition(MarkdownPreviewPanel, PreviewTranslate, 6);
                }
                break;

            case nameof(MainViewModel.CurrentViewMode):
                RefreshMarkdownPreview();
                AnimateViewModeChange();
                break;

            case nameof(MainViewModel.IsSidebarOpen):
                AnimateSidebar(ViewModel.IsSidebarOpen);
                break;

            case nameof(MainViewModel.IsSettingsOpen):
                AnimateSettingsModal(ViewModel.IsSettingsOpen);
                break;

            case nameof(MainViewModel.IsDarkMode):
            case nameof(MainViewModel.EditorFontSize):
                RefreshMarkdownPreview();
                break;
        }
    }

    private void RefreshMarkdownPreview()
    {
        if (ViewModel.CurrentViewMode == EditorViewMode.Write)
            return;

        MarkdownRenderer.Render(
            MarkdownPreviewPanel,
            ViewModel.EditorText,
            ViewModel.IsDarkMode,
            ViewModel.EditorFontSize,
            OnPreviewChecklistToggled);
    }

    private void OnPreviewChecklistToggled(int lineIndex, bool isChecked)
    {
        var lines = (ViewModel.EditorText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lineIndex < 0 || lineIndex >= lines.Length)
            return;

        var line = lines[lineIndex];
        var idx = line.IndexOf("- [", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0 && idx + 5 <= line.Length)
        {
            lines[lineIndex] = line[..(idx + 3)] + (isChecked ? "x" : " ") + line[(idx + 4)..];
            var updated = string.Join("\r", lines);
            EditorTextBox.Text = updated;
            ViewModel.EditorText = updated;
            RefreshMarkdownPreview();
        }
    }

    private void AnimateSidebar(bool open)
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var widthAnim = new DoubleAnimation
        {
            From = SidebarContainer.ActualWidth,
            To = open ? 280.0 : 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = ease,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(widthAnim, SidebarContainer);
        Storyboard.SetTargetProperty(widthAnim, "Width");

        var slideAnim = new DoubleAnimation
        {
            From = open ? -32.0 : 0.0,
            To = open ? 0.0 : -32.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(slideAnim, SidebarTranslate);
        Storyboard.SetTargetProperty(slideAnim, "X");

        var fadeAnim = new DoubleAnimation
        {
            From = open ? 0.0 : 1.0,
            To = open ? 1.0 : 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(fadeAnim, SidebarContainer);
        Storyboard.SetTargetProperty(fadeAnim, "Opacity");

        sb.Children.Add(widthAnim);
        sb.Children.Add(slideAnim);
        sb.Children.Add(fadeAnim);
        sb.Begin();
    }

    private void AnimateViewModeChange()
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var pillSlide = new DoubleAnimation
        {
            To = ViewModel.ModeIndicatorOffsetX,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(pillSlide, ModeIndicatorTranslate);
        Storyboard.SetTargetProperty(pillSlide, "X");
        sb.Children.Add(pillSlide);
        sb.Begin();

        if (ViewModel.EditorPaneVisibility == Visibility.Visible)
            PlayFluidTransition(EditorTextBox, EditorTranslate, 6);
        if (ViewModel.PreviewPaneVisibility == Visibility.Visible)
            PlayFluidTransition(MarkdownPreviewPanel, PreviewTranslate, 6);
    }

    private void AnimateSettingsModal(bool open)
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (open)
            SettingsOverlayGrid.Visibility = Visibility.Visible;

        var backdropFade = new DoubleAnimation
        {
            From = open ? 0.0 : 1.0,
            To = open ? 1.0 : 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(open ? 200 : 150)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(backdropFade, SettingsOverlayGrid);
        Storyboard.SetTargetProperty(backdropFade, "Opacity");

        var scaleX = new DoubleAnimation
        {
            From = open ? 0.92 : 1.0,
            To = open ? 1.0 : 0.94,
            Duration = new Duration(TimeSpan.FromMilliseconds(open ? 220 : 150)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(scaleX, SettingsModalScale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");

        var scaleY = new DoubleAnimation
        {
            From = open ? 0.92 : 1.0,
            To = open ? 1.0 : 0.94,
            Duration = new Duration(TimeSpan.FromMilliseconds(open ? 220 : 150)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(scaleY, SettingsModalScale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");

        var slideY = new DoubleAnimation
        {
            From = open ? 16.0 : 0.0,
            To = open ? 0.0 : 12.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(open ? 220 : 150)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(slideY, SettingsModalTranslate);
        Storyboard.SetTargetProperty(slideY, "Y");

        sb.Children.Add(backdropFade);
        sb.Children.Add(scaleX);
        sb.Children.Add(scaleY);
        sb.Children.Add(slideY);

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

    private static void PlayFluidTransition(UIElement element, Microsoft.UI.Xaml.Media.TranslateTransform translate, double offsetY)
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation
        {
            From = 0.3,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation
        {
            From = offsetY,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(slide, translate);
        Storyboard.SetTargetProperty(slide, "Y");

        sb.Children.Add(fade);
        sb.Children.Add(slide);
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

    private void OnCreateNoteInFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folderPath })
            _ = ViewModel.CreateNoteInFolderAsync(folderPath);
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

    private void OnSettingsBackdropPressed(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.IsSettingsOpen = false;
    }

    private void OnSettingsCardPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnImportMarkdownClick(object sender, RoutedEventArgs e) =>
        _ = PickAndImportMarkdownAsync();

    private void OnExportMarkdownClick(object sender, RoutedEventArgs e) =>
        _ = PickAndExportMarkdownAsync();

    private void OnChangeVaultFolderClick(object sender, RoutedEventArgs e) =>
        _ = PickVaultFolderAsync();

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
            AddElementRect(ImportButton);
            AddElementRect(ExportButton);

            nonClientSource.SetRegionRects(NonClientRegionKind.Passthrough, regions.ToArray());
        }
        catch
        {
            // Ignore passthrough registration errors during initial window teardown/creation
        }
    }
}
