namespace Noti.App.Models;

/// <summary>
/// Represents a hierarchical folder node in the Markdown vault tree.
/// </summary>
public sealed record VaultFolder(
    string Path,
    string DisplayName,
    int Depth,
    bool IsExpanded,
    int NoteCount,
    string ColorHex = "");
