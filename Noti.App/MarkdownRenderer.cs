using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Noti.App;

public enum LiveBlockKind
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    Checklist,
    Bullet,
    Quote,
    CodeBlock,
    TableBlock,
    Divider
}

public sealed class LiveDocBlock
{
    public LiveBlockKind Kind { get; set; } = LiveBlockKind.Paragraph;
    public string Content { get; set; } = string.Empty;
    public bool IsChecked { get; set; }
    public bool IsBlockBold { get; set; }
    public bool IsBlockItalic { get; set; }
    public bool IsBlockStrikethrough { get; set; }
    public bool IsBlockCode { get; set; }
    public List<List<string>> TableMatrix { get; set; } = [];
    public List<double> ColumnWidths { get; set; } = [];
    public TextBox? PrimaryTextBox { get; set; }
    public Border? RichDisplayHost { get; set; }
    public FrameworkElement? RootElement { get; set; }
}

public static class MarkdownRenderer
{
    private static readonly Dictionary<int, List<double>> TableColumnWidths = new();

    public static void Render(
        StackPanel container,
        string? markdown,
        bool isDarkMode,
        double baseFontSize,
        Action<int, bool>? onChecklistToggled = null,
        IReadOnlyList<string>? noteTitles = null,
        Action<string>? onMentionClicked = null,
        Action<int, int, IReadOnlyList<string>>? onTableChanged = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Children.Clear();

        var primaryBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 245, 245, 247) : ColorHelper.FromArgb(255, 28, 28, 30));
        var secondaryBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 142, 142, 147) : ColorHelper.FromArgb(255, 108, 108, 112));
        var surfaceBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(36, 255, 255, 255) : ColorHelper.FromArgb(22, 0, 0, 0));
        var borderBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(38, 255, 255, 255) : ColorHelper.FromArgb(28, 0, 0, 0));
        var accentBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 10, 132, 255));
        var monoFont = new FontFamily("Cascadia Mono, Consolas, Courier New");
        var sortedTitles = noteTitles?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .OrderByDescending(t => t.Length)
            .ToList() ?? [];

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
                BorderThickness = new Thickness(0),
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
                        BorderThickness = new Thickness(0),
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

            // Interactive Visual Table block (| Col 1 | Col 2 |)
            if (IsMarkdownTableLine(trimmed))
            {
                var tableStart = i;
                var tableLines = new List<string> { raw };
                while (i + 1 < lines.Length && IsMarkdownTableLine(lines[i + 1].Trim()))
                {
                    i++;
                    tableLines.Add(lines[i]);
                }
                var tableEnd = i;
                container.Children.Add(CreateInteractiveTableElement(
                    tableStart,
                    tableEnd,
                    tableLines,
                    isDarkMode,
                    baseFontSize,
                    onTableChanged));
                continue;
            }

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
                container.Children.Add(CreateFormattedTextBlock(trimmed[4..], baseFontSize + 2, FontWeights.SemiBold, primaryBrush, accentBrush, monoFont, new Thickness(0, 12, 0, 4), sortedTitles, onMentionClicked));
                continue;
            }
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                container.Children.Add(CreateFormattedTextBlock(trimmed[3..], baseFontSize + 5, FontWeights.SemiBold, primaryBrush, accentBrush, monoFont, new Thickness(0, 16, 0, 6), sortedTitles, onMentionClicked));
                continue;
            }
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                container.Children.Add(CreateFormattedTextBlock(trimmed[2..], baseFontSize + 9, FontWeights.SemiBold, primaryBrush, accentBrush, monoFont, new Thickness(0, 8, 0, 8), sortedTitles, onMentionClicked));
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
                    accentBrush,
                    monoFont,
                    new Thickness(0),
                    sortedTitles,
                    onMentionClicked);
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
                var textBlock = CreateFormattedTextBlock(trimmed[2..], baseFontSize, FontWeights.Normal, primaryBrush, accentBrush, monoFont, new Thickness(0), sortedTitles, onMentionClicked);
                Grid.SetColumn(dot, 0);
                Grid.SetColumn(textBlock, 1);
                row.Children.Add(dot);
                row.Children.Add(textBlock);
                container.Children.Add(row);
                continue;
            }

            // Blockquote / Callout (> ) with clean left border
            if (trimmed.StartsWith("> ", StringComparison.Ordinal) || trimmed == ">")
            {
                var quoteText = trimmed.Length > 2 ? trimmed[2..] : string.Empty;
                var quoteBorder = new Border
                {
                    Background = surfaceBrush,
                    BorderBrush = accentBrush,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 10, 16, 10),
                    Margin = new Thickness(0, 4, 0, 4),
                    Child = CreateFormattedTextBlock(quoteText, baseFontSize, FontWeights.Normal, primaryBrush, accentBrush, monoFont, new Thickness(0), sortedTitles, onMentionClicked)
                };
                container.Children.Add(quoteBorder);
                continue;
            }

            // Standard paragraph
            container.Children.Add(CreateFormattedTextBlock(raw, baseFontSize, FontWeights.Normal, primaryBrush, accentBrush, monoFont, new Thickness(0, 2, 0, 4), sortedTitles, onMentionClicked));
        }

        FlushTreeBuffer();
    }

    public static bool IsMarkdownTableLine(string trimmedLine) =>
        trimmedLine.Length >= 2 && trimmedLine.StartsWith('|') && trimmedLine.Contains('|');

    public static bool TryFindTableBlockAtLine(
        string[] lines,
        int targetLineIndex,
        out int startLine,
        out int endLine,
        out List<string> tableLines)
    {
        startLine = -1;
        endLine = -1;
        tableLines = [];

        if (targetLineIndex < 0 || targetLineIndex >= lines.Length)
            return false;

        if (!IsMarkdownTableLine(lines[targetLineIndex].Trim()))
            return false;

        startLine = targetLineIndex;
        while (startLine > 0 && IsMarkdownTableLine(lines[startLine - 1].Trim()))
            startLine--;

        endLine = targetLineIndex;
        while (endLine + 1 < lines.Length && IsMarkdownTableLine(lines[endLine + 1].Trim()))
            endLine++;

        for (var i = startLine; i <= endLine; i++)
            tableLines.Add(lines[i]);

        return tableLines.Count > 0;
    }

    public static FrameworkElement CreateInteractiveTableElement(
        int initialStartLine,
        int initialEndLine,
        List<string> rawTableLines,
        bool isDarkMode,
        double baseFontSize,
        Action<int, int, IReadOnlyList<string>>? onTableChanged)
    {
        var primaryBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 245, 245, 247) : ColorHelper.FromArgb(255, 28, 28, 30));
        var secondaryBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 161, 161, 170) : ColorHelper.FromArgb(255, 82, 82, 91));
        var headerBgBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(34, 255, 255, 255) : ColorHelper.FromArgb(18, 0, 0, 0));
        var surfaceBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(20, 255, 255, 255) : ColorHelper.FromArgb(10, 0, 0, 0));
        var subtleBorderBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(36, 255, 255, 255) : ColorHelper.FromArgb(26, 0, 0, 0));
        var accentBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 10, 132, 255));
        var buttonBgBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(32, 255, 255, 255) : ColorHelper.FromArgb(18, 0, 0, 0));

        var currentStartLine = initialStartLine;
        var currentEndLine = initialEndLine;
        var matrix = ParseMarkdownTableMatrix(rawTableLines);
        var colCount = matrix.Count > 0 ? matrix.Max(r => r.Count) : 3;
        if (colCount < 1)
            colCount = 3;

        foreach (var row in matrix)
        {
            while (row.Count < colCount)
                row.Add(string.Empty);
        }

        if (!TableColumnWidths.TryGetValue(initialStartLine, out var colWidths))
        {
            colWidths = Enumerable.Repeat(150.0, colCount).ToList();
            TableColumnWidths[initialStartLine] = colWidths;
        }
        while (colWidths.Count < colCount)
            colWidths.Add(150.0);

        var wrapperGrid = new Grid
        {
            Margin = new Thickness(0, 6, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        wrapperGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        wrapperGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        wrapperGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        wrapperGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tableBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = subtleBorderBrush,
            Background = surfaceBrush
        };
        Grid.SetRow(tableBorder, 0);
        Grid.SetColumn(tableBorder, 0);
        wrapperGrid.Children.Add(tableBorder);

        var addColumnBtn = new Button
        {
            Content = "+",
            Width = 24,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = buttonBgBrush,
            Foreground = secondaryBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = subtleBorderBrush,
            VerticalAlignment = VerticalAlignment.Stretch,
            Opacity = 0.0
        };
        ToolTipService.SetToolTip(addColumnBtn, "Add column");
        Grid.SetRow(addColumnBtn, 0);
        Grid.SetColumn(addColumnBtn, 1);
        wrapperGrid.Children.Add(addColumnBtn);

        var addRowBtn = new Button
        {
            Content = "+ Add row",
            Height = 24,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 11,
            CornerRadius = new CornerRadius(8),
            Background = buttonBgBrush,
            Foreground = secondaryBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = subtleBorderBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Opacity = 0.0
        };
        ToolTipService.SetToolTip(addRowBtn, "Add row");
        Grid.SetRow(addRowBtn, 1);
        Grid.SetColumn(addRowBtn, 0);
        wrapperGrid.Children.Add(addRowBtn);

        wrapperGrid.PointerEntered += (_, _) =>
        {
            addColumnBtn.Opacity = 1.0;
            addRowBtn.Opacity = 1.0;
        };
        wrapperGrid.PointerExited += (_, _) =>
        {
            addColumnBtn.Opacity = 0.0;
            addRowBtn.Opacity = 0.0;
        };

        void SyncMarkdownToDocument()
        {
            var serialized = SerializeMarkdownTable(matrix);
            var oldEnd = currentEndLine;
            currentEndLine = currentStartLine + serialized.Count - 1;
            onTableChanged?.Invoke(currentStartLine, oldEnd, serialized);
        }

        void BuildTableGrid(int? focusRow = null, int? focusCol = null)
        {
            var rCount = matrix.Count;
            var cCount = matrix.Count > 0 ? matrix[0].Count : 3;
            while (colWidths.Count < cCount)
                colWidths.Add(150.0);

            var grid = new Grid();
            var colDefs = new List<ColumnDefinition>(cCount);
            for (var c = 0; c < cCount; c++)
            {
                var cd = new ColumnDefinition
                {
                    Width = new GridLength(Math.Clamp(colWidths[c], 76.0, 480.0), GridUnitType.Pixel)
                };
                colDefs.Add(cd);
                grid.ColumnDefinitions.Add(cd);
            }

            for (var r = 0; r < rCount; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var cellBoxes = new TextBox[rCount, cCount];

            for (var r = 0; r < rCount; r++)
            {
                var isHeader = r == 0;
                for (var c = 0; c < cCount; c++)
                {
                    var capturedRow = r;
                    var capturedCol = c;
                    var cellBorder = new Border
                    {
                        Background = isHeader ? headerBgBrush : new SolidColorBrush(Colors.Transparent),
                        BorderBrush = subtleBorderBrush,
                        BorderThickness = new Thickness(
                            0,
                            0,
                            c < cCount - 1 ? 1 : 0,
                            r < rCount - 1 ? 1 : 0)
                    };

                    var tb = new TextBox
                    {
                        Text = matrix[r][c],
                        PlaceholderText = isHeader ? $"Column {c + 1}" : string.Empty,
                        FontSize = Math.Max(12, baseFontSize - 2),
                        FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = primaryBrush,
                        Background = new SolidColorBrush(Colors.Transparent),
                        SelectionHighlightColor = accentBrush,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(10, 7, 10, 7),
                        MinHeight = 32,
                        MinWidth = 60,
                        IsSpellCheckEnabled = false,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    if (Application.Current?.Resources.TryGetValue("BorderlessTextBoxStyle", out var styleObj) == true && styleObj is Style s)
                        tb.Style = s;

                    tb.TextChanged += (_, _) =>
                    {
                        matrix[capturedRow][capturedCol] = (tb.Text ?? string.Empty).Replace('|', '/').Replace("\r", " ").Replace("\n", " ");
                        SyncMarkdownToDocument();
                    };

                    tb.PreviewKeyDown += (_, e) =>
                    {
                        if (e.Key != VirtualKey.Tab)
                            return;

                        e.Handled = true;
                        var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
                        if (shiftDown)
                        {
                            var prevCol = capturedCol - 1;
                            var prevRow = capturedRow;
                            if (prevCol < 0)
                            {
                                prevCol = cCount - 1;
                                prevRow = (capturedRow - 1 + rCount) % rCount;
                            }
                            cellBoxes[prevRow, prevCol].Focus(FocusState.Programmatic);
                            cellBoxes[prevRow, prevCol].SelectAll();
                        }
                        else
                        {
                            var nextCol = capturedCol + 1;
                            var nextRow = capturedRow;
                            if (nextCol >= cCount)
                            {
                                nextCol = 0;
                                nextRow = capturedRow + 1;
                            }

                            if (nextRow >= rCount)
                            {
                                matrix.Add(Enumerable.Repeat(string.Empty, cCount).ToList());
                                SyncMarkdownToDocument();
                                BuildTableGrid(focusRow: matrix.Count - 1, focusCol: 0);
                            }
                            else
                            {
                                cellBoxes[nextRow, nextCol].Focus(FocusState.Programmatic);
                                cellBoxes[nextRow, nextCol].SelectAll();
                            }
                        }
                    };

                    cellBoxes[r, c] = tb;
                    cellBorder.Child = tb;
                    Grid.SetRow(cellBorder, r);
                    Grid.SetColumn(cellBorder, c);
                    grid.Children.Add(cellBorder);
                }
            }

            // Draggable vertical column borders to resize columns
            for (var c = 0; c < cCount; c++)
            {
                var capturedCol = c;
                var colDef = colDefs[c];
                var resizeHandle = new Border
                {
                    Width = 6,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = new SolidColorBrush(Colors.Transparent),
                    ManipulationMode = ManipulationModes.TranslateX
                };
                ToolTipService.SetToolTip(resizeHandle, "Drag to resize column");
                resizeHandle.PointerEntered += (_, _) => resizeHandle.Background = accentBrush;
                resizeHandle.PointerExited += (_, _) => resizeHandle.Background = new SolidColorBrush(Colors.Transparent);
                resizeHandle.ManipulationDelta += (_, e) =>
                {
                    var currentWidth = colDef.Width.Value;
                    var nextWidth = Math.Clamp(currentWidth + e.Delta.Translation.X, 76.0, 480.0);
                    colDef.Width = new GridLength(nextWidth, GridUnitType.Pixel);
                    colWidths[capturedCol] = nextWidth;
                };

                Grid.SetRow(resizeHandle, 0);
                Grid.SetRowSpan(resizeHandle, rCount);
                Grid.SetColumn(resizeHandle, c);
                grid.Children.Add(resizeHandle);
            }

            tableBorder.Child = grid;

            if (focusRow is int fr && focusCol is int fc && fr >= 0 && fr < rCount && fc >= 0 && fc < cCount)
            {
                var targetTb = cellBoxes[fr, fc];
                targetTb.Loaded += (_, _) =>
                {
                    targetTb.Focus(FocusState.Programmatic);
                    targetTb.SelectAll();
                };
            }
        }

        addColumnBtn.Click += (_, _) =>
        {
            var newColIndex = matrix.Count > 0 ? matrix[0].Count : 0;
            for (var r = 0; r < matrix.Count; r++)
                matrix[r].Add(r == 0 ? $"Column {newColIndex + 1}" : string.Empty);

            colWidths.Add(150.0);
            SyncMarkdownToDocument();
            BuildTableGrid(focusRow: 0, focusCol: newColIndex);
        };

        addRowBtn.Click += (_, _) =>
        {
            var cCount = matrix.Count > 0 ? matrix[0].Count : 3;
            matrix.Add(Enumerable.Repeat(string.Empty, cCount).ToList());
            SyncMarkdownToDocument();
            BuildTableGrid(focusRow: matrix.Count - 1, focusCol: 0);
        };

        BuildTableGrid();
        return wrapperGrid;
    }

    private static List<List<string>> ParseMarkdownTableMatrix(List<string> rawLines)
    {
        var rows = new List<List<string>>();
        for (var i = 0; i < rawLines.Count; i++)
        {
            var trimmed = rawLines[i].Trim();
            if (!IsMarkdownTableLine(trimmed))
                continue;

            var inner = trimmed.Trim('|');
            var parts = inner.Split('|').Select(p => p.Trim()).ToList();

            // Skip separator row (| --- | --- |)
            if (i == 1 && parts.All(p => p.Length > 0 && p.All(ch => ch is '-' or ':' or ' ')))
                continue;

            rows.Add(parts);
        }

        if (rows.Count == 0)
        {
            rows.Add(["Column 1", "Column 2", "Column 3"]);
            rows.Add([string.Empty, string.Empty, string.Empty]);
        }
        else if (rows.Count == 1)
        {
            rows.Add(Enumerable.Repeat(string.Empty, rows[0].Count).ToList());
        }

        return rows;
    }

    public static List<string> SerializeMarkdownTable(List<List<string>> matrix)
    {
        if (matrix.Count == 0)
            return ["| Column 1 | Column 2 | Column 3 |", "| --- | --- | --- |", "|  |  |  |"];

        var colCount = matrix.Max(r => r.Count);
        var lines = new List<string>(matrix.Count + 1);

        var headerCells = matrix[0].Select(c => string.IsNullOrWhiteSpace(c) ? " " : c.Trim());
        lines.Add($"| {string.Join(" | ", headerCells)} |");
        lines.Add($"| {string.Join(" | ", Enumerable.Repeat("---", colCount))} |");

        for (var r = 1; r < matrix.Count; r++)
        {
            var row = matrix[r];
            var cells = new List<string>(colCount);
            for (var c = 0; c < colCount; c++)
                cells.Add(c < row.Count ? row[c] : string.Empty);

            lines.Add($"| {string.Join(" | ", cells)} |");
        }

        return lines;
    }

    public static bool HasInlineMarkdownFormatting(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return text.Contains("**", StringComparison.Ordinal) ||
               text.Contains("~~", StringComparison.Ordinal) ||
               text.Contains('`') ||
               text.Contains("](", StringComparison.Ordinal) ||
               text.Contains('@') ||
               (text.IndexOf('*') is var starIdx && starIdx >= 0 && text.IndexOf('*', starIdx + 1) > starIdx);
    }

    public static List<LiveDocBlock> ParseLiveBlocks(string? markdown)
    {
        var blocks = new List<LiveDocBlock>();
        if (string.IsNullOrEmpty(markdown))
        {
            blocks.Add(new LiveDocBlock { Kind = LiveBlockKind.Paragraph });
            return blocks;
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inCodeBlock = false;
        var codeBuffer = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCodeBlock)
                {
                    blocks.Add(new LiveDocBlock
                    {
                        Kind = LiveBlockKind.CodeBlock,
                        Content = string.Join("\r", codeBuffer)
                    });
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

            if (IsMarkdownTableLine(trimmed))
            {
                var tableLines = new List<string> { raw };
                while (i + 1 < lines.Length && IsMarkdownTableLine(lines[i + 1].Trim()))
                {
                    i++;
                    tableLines.Add(lines[i]);
                }

                blocks.Add(new LiveDocBlock
                {
                    Kind = LiveBlockKind.TableBlock,
                    TableMatrix = ParseMarkdownTableMatrix(tableLines)
                });
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                blocks.Add(new LiveDocBlock { Kind = LiveBlockKind.Divider });
                continue;
            }

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                blocks.Add(CreateTextLiveBlock(LiveBlockKind.Heading3, trimmed[4..]));
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                blocks.Add(CreateTextLiveBlock(LiveBlockKind.Heading2, trimmed[3..]));
                continue;
            }

            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                blocks.Add(CreateTextLiveBlock(LiveBlockKind.Heading1, trimmed[2..]));
                continue;
            }

            if (trimmed.StartsWith("- [ ] ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
            {
                var isChecked = trimmed[3] is 'x' or 'X';
                blocks.Add(CreateTextLiveBlock(LiveBlockKind.Checklist, trimmed[6..], isChecked));
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                blocks.Add(CreateTextLiveBlock(LiveBlockKind.Bullet, trimmed[2..]));
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal) || trimmed == ">")
            {
                var quoteText = trimmed.Length > 2 ? trimmed[2..] : string.Empty;
                blocks.Add(CreateTextLiveBlock(LiveBlockKind.Quote, quoteText));
                continue;
            }

            blocks.Add(CreateTextLiveBlock(LiveBlockKind.Paragraph, raw));
        }

        if (inCodeBlock)
        {
            blocks.Add(new LiveDocBlock
            {
                Kind = LiveBlockKind.CodeBlock,
                Content = string.Join("\r", codeBuffer)
            });
        }

        if (blocks.Count == 0)
            blocks.Add(new LiveDocBlock { Kind = LiveBlockKind.Paragraph });

        return blocks;
    }

    private static LiveDocBlock CreateTextLiveBlock(LiveBlockKind kind, string rawContent, bool isChecked = false)
    {
        var block = new LiveDocBlock
        {
            Kind = kind,
            IsChecked = isChecked
        };

        var s = rawContent ?? string.Empty;
        var trimmed = s.Trim();
        if (trimmed is "**" or "****" or "~~" or "~~~~" or "``")
        {
            block.Content = string.Empty;
            return block;
        }

        var changed = true;
        while (changed && s.Length > 0)
        {
            changed = false;
            if (s.Length >= 6 && s.StartsWith("***", StringComparison.Ordinal) && s.EndsWith("***", StringComparison.Ordinal) &&
                s.IndexOf("***", 3, StringComparison.Ordinal) == s.Length - 3)
            {
                block.IsBlockBold = true;
                block.IsBlockItalic = true;
                s = s[3..^3];
                changed = true;
            }
            else if (s.Length >= 4 && s.StartsWith("**", StringComparison.Ordinal) && s.EndsWith("**", StringComparison.Ordinal) &&
                     s.IndexOf("**", 2, StringComparison.Ordinal) == s.Length - 2)
            {
                block.IsBlockBold = true;
                s = s[2..^2];
                changed = true;
            }
            else if (s.Length >= 4 && s.StartsWith("~~", StringComparison.Ordinal) && s.EndsWith("~~", StringComparison.Ordinal) &&
                     s.IndexOf("~~", 2, StringComparison.Ordinal) == s.Length - 2)
            {
                block.IsBlockStrikethrough = true;
                s = s[2..^2];
                changed = true;
            }
            else if (s.Length >= 2 && s.StartsWith('*') && !s.StartsWith("**", StringComparison.Ordinal) &&
                     s.EndsWith('*') && !s.EndsWith("**", StringComparison.Ordinal) &&
                     s.IndexOf('*', 1) == s.Length - 1)
            {
                block.IsBlockItalic = true;
                s = s[1..^1];
                changed = true;
            }
            else if (s.Length >= 2 && s.StartsWith('`') && s.EndsWith('`') && s.IndexOf('`', 1) == s.Length - 1)
            {
                block.IsBlockCode = true;
                s = s[1..^1];
                changed = true;
            }
        }

        if (s.StartsWith("**", StringComparison.Ordinal) && !s[2..].Contains("**", StringComparison.Ordinal))
        {
            block.IsBlockBold = true;
            s = s[2..];
        }
        else if (s.StartsWith("~~", StringComparison.Ordinal) && !s[2..].Contains("~~", StringComparison.Ordinal))
        {
            block.IsBlockStrikethrough = true;
            s = s[2..];
        }

        block.Content = s;
        return block;
    }

    public static string SerializeLiveBlocks(IReadOnlyList<LiveDocBlock> blocks)
    {
        if (blocks.Count == 0)
            return string.Empty;

        var lines = new List<string>(blocks.Count);
        foreach (var b in blocks)
        {
            switch (b.Kind)
            {
                case LiveBlockKind.CodeBlock:
                {
                    var normalizedCode = (b.Content ?? string.Empty).Replace("\r\n", "\r").Replace('\n', '\r');
                    lines.Add("```");
                    lines.Add(normalizedCode);
                    lines.Add("```");
                    break;
                }

                case LiveBlockKind.TableBlock:
                {
                    var tableLines = SerializeMarkdownTable(b.TableMatrix);
                    lines.AddRange(tableLines);
                    break;
                }

                case LiveBlockKind.Divider:
                    lines.Add("---");
                    break;

                default:
                {
                    var content = FormatBlockInlineContent(b);
                    var line = b.Kind switch
                    {
                        LiveBlockKind.Heading1 => $"# {content}",
                        LiveBlockKind.Heading2 => $"## {content}",
                        LiveBlockKind.Heading3 => $"### {content}",
                        LiveBlockKind.Checklist => $"- [{(b.IsChecked ? "x" : " ")}] {content}",
                        LiveBlockKind.Bullet => $"- {content}",
                        LiveBlockKind.Quote => string.IsNullOrEmpty(content) ? "> " : $"> {content}",
                        _ => content
                    };
                    lines.Add(line);
                    break;
                }
            }
        }

        return string.Join("\r", lines);
    }

    public static string FormatBlockInlineContent(LiveDocBlock b)
    {
        var content = b.Content ?? string.Empty;
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        if (b.IsBlockCode)
            return $"`{content}`";

        if (b.IsBlockStrikethrough)
            content = $"~~{content}~~";

        if (b.IsBlockBold && b.IsBlockItalic)
            return $"***{content}***";
        if (b.IsBlockBold)
            return $"**{content}**";
        if (b.IsBlockItalic)
            return $"*{content}*";

        return content;
    }

    public static FrameworkElement CreateLiveTableElement(
        LiveDocBlock tableBlock,
        bool isDarkMode,
        double baseFontSize,
        Action onTableMutated,
        Action? onRequestExitBelow = null,
        Action? onRequestDeleteTable = null)
    {
        if (tableBlock.TableMatrix.Count == 0)
        {
            tableBlock.TableMatrix =
            [
                ["Column 1", "Column 2", "Column 3"],
                [string.Empty, string.Empty, string.Empty],
                [string.Empty, string.Empty, string.Empty]
            ];
        }

        var primaryBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 245, 245, 247) : ColorHelper.FromArgb(255, 28, 28, 30));
        var secondaryBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(255, 161, 161, 170) : ColorHelper.FromArgb(255, 82, 82, 91));
        var headerBgBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(34, 255, 255, 255) : ColorHelper.FromArgb(18, 0, 0, 0));
        var surfaceBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(20, 255, 255, 255) : ColorHelper.FromArgb(10, 0, 0, 0));
        var subtleBorderBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(36, 255, 255, 255) : ColorHelper.FromArgb(26, 0, 0, 0));
        var accentBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 10, 132, 255));
        var buttonBgBrush = new SolidColorBrush(isDarkMode ? ColorHelper.FromArgb(32, 255, 255, 255) : ColorHelper.FromArgb(18, 0, 0, 0));

        var matrix = tableBlock.TableMatrix;
        var colCount = matrix.Count > 0 ? matrix.Max(r => r.Count) : 3;
        if (colCount < 1)
            colCount = 3;

        foreach (var row in matrix)
        {
            while (row.Count < colCount)
                row.Add(string.Empty);
        }

        var colWidths = tableBlock.ColumnWidths;
        while (colWidths.Count < colCount)
            colWidths.Add(156.0);

        var wrapperGrid = new Grid
        {
            Margin = new Thickness(0, 6, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        wrapperGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        wrapperGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        wrapperGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        wrapperGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tableBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = subtleBorderBrush,
            Background = surfaceBrush
        };
        Grid.SetRow(tableBorder, 0);
        Grid.SetColumn(tableBorder, 0);
        wrapperGrid.Children.Add(tableBorder);

        var rightEdgeStack = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Opacity = 0.0
        };

        var addColumnBtn = new Button
        {
            Content = "+",
            Width = 24,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = buttonBgBrush,
            Foreground = secondaryBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = subtleBorderBrush
        };
        ToolTipService.SetToolTip(addColumnBtn, "Add column");
        rightEdgeStack.Children.Add(addColumnBtn);

        if (onRequestDeleteTable is not null)
        {
            var deleteTableBtn = new Button
            {
                Content = "×",
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Background = buttonBgBrush,
                Foreground = secondaryBrush,
                BorderThickness = new Thickness(1),
                BorderBrush = subtleBorderBrush
            };
            ToolTipService.SetToolTip(deleteTableBtn, "Delete table");
            deleteTableBtn.Click += (_, _) => onRequestDeleteTable();
            rightEdgeStack.Children.Add(deleteTableBtn);
        }

        Grid.SetRow(rightEdgeStack, 0);
        Grid.SetColumn(rightEdgeStack, 1);
        wrapperGrid.Children.Add(rightEdgeStack);

        var addRowBtn = new Button
        {
            Content = "+ Add row",
            Height = 24,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 11,
            CornerRadius = new CornerRadius(8),
            Background = buttonBgBrush,
            Foreground = secondaryBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = subtleBorderBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Opacity = 0.0
        };
        ToolTipService.SetToolTip(addRowBtn, "Add row");
        Grid.SetRow(addRowBtn, 1);
        Grid.SetColumn(addRowBtn, 0);
        wrapperGrid.Children.Add(addRowBtn);

        wrapperGrid.PointerEntered += (_, _) =>
        {
            rightEdgeStack.Opacity = 1.0;
            addRowBtn.Opacity = 1.0;
        };
        wrapperGrid.PointerExited += (_, _) =>
        {
            rightEdgeStack.Opacity = 0.0;
            addRowBtn.Opacity = 0.0;
        };

        void BuildTableGrid(int? focusRow = null, int? focusCol = null)
        {
            var rCount = matrix.Count;
            var cCount = matrix.Count > 0 ? matrix[0].Count : 3;
            while (colWidths.Count < cCount)
                colWidths.Add(156.0);

            var grid = new Grid();
            var colDefs = new List<ColumnDefinition>(cCount);
            for (var c = 0; c < cCount; c++)
            {
                var cd = new ColumnDefinition
                {
                    Width = new GridLength(Math.Clamp(colWidths[c], 76.0, 480.0), GridUnitType.Pixel)
                };
                colDefs.Add(cd);
                grid.ColumnDefinitions.Add(cd);
            }

            for (var r = 0; r < rCount; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var cellBoxes = new TextBox[rCount, cCount];

            for (var r = 0; r < rCount; r++)
            {
                var isHeader = r == 0;
                for (var c = 0; c < cCount; c++)
                {
                    var capturedRow = r;
                    var capturedCol = c;
                    var cellBorder = new Border
                    {
                        Background = isHeader ? headerBgBrush : new SolidColorBrush(Colors.Transparent),
                        BorderBrush = subtleBorderBrush,
                        BorderThickness = new Thickness(
                            0,
                            0,
                            c < cCount - 1 ? 1 : 0,
                            r < rCount - 1 ? 1 : 0)
                    };

                    var tb = new TextBox
                    {
                        Text = matrix[r][c],
                        PlaceholderText = isHeader ? $"Column {c + 1}" : string.Empty,
                        FontSize = Math.Max(12, baseFontSize - 2),
                        FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = primaryBrush,
                        Background = new SolidColorBrush(Colors.Transparent),
                        SelectionHighlightColor = accentBrush,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(10, 7, 10, 7),
                        MinHeight = 32,
                        MinWidth = 60,
                        IsSpellCheckEnabled = false,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    if (Application.Current?.Resources.TryGetValue("BorderlessTextBoxStyle", out var styleObj) == true && styleObj is Style s)
                        tb.Style = s;

                    tb.TextChanged += (_, _) =>
                    {
                        matrix[capturedRow][capturedCol] = (tb.Text ?? string.Empty).Replace('|', '/').Replace("\r", " ").Replace("\n", " ");
                        onTableMutated();
                    };

                    tb.PreviewKeyDown += (_, e) =>
                    {
                        if (e.Key == VirtualKey.Tab)
                        {
                            e.Handled = true;
                            var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
                            if (shiftDown)
                            {
                                var prevCol = capturedCol - 1;
                                var prevRow = capturedRow;
                                if (prevCol < 0)
                                {
                                    prevCol = cCount - 1;
                                    prevRow = (capturedRow - 1 + rCount) % rCount;
                                }
                                cellBoxes[prevRow, prevCol].Focus(FocusState.Programmatic);
                                cellBoxes[prevRow, prevCol].SelectAll();
                            }
                            else
                            {
                                var nextCol = capturedCol + 1;
                                var nextRow = capturedRow;
                                if (nextCol >= cCount)
                                {
                                    nextCol = 0;
                                    nextRow = capturedRow + 1;
                                }

                                if (nextRow >= rCount)
                                {
                                    matrix.Add(Enumerable.Repeat(string.Empty, cCount).ToList());
                                    onTableMutated();
                                    BuildTableGrid(focusRow: matrix.Count - 1, focusCol: 0);
                                }
                                else
                                {
                                    cellBoxes[nextRow, nextCol].Focus(FocusState.Programmatic);
                                    cellBoxes[nextRow, nextCol].SelectAll();
                                }
                            }
                        }
                        else if (e.Key == VirtualKey.Down && capturedRow == rCount - 1)
                        {
                            e.Handled = true;
                            onRequestExitBelow?.Invoke();
                        }
                        else if (e.Key == VirtualKey.Down && capturedRow + 1 < rCount)
                        {
                            e.Handled = true;
                            cellBoxes[capturedRow + 1, capturedCol].Focus(FocusState.Programmatic);
                        }
                        else if (e.Key == VirtualKey.Up && capturedRow > 0)
                        {
                            e.Handled = true;
                            cellBoxes[capturedRow - 1, capturedCol].Focus(FocusState.Programmatic);
                        }
                    };

                    cellBoxes[r, c] = tb;
                    cellBorder.Child = tb;
                    Grid.SetRow(cellBorder, r);
                    Grid.SetColumn(cellBorder, c);
                    grid.Children.Add(cellBorder);
                }
            }

            if (rCount > 0 && cCount > 0)
                tableBlock.PrimaryTextBox = cellBoxes[0, 0];

            for (var c = 0; c < cCount; c++)
            {
                var capturedCol = c;
                var colDef = colDefs[c];
                var resizeHandle = new Border
                {
                    Width = 6,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = new SolidColorBrush(Colors.Transparent),
                    ManipulationMode = ManipulationModes.TranslateX
                };
                ToolTipService.SetToolTip(resizeHandle, "Drag to resize column");
                resizeHandle.PointerEntered += (_, _) => resizeHandle.Background = accentBrush;
                resizeHandle.PointerExited += (_, _) => resizeHandle.Background = new SolidColorBrush(Colors.Transparent);
                resizeHandle.ManipulationDelta += (_, e) =>
                {
                    var currentWidth = colDef.Width.Value;
                    var nextWidth = Math.Clamp(currentWidth + e.Delta.Translation.X, 76.0, 480.0);
                    colDef.Width = new GridLength(nextWidth, GridUnitType.Pixel);
                    colWidths[capturedCol] = nextWidth;
                };

                Grid.SetRow(resizeHandle, 0);
                Grid.SetRowSpan(resizeHandle, rCount);
                Grid.SetColumn(resizeHandle, c);
                grid.Children.Add(resizeHandle);
            }

            tableBorder.Child = grid;

            if (focusRow is int fr && focusCol is int fc && fr >= 0 && fr < rCount && fc >= 0 && fc < cCount)
            {
                var targetTb = cellBoxes[fr, fc];
                targetTb.Loaded += (_, _) =>
                {
                    targetTb.Focus(FocusState.Programmatic);
                    targetTb.SelectAll();
                };
            }
        }

        addColumnBtn.Click += (_, _) =>
        {
            var newColIndex = matrix.Count > 0 ? matrix[0].Count : 0;
            for (var r = 0; r < matrix.Count; r++)
                matrix[r].Add(r == 0 ? $"Column {newColIndex + 1}" : string.Empty);

            colWidths.Add(156.0);
            onTableMutated();
            BuildTableGrid(focusRow: 0, focusCol: newColIndex);
        };

        addRowBtn.Click += (_, _) =>
        {
            var cCount = matrix.Count > 0 ? matrix[0].Count : 3;
            matrix.Add(Enumerable.Repeat(string.Empty, cCount).ToList());
            onTableMutated();
            BuildTableGrid(focusRow: matrix.Count - 1, focusCol: 0);
        };

        BuildTableGrid();
        return wrapperGrid;
    }

    public static TextBlock CreateFormattedTextBlock(
        string text,
        double fontSize,
        Windows.UI.Text.FontWeight fontWeight,
        Brush foreground,
        Brush accentBrush,
        FontFamily monoFont,
        Thickness margin,
        List<string> sortedTitles,
        Action<string>? onMentionClicked)
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

        PopulateInlines(tb.Inlines, text, monoFont, accentBrush, sortedTitles, onMentionClicked);
        return tb;
    }

    private static void PopulateInlines(
        InlineCollection inlines,
        string text,
        FontFamily monoFont,
        Brush accentBrush,
        List<string> sortedTitles,
        Action<string>? onMentionClicked)
    {
        var i = 0;
        while (i < text.Length)
        {
            // Smart @Note mention (@[Note Title] or @NoteTitle)
            if (text[i] == '@' && i + 1 < text.Length && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))
            {
                var rest = text[(i + 1)..];
                string? matchedMention = null;
                var consumedLen = 0;

                if (rest.StartsWith('[') && rest.IndexOf(']') is var closeBracket and > 1)
                {
                    matchedMention = rest[1..closeBracket];
                    consumedLen = closeBracket + 2; // '@' + '[' + title + ']'
                }
                else
                {
                    foreach (var title in sortedTitles)
                    {
                        if (rest.StartsWith(title, StringComparison.OrdinalIgnoreCase) &&
                            (rest.Length == title.Length || !char.IsLetterOrDigit(rest[title.Length])))
                        {
                            matchedMention = title;
                            consumedLen = 1 + title.Length;
                            break;
                        }
                    }

                    if (matchedMention is null)
                    {
                        var wordLen = 0;
                        while (wordLen < rest.Length && (char.IsLetterOrDigit(rest[wordLen]) || rest[wordLen] is '_' or '-'))
                            wordLen++;

                        if (wordLen > 0)
                        {
                            matchedMention = rest[..wordLen];
                            consumedLen = 1 + wordLen;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(matchedMention) && consumedLen > 1)
                {
                    var targetName = matchedMention;
                    var link = new Hyperlink
                    {
                        UnderlineStyle = UnderlineStyle.None,
                        Foreground = accentBrush
                    };
                    var bold = new Bold();
                    bold.Inlines.Add(new Run { Text = $"@{targetName}" });
                    link.Inlines.Add(bold);
                    link.Click += (_, _) => onMentionClicked?.Invoke(targetName);
                    inlines.Add(link);
                    i += consumedLen;
                    continue;
                }
            }

            // Markdown link ([label](url))
            if (text[i] == '[')
            {
                var closeBracket = text.IndexOf("](", i + 1, StringComparison.Ordinal);
                if (closeBracket > i + 1)
                {
                    var closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 2)
                    {
                        var labelText = text[(i + 1)..closeBracket];
                        var urlText = text[(closeBracket + 2)..closeParen].Trim();
                        var link = new Hyperlink
                        {
                            UnderlineStyle = UnderlineStyle.Single,
                            Foreground = accentBrush
                        };
                        if (Uri.TryCreate(urlText, UriKind.Absolute, out var uri))
                            link.NavigateUri = uri;
                        link.Inlines.Add(new Run { Text = labelText });
                        inlines.Add(link);
                        i = closeParen + 1;
                        continue;
                    }
                }
            }

            // Strikethrough (~~text~~)
            if (i + 3 < text.Length && text[i] == '~' && text[i + 1] == '~')
            {
                var close = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (close > i + 2)
                {
                    var strikeRun = (Run)XamlReader.Load(
                        "<Run xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TextDecorations=\"Strikethrough\" />");
                    strikeRun.Text = text[(i + 2)..close];
                    inlines.Add(strikeRun);
                    i = close + 2;
                    continue;
                }
            }

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

            var nextSpecial = text.IndexOfAny(['*', '`', '@', '~', '['], i + 1);
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
