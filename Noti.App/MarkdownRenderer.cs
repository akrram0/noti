using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Noti.App;

public static class MarkdownRenderer
{
    public static void Render(
        StackPanel container,
        string? markdown,
        bool isDarkMode,
        double baseFontSize,
        Action<int, bool>? onChecklistToggled = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Children.Clear();

        var primaryBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 245, 245, 247) : ColorHelper.FromArgb(255, 28, 28, 30));
        var secondaryBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 142, 142, 147) : ColorHelper.FromArgb(255, 108, 108, 112));
        var surfaceBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 22, 22, 24) : ColorHelper.FromArgb(255, 242, 242, 247));
        var borderBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 38, 38, 38) : ColorHelper.FromArgb(255, 209, 209, 214));
        var monoFont = new FontFamily("Cascadia Mono, Consolas, Courier New");

        if (string.IsNullOrWhiteSpace(markdown))
        {
            container.Children.Add(new TextBlock
            {
                Text = "Nothing to preview yet.",
                FontSize = baseFontSize,
                Foreground = secondaryBrush
            });
            return;
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inCodeBlock = false;
        var codeBuffer = new List<string>();
        var treeBuffer = new List<string>();

        void FlushTreeBuffer()
        {
            if (treeBuffer.Count == 0)
                return;

            var card = new Border
            {
                Background = surfaceBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 4, 0, 8),
                Child = new TextBlock
                {
                    Text = string.Join(Environment.NewLine, treeBuffer),
                    FontFamily = monoFont,
                    FontSize = Math.Max(13, baseFontSize - 1),
                    LineHeight = 22,
                    Foreground = primaryBrush,
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.NoWrap
                }
            };
            container.Children.Add(card);
            treeBuffer.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var lineIndex = i;
            var raw = lines[i];
            var trimmed = raw.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushTreeBuffer();
                if (inCodeBlock)
                {
                    var codeCard = new Border
                    {
                        Background = surfaceBrush,
                        BorderBrush = borderBrush,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(16, 12, 16, 12),
                        Margin = new Thickness(0, 4, 0, 8),
                        Child = new TextBlock
                        {
                            Text = string.Join(Environment.NewLine, codeBuffer),
                            FontFamily = monoFont,
                            FontSize = Math.Max(13, baseFontSize - 2),
                            Foreground = primaryBrush,
                            IsTextSelectionEnabled = true,
                            TextWrapping = TextWrapping.Wrap
                        }
                    };
                    container.Children.Add(codeCard);
                    codeBuffer.Clear();
                    inCodeBlock = false;
                }
                else
                {
                    inCodeBlock = true;
                }
                continue;
            }

            if (inCodeBlock)
            {
                codeBuffer.Add(raw);
                continue;
            }

            // Group tree diagram lines (├─, └──, │) into a sleek monospace tree block
            if (raw.Contains("├─", StringComparison.Ordinal) ||
                raw.Contains("└─", StringComparison.Ordinal) ||
                trimmed.StartsWith('│'))
            {
                treeBuffer.Add(raw);
                continue;
            }

            FlushTreeBuffer();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                container.Children.Add(new Border { Height = 8 });
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                container.Children.Add(new Border
                {
                    Height = 1,
                    Background = borderBrush,
                    Margin = new Thickness(0, 12, 0, 12)
                });
                continue;
            }

            // Headings (#, ##, ###)
            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                container.Children.Add(CreateFormattedTextBlock(trimmed[4..], baseFontSize + 2, FontWeights.SemiBold, primaryBrush, surfaceBrush, monoFont, new Thickness(0, 12, 0, 4)));
                continue;
            }
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                container.Children.Add(CreateFormattedTextBlock(trimmed[3..], baseFontSize + 5, FontWeights.SemiBold, primaryBrush, surfaceBrush, monoFont, new Thickness(0, 16, 0, 6)));
                continue;
            }
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                container.Children.Add(CreateFormattedTextBlock(trimmed[2..], baseFontSize + 9, FontWeights.SemiBold, primaryBrush, surfaceBrush, monoFont, new Thickness(0, 8, 0, 8)));
                continue;
            }

            // Checklist (- [ ] or - [x])
            if (trimmed.StartsWith("- [ ] ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
            {
                var isChecked = trimmed[3] is 'x' or 'X';
                var itemText = trimmed[6..];
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(0, 2, 0, 2)
                };

                var checkBox = new CheckBox
                {
                    IsChecked = isChecked,
                    MinWidth = 0,
                    Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkBox.Click += (_, _) => onChecklistToggled?.Invoke(lineIndex, checkBox.IsChecked == true);

                var label = CreateFormattedTextBlock(
                    itemText,
                    baseFontSize,
                    FontWeights.Normal,
                    isChecked ? secondaryBrush : primaryBrush,
                    surfaceBrush,
                    monoFont,
                    new Thickness(0));
                label.VerticalAlignment = VerticalAlignment.Center;
                if (isChecked)
                    label.Opacity = 0.65;

                row.Children.Add(checkBox);
                row.Children.Add(label);
                container.Children.Add(row);
                continue;
            }

            // Bullet list (- or *)
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                var indent = (raw.Length - raw.TrimStart().Length) * 8;
                var row = new Grid { Margin = new Thickness(indent, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var dot = new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = secondaryBrush,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(4, 10, 0, 0)
                };
                var textBlock = CreateFormattedTextBlock(trimmed[2..], baseFontSize, FontWeights.Normal, primaryBrush, surfaceBrush, monoFont, new Thickness(0));
                Grid.SetColumn(dot, 0);
                Grid.SetColumn(textBlock, 1);
                row.Children.Add(dot);
                row.Children.Add(textBlock);
                container.Children.Add(row);
                continue;
            }

            // Blockquote (> )
            if (trimmed.StartsWith("> ", StringComparison.Ordinal) || trimmed == ">")
            {
                var quoteText = trimmed.Length > 2 ? trimmed[2..] : string.Empty;
                var quoteBorder = new Border
                {
                    Background = surfaceBrush,
                    BorderBrush = secondaryBrush,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(16, 10, 16, 10),
                    Margin = new Thickness(0, 4, 0, 4),
                    Child = CreateFormattedTextBlock(quoteText, baseFontSize, FontWeights.Normal, secondaryBrush, surfaceBrush, monoFont, new Thickness(0))
                };
                container.Children.Add(quoteBorder);
                continue;
            }

            // Standard paragraph
            container.Children.Add(CreateFormattedTextBlock(raw, baseFontSize, FontWeights.Normal, primaryBrush, surfaceBrush, monoFont, new Thickness(0, 2, 0, 4)));
        }

        FlushTreeBuffer();
    }

    private static TextBlock CreateFormattedTextBlock(
        string text,
        double fontSize,
        Windows.UI.Text.FontWeight fontWeight,
        Brush foreground,
        Brush codeSurface,
        FontFamily monoFont,
        Thickness margin)
    {
        var tb = new TextBlock
        {
            FontSize = fontSize,
            FontWeight = fontWeight,
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = fontSize * 1.65,
            Margin = margin,
            IsTextSelectionEnabled = true
        };

        PopulateInlines(tb.Inlines, text, monoFont);
        return tb;
    }

    private static void PopulateInlines(InlineCollection inlines, string text, FontFamily monoFont)
    {
        var i = 0;
        while (i < text.Length)
        {
            // Bold (**text**)
            if (i + 3 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var close = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (close > i + 2)
                {
                    var bold = new Bold();
                    bold.Inlines.Add(new Run { Text = text[(i + 2)..close] });
                    inlines.Add(bold);
                    i = close + 2;
                    continue;
                }
            }

            // Inline code (`code`)
            if (text[i] == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > i + 1)
                {
                    inlines.Add(new Run
                    {
                        Text = text[(i + 1)..close],
                        FontFamily = monoFont
                    });
                    i = close + 1;
                    continue;
                }
            }

            // Italic (*text*)
            if (text[i] == '*' && (i + 1 < text.Length && text[i + 1] != '*'))
            {
                var close = text.IndexOf('*', i + 1);
                if (close > i + 1)
                {
                    var italic = new Italic();
                    italic.Inlines.Add(new Run { Text = text[(i + 1)..close] });
                    inlines.Add(italic);
                    i = close + 1;
                    continue;
                }
            }

            var nextSpecial = text.IndexOfAny(['*', '`'], i + 1);
            if (nextSpecial < 0)
            {
                inlines.Add(new Run { Text = text[i..] });
                break;
            }

            inlines.Add(new Run { Text = text[i..nextSpecial] });
            i = nextSpecial;
        }
    }
}
