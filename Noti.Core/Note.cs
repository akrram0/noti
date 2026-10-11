namespace Noti.Core;

public sealed class Note
{
    private const int MaxSnippetLength = 38;

    public required string Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsPinned { get; set; }

    public string FolderPath
    {
        get
        {
            var normalized = Id.Replace('\\', '/').Trim('/');
            var lastSlash = normalized.LastIndexOf('/');
            return lastSlash > 0 ? normalized[..lastSlash] : string.Empty;
        }
    }

    public string Title
    {
        get
        {
            foreach (var rawLine in EnumerateLines(Content))
            {
                var cleaned = CleanDisplayLine(rawLine);
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
                var cleaned = CleanDisplayLine(rawLine);
                if (string.IsNullOrEmpty(cleaned))
                    continue;

                if (!skippedTitle)
                {
                    skippedTitle = true;
                    continue;
                }

                parts.Add(cleaned);
            }

            if (parts.Count == 0)
                return "No additional text";

            var joined = string.Join(" ", parts);
            return joined.Length > MaxSnippetLength
                ? $"{joined[..MaxSnippetLength]}..."
                : joined;
        }
    }

    public int WordCount => NoteService.CountWords(Content);

    public static string CleanDisplayLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        var s = line.Trim();
        if (s.StartsWith("```", StringComparison.Ordinal) ||
            s is "---" or "***" or "___" ||
            (s.StartsWith("<div", StringComparison.OrdinalIgnoreCase) && s.EndsWith('>')) ||
            s.Equals("</div>", StringComparison.OrdinalIgnoreCase) ||
            (s.StartsWith("<img", StringComparison.OrdinalIgnoreCase) && s.EndsWith('>')) ||
            (s.StartsWith("![", StringComparison.Ordinal) && s.EndsWith(')')) ||
            (s.StartsWith("[![", StringComparison.Ordinal) && s.EndsWith(')')))
        {
            return string.Empty;
        }

        if (s.StartsWith("<sub>", StringComparison.OrdinalIgnoreCase) && s.EndsWith("</sub>", StringComparison.OrdinalIgnoreCase) && s.Length >= 11)
            s = s[5..^6].Trim();

        if (s.StartsWith('|') && s.Contains('|'))
        {
            var strippedPipes = s.Replace("|", " ", StringComparison.Ordinal).Trim();
            if (strippedPipes.All(ch => ch is '-' or ':' or ' ' or '\t'))
                return string.Empty;
            s = strippedPipes;
        }

        // Strip tree connectors and markdown prefixes
        while (s.Length > 0)
        {
            if (s.StartsWith("├─", StringComparison.Ordinal) ||
                s.StartsWith("└─", StringComparison.Ordinal))
            {
                s = s[2..].TrimStart('─', ' ', '\t');
                continue;
            }

            if (s[0] is '│' or '├' or '└' or '─' or '#' or '>' or '\t' or ' ')
            {
                s = s[1..];
                continue;
            }

            if (s.StartsWith("- [ ] ", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
            {
                s = s[6..];
                continue;
            }

            if (s.StartsWith("- ", StringComparison.Ordinal) ||
                s.StartsWith("* ", StringComparison.Ordinal))
            {
                s = s[2..];
                continue;
            }

            break;
        }

        return s.Replace("**", string.Empty, StringComparison.Ordinal)
                .Replace("__", string.Empty, StringComparison.Ordinal)
                .Replace("~~", string.Empty, StringComparison.Ordinal)
                .Replace("`", string.Empty, StringComparison.Ordinal)
                .Trim();
    }

    private static IEnumerable<string> EnumerateLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            yield return line;
    }
}
