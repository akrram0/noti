using System.Text.Json;

namespace Noti.App;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ThemePreference { get; set; } = "System";
    public bool IsDarkMode { get; set; } = true;
    public bool IsFluidAnimationEnabled { get; set; } = true;
    public bool IsTreeShortcutEnabled { get; set; } = true;
    public double EditorFontSize { get; set; } = 16.0;
    public double SurfaceOpacityPercent { get; set; } = 22.0;
    public string BackdropKind { get; set; } = "Blur";
    public string VaultDirectoryPath { get; set; } = string.Empty;
    public string UserName { get; set; } = string.IsNullOrWhiteSpace(Environment.UserName) ? "Writer" : Environment.UserName;
    public string UserAvatarPath { get; set; } = string.Empty;
    public List<string> PinnedNoteIds { get; set; } = new();
    public List<string> CollapsedFolders { get; set; } = new();
    public Dictionary<string, string> ItemColors { get; set; } = new();

    public static string AppDataDirectory
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");

            return Path.Combine(localAppData, "Noti");
        }
    }

    public static string SettingsFilePath => Path.Combine(AppDataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsFilePath;
            if (!File.Exists(path))
                return new AppSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var path = SettingsFilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tempPath = $"{path}.tmp";
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // Ignore transient IO failures during rapid UI toggles
        }
    }
}
