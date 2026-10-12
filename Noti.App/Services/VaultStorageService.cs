using Microsoft.UI.Xaml;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Noti.App.Services;

/// <summary>
/// Handles local vault asset/image storage, clipboard bitmap persistence,
/// and Markdown image tag generation.
/// </summary>
internal static class VaultStorageService
{
    public static string ImagesDirectory => Path.Combine(AppSettings.AppDataDirectory, "images");

    public static bool IsSupportedImageExtension(string? ext) =>
        !string.IsNullOrWhiteSpace(ext) &&
        (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
         ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
         ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
         ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
         ext.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
         ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
         ext.Equals(".svg", StringComparison.OrdinalIgnoreCase));

    public static async Task<string> CopyImageToAssetsAndFormatMarkdownAsync(
        string sourceFilePath,
        TextAlignment alignment)
    {
        var imagesDir = ImagesDirectory;
        Directory.CreateDirectory(imagesDir);

        var rawStem = Path.GetFileNameWithoutExtension(sourceFilePath);
        var safeStem = string.Concat(rawStem.Select(ch => ch is ' ' or '(' or ')' or '[' or ']' ? '-' : ch)).Trim('-');
        if (string.IsNullOrWhiteSpace(safeStem))
            safeStem = "image";

        var ext = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".png";

        var destFileName = $"{safeStem}{ext}";
        var destPath = Path.Combine(imagesDir, destFileName);
        var fullSource = Path.GetFullPath(sourceFilePath);
        var fullDest = Path.GetFullPath(destPath);

        if (File.Exists(destPath) && !string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
        {
            destFileName = $"{safeStem}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}";
            destPath = Path.Combine(imagesDir, destFileName);
            fullDest = Path.GetFullPath(destPath);
        }

        if (!string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
            await Task.Run(() => File.Copy(sourceFilePath, destPath, overwrite: true));

        var altText = string.IsNullOrWhiteSpace(rawStem) ? "image" : rawStem;
        return alignment switch
        {
            TextAlignment.Center => $"<img src=\"{destFileName}\" alt=\"{altText.Replace('"', '\'')}\" align=\"center\" />",
            TextAlignment.Right => $"<img src=\"{destFileName}\" alt=\"{altText.Replace('"', '\'')}\" align=\"right\" />",
            _ => $"![{altText}]({destFileName})"
        };
    }

    public static async Task<string> SaveClipboardBitmapToAssetsAsync(RandomAccessStreamReference bitmapRef)
    {
        var imagesDir = ImagesDirectory;
        Directory.CreateDirectory(imagesDir);

        var fileName = $"pasted-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.png";
        var destPath = Path.Combine(imagesDir, fileName);

        using (var inputStream = await bitmapRef.OpenReadAsync())
        {
            var decoder = await BitmapDecoder.CreateAsync(inputStream);
            var softBmp = await decoder.GetSoftwareBitmapAsync();
            using var fileStream = File.Create(destPath);
            using var winrtOut = fileStream.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, winrtOut);
            encoder.SetSoftwareBitmap(softBmp);
            await encoder.FlushAsync();
        }

        return destPath;
    }
}
