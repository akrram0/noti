using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
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

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        ViewModel.RequestExportMarkdown = () => _ = PickAndExportMarkdownAsync();

        InitializeComponent();
        ConfigureWindowChrome();

        RootGrid.Loaded += (_, _) =>
        {
            ApplyResponsiveLayout();
            ThemeIndicatorTranslate.X = ViewModel.ThemeIndicatorOffsetX;
            UpdateTitleBarPassthrough();
            RefreshMarkdownPreview();
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

        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };

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

    private void ApplyResponsiveLayout()
    {
        var width = RootGrid.ActualWidth;
        if (width <= 0)
            return;

        var isCompact = width < 860;
        var horizPad = isCompact ? 20.0 : 44.0;
        var vertPad = isCompact ? 18.0 : 28.0;

        EditorTextBox.Padding = new Thickness(horizPad, vertPad, horizPad, 16);
        MarkdownPreviewPanel.Padding = new Thickness(horizPad, vertPad, horizPad, 20);
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
            case VirtualKey.P:
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
                RefreshMarkdownPreview();
                if (ViewModel.IsFluidAnimationEnabled)
                {
                    PlayFluidTransition(EditorTextBox);
                    PlayFluidTransition(MarkdownPreviewPanel);
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

            case nameof(MainViewModel.CurrentThemeMode):
                AnimateThemeModePill();
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

        if (ViewModel.EditorPaneVisibility == Visibility.Visible)
            PlayFluidTransition(EditorTextBox);
        if (ViewModel.PreviewPaneVisibility == Visibility.Visible)
            PlayFluidTransition(MarkdownPreviewPanel);
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

    private void AnimateSettingsModal(bool open)
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var targetOpacity = open ? 1.0 : 0.0;
        var targetAngle = open ? 180.0 : 0.0;

        if (open)
        {
            ThemeIndicatorTranslate.X = ViewModel.ThemeIndicatorOffsetX;
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
        if (e.NewValue >= 30.0)
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
                        EditorTextBox.Focus(FocusState.Programmatic);
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
        if (item.TargetNote is not null)
        {
            ViewModel.SelectedNote = item.TargetNote;
            ViewModel.IsSettingsOpen = false;
            EditorTextBox.Focus(FocusState.Programmatic);
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
            From = 0.35,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
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
            ViewModel.ToggleFolderExpansion(item.FolderPath);
        else
            ViewModel.SelectedNote = item;
    }

    private void OnSidebarRowPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid { Children.Count: > 0 } grid && grid.Children[0] is Border hoverBorder)
        {
            var isAlreadySelected = grid.DataContext is NoteItemViewModel { IsFolder: false, IsSelected: true };
            hoverBorder.Opacity = isAlreadySelected ? 0.0 : 1.0;
        }
    }

    private void OnSidebarRowPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid { Children.Count: > 0 } grid && grid.Children[0] is Border hoverBorder)
            hoverBorder.Opacity = 0.0;
    }

    private void OnCreateNoteInFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folderPath })
            _ = ViewModel.CreateNoteInFolderAsync(folderPath);
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
