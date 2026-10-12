using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Noti.App.Styles;
using Noti.App.ViewModels;
using Windows.Foundation;

namespace Noti.App;

public sealed partial class MainWindow
{
    private void OnSidebarRowRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: NoteItemViewModel item })
            return;

        e.Handled = true;
        ShowSidebarContextPopup(item, e.GetPosition(RootGrid));
    }

    private void OnSidebarContextBackdropPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        HideSidebarContextPopup();
    }

    private void OnSidebarContextBackdropRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        HideSidebarContextPopup();
    }

    private void OnSidebarContextCardPointerPressed(object sender, PointerRoutedEventArgs e) =>
        e.Handled = true;

    private void HideSidebarContextPopup()
    {
        _sidebarContextStoryboard?.Stop();
        if (SidebarContextOverlay.Visibility != Visibility.Collapsed)
            SidebarContextOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowSidebarContextPopup(NoteItemViewModel item, Point cursorInRoot)
    {
        EnsureLiveRenderResources();
        SidebarContextContentPanel.Children.Clear();

        var headerRow = new Grid
        {
            Padding = new Thickness(10, 6, 10, 4),
            ColumnSpacing = 12
        };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var headerIcon = new FontIcon
        {
            Glyph = item.IsFolder ? "\uE8B7" : "\uE8A5",
            FontSize = 12,
            Foreground = item.HasCustomColor ? item.AccentColorBrush : _cachedSecondaryBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var headerTitle = new TextBlock
        {
            Text = item.Title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = _cachedPrimaryBrush,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headerTitle, 1);
        headerRow.Children.Add(headerIcon);
        headerRow.Children.Add(headerTitle);
        SidebarContextContentPanel.Children.Add(headerRow);

        var swatchRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(12, 10, 12, 10)
        };

        Application.Current.Resources.TryGetValue("ColorSwatchButtonStyle", out var swatchStyleObj);
        var swatchStyle = swatchStyleObj as Style;

        foreach (var (name, hex) in ThemePalette.SwatchColors)
        {
            var color = NoteItemViewModel.ParseHexColor(hex, Colors.DodgerBlue);
            var isCurrent = string.Equals(item.ColorHex, hex, StringComparison.OrdinalIgnoreCase);
            var checkColor = ThemePalette.GetContrastingCheckmarkColor(color);

            var swatchBtn = new Button
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(115, color.R, color.G, color.B)),
                Foreground = new SolidColorBrush(checkColor)
            };
            if (swatchStyle is not null)
                swatchBtn.Style = swatchStyle;

            if (isCurrent)
            {
                swatchBtn.Content = new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(checkColor)
                };
            }

            ToolTipService.SetToolTip(swatchBtn, isCurrent ? $"{name} (Click to clear)" : name);

            var capturedHex = hex;
            var capturedIsCurrent = isCurrent;
            swatchBtn.Click += (_, _) =>
            {
                ViewModel.SetItemColor(item, capturedIsCurrent ? null : capturedHex);
                HideSidebarContextPopup();
            };

            swatchRow.Children.Add(swatchBtn);
        }

        SidebarContextContentPanel.Children.Add(swatchRow);
        SidebarContextContentPanel.Children.Add(CreateSidebarContextDivider());

        if (item.HasCustomColor)
        {
            SidebarContextContentPanel.Children.Add(CreateSidebarContextActionButton(
                "\uE711",
                "Reset color",
                null,
                isDestructive: false,
                () =>
                {
                    HideSidebarContextPopup();
                    ViewModel.SetItemColor(item, null);
                }));
        }

        if (item.IsFolder)
        {
            SidebarContextContentPanel.Children.Add(CreateSidebarContextActionButton(
                "\uE710",
                "New note in folder",
                null,
                isDestructive: false,
                () =>
                {
                    HideSidebarContextPopup();
                    _ = ViewModel.CreateNoteInFolderAsync(item.FolderPath);
                }));

            SidebarContextContentPanel.Children.Add(CreateSidebarContextActionButton(
                "\uE8F4",
                "New subfolder",
                null,
                isDestructive: false,
                () =>
                {
                    HideSidebarContextPopup();
                    if (!string.IsNullOrWhiteSpace(item.FolderPath))
                    {
                        ViewModel.StartNewSubfolder(item.FolderPath);
                        NewFolderTextBox.Focus(FocusState.Programmatic);
                    }
                }));

            SidebarContextContentPanel.Children.Add(CreateSidebarContextActionButton(
                item.IsExpanded ? "\uE70D" : "\uE76C",
                item.IsExpanded ? "Collapse folder" : "Expand folder",
                null,
                isDestructive: false,
                () =>
                {
                    HideSidebarContextPopup();
                    ViewModel.ToggleFolderExpansion(item.FolderPath);
                }));

            SidebarContextContentPanel.Children.Add(CreateSidebarContextDivider());

            SidebarContextContentPanel.Children.Add(CreateSidebarContextActionButton(
                "\uE74D",
                "Delete folder",
                null,
                isDestructive: true,
                () =>
                {
                    HideSidebarContextPopup();
                    if (!string.IsNullOrWhiteSpace(item.FolderPath))
                    {
                        _pendingDeleteFolderPath = item.FolderPath;
                        DeleteFolderConfirmTitle.Text = $"Delete \"{item.FolderPath}\"?";
                        ShowDeleteFolderOverlay(open: true);
                    }
                }));
        }
        else
        {
            SidebarContextContentPanel.Children.Add(CreateSidebarContextActionButton(
                "\uE718",
                item.IsPinned ? "Unpin from top" : "Pin to top",
                null,
                isDestructive: false,
                () =>
                {
                    HideSidebarContextPopup();
                    ViewModel.TogglePinNote(item);
                }));

            SidebarContextContentPanel.Children.Add(CreateSidebarContextActionButton(
                "\uEDE1",
                "Export Markdown (.md)",
                "Ctrl+Shift+S",
                isDestructive: false,
                () =>
                {
                    HideSidebarContextPopup();
                    if (_liveSyncTimer.IsEnabled)
                        FlushSyncLiveBlocksToViewModel();
                    ViewModel.SelectedNote = item;
                    _ = PickAndExportMarkdownAsync();
                }));

            SidebarContextContentPanel.Children.Add(CreateSidebarContextDivider());

            SidebarContextContentPanel.Children.Add(CreateSidebarContextActionButton(
                "\uE74D",
                "Delete note",
                "Ctrl+Del",
                isDestructive: true,
                () =>
                {
                    HideSidebarContextPopup();
                    _ = ViewModel.DeleteNoteAsync(item);
                }));
        }

        var maxW = Math.Max(320.0, RootGrid.ActualWidth);
        var maxH = Math.Max(320.0, RootGrid.ActualHeight);
        var clampedLeft = Math.Clamp(cursorInRoot.X, 12.0, Math.Max(12.0, maxW - 276.0));
        var clampedTop = Math.Clamp(cursorInRoot.Y, 44.0, Math.Max(44.0, maxH - 260.0));
        SidebarContextPopupBorder.Margin = new Thickness(clampedLeft, clampedTop, 0, 0);

        SidebarContextOverlay.Visibility = Visibility.Visible;

        _sidebarContextStoryboard?.Stop();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb = new Storyboard();

        var fade = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(140)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(fade, SidebarContextPopupBorder);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var scaleX = new DoubleAnimation
        {
            From = 0.94,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(155)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(scaleX, SidebarContextPopupScale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");

        var scaleY = new DoubleAnimation
        {
            From = 0.94,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(155)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(scaleY, SidebarContextPopupScale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");

        var slideY = new DoubleAnimation
        {
            From = -5.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(155)),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(slideY, SidebarContextPopupTranslate);
        Storyboard.SetTargetProperty(slideY, "Y");

        SidebarContextPopupBorder.Opacity = 1.0;
        SidebarContextPopupScale.ScaleX = 1.0;
        SidebarContextPopupScale.ScaleY = 1.0;
        SidebarContextPopupTranslate.Y = 0.0;

        sb.Children.Add(fade);
        sb.Children.Add(scaleX);
        sb.Children.Add(scaleY);
        sb.Children.Add(slideY);

        _sidebarContextStoryboard = sb;
        sb.Begin();
    }

    private Border CreateSidebarContextDivider() =>
        new()
        {
            Height = 1,
            Margin = new Thickness(6, 4, 6, 4),
            Background = _cachedBorderBrush
        };

    private Button CreateSidebarContextActionButton(
        string glyph,
        string label,
        string? shortcut,
        bool isDestructive,
        Action onClick)
    {
        var fgBrush = isDestructive
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 255, 69, 58))
            : _cachedPrimaryBrush;

        var rowGrid = new Grid
        {
            ColumnSpacing = 12
        };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 13,
            Foreground = isDestructive ? fgBrush : _cachedSecondaryBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleBlock = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = fgBrush,
            VerticalAlignment = VerticalAlignment.Center,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(titleBlock, 1);

        rowGrid.Children.Add(icon);
        rowGrid.Children.Add(titleBlock);

        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            var shortcutBlock = new TextBlock
            {
                Text = shortcut,
                FontSize = 11,
                FontFamily = MonoFontFamily,
                Foreground = _cachedSecondaryBrush,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(shortcutBlock, 2);
            rowGrid.Children.Add(shortcutBlock);
        }

        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 36,
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(8),
            Background = TransparentBrush,
            BorderThickness = new Thickness(0),
            Content = rowGrid
        };

        if (Application.Current.Resources.TryGetValue("ContextMenuActionButtonStyle", out var styleObj) && styleObj is Style actionStyle)
            btn.Style = actionStyle;

        btn.Click += (_, _) => onClick();
        return btn;
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
}
