namespace Noti.Core;

public sealed class Note
{
    private const int MaxSnippetLength = 38;

    public required string Id { get; init; }
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Title
    {
        get
        {
            foreach (var rawLine in EnumerateLines(Content))
            {
                var cleaned = rawLine.TrimStart('#', ' ', '\t').Trim();
                if (!string.IsNullOrEmpty(cleaned))
                    return cleaned;
            }
            return "Untitled";
        }
    }

    public string Snippet
    {
        get
        {
            var skippedTitle = false;
            var parts = new List<string>();

            foreach (var rawLine in EnumerateLines(Content))
            {
                var trimmed = rawLine.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (!skippedTitle)
                {
                    skippedTitle = true;
                    continue;
                }

                parts.Add(trimmed);
            }

            if (parts.Count == 0)
                return string.Empty;

            var joined = string.Join(" ", parts);
            return joined.Length > MaxSnippetLength
                ? string.Concat(joined.AsSpan(0, MaxSnippetLength), "...")
                : joined;
        }
    }

    public int WordCount => NoteService.CountWords(Content);

    private static IEnumerable<string> EnumerateLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            yield return line;
    }
}
