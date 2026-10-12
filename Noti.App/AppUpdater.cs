using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Noti.App;

public static class AppUpdater
{
    private const string VersionManifestUrl = "https://github.com/akrram0/noti/releases/latest/download/version.json";
    private const string DefaultInstallerUrl = "https://github.com/akrram0/noti/releases/latest/download/Noti-Setup-win-x64.exe";

    private static readonly HttpClient Http = CreateHttpClient();

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? new Version(1, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string CurrentVersionText => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public static async Task<(bool IsUpdateAvailable, string LatestVersion, string DownloadUrl)> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{VersionManifestUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (false, CurrentVersionText, DefaultInstallerUrl);

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var versionStr = root.TryGetProperty("version", out var vProp) ? vProp.GetString() : null;
            var downloadUrl = root.TryGetProperty("downloadUrl", out var dProp) ? dProp.GetString() : DefaultInstallerUrl;

            if (string.IsNullOrWhiteSpace(versionStr) || !Version.TryParse(versionStr.TrimStart('v', 'V'), out var remoteVersion))
                return (false, CurrentVersionText, DefaultInstallerUrl);

            var normalizedRemote = new Version(remoteVersion.Major, remoteVersion.Minor, Math.Max(0, remoteVersion.Build));
            var isNewer = normalizedRemote > CurrentVersion;
            return (isNewer, $"v{normalizedRemote}", string.IsNullOrWhiteSpace(downloadUrl) ? DefaultInstallerUrl : downloadUrl);
        }
        catch
        {
            return (false, CurrentVersionText, DefaultInstallerUrl);
        }
    }

    public static async Task<bool> DownloadAndInstallAsync(
        string downloadUrl,
        Action<string>? onProgress = null,
        Action? onBeforeExit = null,
        CancellationToken cancellationToken = default)
    {
        var tempDir = Path.GetTempPath();
        var id = Guid.NewGuid().ToString("N");
        var tempSetupPath = Path.Combine(tempDir, $"Noti-Setup-Update-{id}.exe");
        var handoffScriptPath = Path.Combine(tempDir, $"noti-update-handoff-{id}.cmd");

        try
        {
            onProgress?.Invoke("Downloading update...");
            using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = new FileStream(tempSetupPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    readTotal += read;
                    if (totalBytes > 0)
                    {
                        var pct = (int)((readTotal * 100) / totalBytes);
                        onProgress?.Invoke($"Downloading {pct}%...");
                    }
                }
            }

            onProgress?.Invoke("Installing & restarting...");

            var currentPid = Environment.ProcessId;
            var currentExe = Environment.ProcessPath;
            var defaultInstalledExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Noti",
                "Noti.exe");
            var targetExe = !string.IsNullOrWhiteSpace(currentExe) && File.Exists(currentExe)
                ? currentExe
                : defaultInstalledExe;

            var scriptContent =
                "@echo off\r\n" +
                "cd /d \"%TEMP%\"\r\n" +
                $"taskkill /F /PID {currentPid} >nul 2>&1\r\n" +
                "timeout /t 1 /nobreak >nul 2>&1\r\n" +
                $"start \"\" /wait \"{tempSetupPath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS\r\n" +
                "tasklist /FI \"IMAGENAME eq Noti.exe\" 2>nul | find /I \"Noti.exe\" >nul\r\n" +
                "if errorlevel 1 (\r\n" +
                $"  if exist \"{targetExe}\" start \"\" \"{targetExe}\"\r\n" +
                $"  if not exist \"{targetExe}\" if exist \"{defaultInstalledExe}\" start \"\" \"{defaultInstalledExe}\"\r\n" +
                ")\r\n" +
                $"del /f /q \"{tempSetupPath}\" >nul 2>&1\r\n" +
                "del /f /q \"%~f0\" >nul 2>&1\r\n";

            await File.WriteAllTextAsync(handoffScriptPath, scriptContent, cancellationToken).ConfigureAwait(false);

            try
            {
                onBeforeExit?.Invoke();
            }
            catch
            {
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{handoffScriptPath}\"\"",
                WorkingDirectory = tempDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            Process.GetCurrentProcess().Kill();
            return true;
        }
        catch
        {
            try
            {
                if (File.Exists(tempSetupPath))
                    File.Delete(tempSetupPath);
                if (File.Exists(handoffScriptPath))
                    File.Delete(handoffScriptPath);
            }
            catch
            {
            }
            onProgress?.Invoke("Update failed");
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Noti-Desktop-Updater/1.0");
        return client;
    }
}
