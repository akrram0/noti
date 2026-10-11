using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Noti.App.ViewModels;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace Noti.App;

public sealed partial class MainWindow : Window
{
    private const string BranchMid = "├─ ";
    private const string BranchEnd = "└── ";
    private const string BranchPipe = "│";

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;

        InitializeComponent();
        ConfigureWindowChrome();

        RootGrid.Loaded += (_, _) => UpdateTitleBarPassthrough();
        RootGrid.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
        SidebarToggleButton.SizeChanged += (_, _) => UpdateTitleBarPassthrough();

        EditorTextBox.PreviewKeyDown += OnEditorPreviewKeyDown;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public MainViewModel ViewModel { get; }

    private void ConfigureWindowChrome()
    {
        AppWindow.Resize(new SizeInt32(1040, 640));

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

    private void OnEditorPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.IsFluidAnimationEnabled && e.Key != VirtualKey.Shift && e.Key != VirtualKey.Control && e.Key != VirtualKey.Menu)
            PlayFluidTypingPulse();

        if (e.Key != VirtualKey.Enter || !ViewModel.IsTreeShortcutEnabled)
            return;

        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        var isShiftDown = shiftState.HasFlag(CoreVirtualKeyStates.Down);

        var text = EditorTextBox.Text ?? string.Empty;
        var caret = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
        var lineStart = FindLineStart(text, caret);
        var lineEnd = FindLineEnd(text, caret);
        var currentLine = text[lineStart..lineEnd];

        if (isShiftDown)
        {
            e.Handled = true;

            if (currentLine.StartsWith(BranchEnd, StringComparison.Ordinal))
            {
                var updated = string.Concat(
                    text.AsSpan(0, lineStart),
                    BranchMid,
                    text.AsSpan(lineStart + BranchEnd.Length, caret - (lineStart + BranchEnd.Length)),
                    "\r",
                    BranchPipe,
                    "\r",
                    BranchEnd,
                    text.AsSpan(caret));

                var newCaret = caret - BranchEnd.Length + BranchMid.Length + 1 + BranchPipe.Length + 1 + BranchEnd.Length;
                ApplyEditorText(updated, newCaret);
            }
            else if (currentLine.StartsWith(BranchMid, StringComparison.Ordinal))
            {
                var insert = $"\r{BranchPipe}\r{BranchEnd}";
                var updated = text.Insert(caret, insert);
                ApplyEditorText(updated, caret + insert.Length);
            }
            else
            {
                var insert = $"\r{BranchEnd}";
                var updated = text.Insert(caret, insert);
                ApplyEditorText(updated, caret + insert.Length);
            }
        }
        else if (currentLine == BranchEnd.TrimEnd() || currentLine == BranchEnd)
        {
            e.Handled = true;
            var removeStart = lineStart;
            var pipePrefix = $"{BranchPipe}\r";
            if (lineStart >= pipePrefix.Length && text.AsSpan(lineStart - pipePrefix.Length, pipePrefix.Length).SequenceEqual(pipePrefix))
                removeStart -= pipePrefix.Length;

            var updated = text.Remove(removeStart, lineEnd - removeStart);
            ApplyEditorText(updated, removeStart);
        }
    }

    private void ApplyEditorText(string updatedText, int newCaretIndex)
    {
        EditorTextBox.Text = updatedText;
        ViewModel.EditorText = updatedText;
        EditorTextBox.SelectionStart = Math.Clamp(newCaretIndex, 0, updatedText.Length);
        EditorTextBox.SelectionLength = 0;
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ViewModel.IsFluidAnimationEnabled)
            return;

        if (e.PropertyName == nameof(MainViewModel.SelectedNote))
            PlayFluidTransition(EditorTextBox, EditorTranslate, 5);
        else if (e.PropertyName == nameof(MainViewModel.IsSettingsOpen) && ViewModel.IsSettingsOpen)
            PlayFluidTransition(SettingsCardBorder, SettingsTranslate, 8);
    }

    private void PlayFluidTypingPulse()
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var barOpacity = new DoubleAnimation
        {
            From = 0.75,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(320)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(barOpacity, FluidTypingBar);
        Storyboard.SetTargetProperty(barOpacity, "Opacity");

        var barScale = new DoubleAnimation
        {
            From = 0.25,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(260)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(barScale, FluidBarScale);
        Storyboard.SetTargetProperty(barScale, "ScaleX");

        var wordSlide = new DoubleAnimation
        {
            From = 1.5,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(wordSlide, WordCountTranslate);
        Storyboard.SetTargetProperty(wordSlide, "Y");

        sb.Children.Add(barOpacity);
        sb.Children.Add(barScale);
        sb.Children.Add(wordSlide);
        sb.Begin();
    }

    private static void PlayFluidTransition(UIElement element, Microsoft.UI.Xaml.Media.TranslateTransform translate, double offsetY)
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation
        {
            From = 0.35,
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

    private void UpdateTitleBarPassthrough()
    {
        if (RootGrid.XamlRoot is null || SidebarToggleButton.ActualWidth <= 0)
            return;

        var scale = RootGrid.XamlRoot.RasterizationScale;
        var transform = SidebarToggleButton.TransformToVisual(null);
        var bounds = transform.TransformBounds(new Rect(0, 0, SidebarToggleButton.ActualWidth, SidebarToggleButton.ActualHeight));

        var rect = new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));

        var nonClientSource = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        nonClientSource.SetRegionRects(NonClientRegionKind.Passthrough, [rect]);
    }
}
