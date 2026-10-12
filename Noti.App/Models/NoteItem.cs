using Noti.Core;

namespace Noti.App.Models;

/// <summary>
/// Represents a note item snapshot in the application layer.
/// </summary>
public sealed record NoteItem(
    string Id,
    string Title,
    string Snippet,
    string FolderPath,
    bool IsPinned,
    string ColorHex,
    DateTimeOffset UpdatedAt)
{
    public static NoteItem FromCore(Note note, string colorHex = "") =>
        new(
            note.Id,
            note.Title,
            note.Snippet,
            note.FolderPath,
            note.IsPinned,
            colorHex ?? string.Empty,
            note.UpdatedAt);
}
