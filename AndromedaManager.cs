using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Andromeda.Installer;

internal static class AndromedaManager
{
    private static readonly string[] TargetExeNames =
    [
        "Enemy On Board.exe",
        "EnemyOnBoard.exe",
        "enemy-on-board.exe"
    ];

    public static bool ShouldInstall(string gameDir)
    {
        // Limit Andromeda installation to Enemy on Board installs.
        foreach (var exe in TargetExeNames)
        {
            if (File.Exists(Path.Combine(gameDir, exe)))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<string?> InstallAsync(string gameDir, InstallProgressEventHandler? onProgress)
    {
        string? modUrl = await ResolveAndromedaAssetUrlAsync();
        if (string.IsNullOrWhiteSpace(modUrl))
        {
            return "Could not locate an Andromeda release asset in the latest GitHub release.";
        }

        onProgress?.Invoke(0.15, "Downloading Andromeda mod");

        string tempDir = Path.Combine(Path.GetTempPath(), "AndromedaInstaller_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string fileName = Path.GetFileName(new Uri(modUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "Andromeda.Mod.zip";
            }

            string artifactPath = Path.Combine(tempDir, fileName);
            await using (var fs = File.Create(artifactPath))
            {
                string? downloadError = await InstallerUtils.DownloadFileAsync(modUrl, fs, null);
                if (downloadError != null)
                {
                    return "Failed to download Andromeda mod: " + downloadError;
                }
            }

            onProgress?.Invoke(0.45, "Installing Andromeda mod");

            string modsDir = Path.Combine(gameDir, "Mods");
            Directory.CreateDirectory(modsDir);

            string ext = Path.GetExtension(artifactPath).ToLowerInvariant();
            if (ext == ".dll")
            {
                string target = Path.Combine(modsDir, Path.GetFileName(artifactPath));
                File.Copy(artifactPath, target, true);
            }
            else if (ext == ".zip")
            {
                string extractDir = Path.Combine(tempDir, "extract");
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(artifactPath, extractDir, true);

                var dlls = Directory
                    .GetFiles(extractDir, "*.dll", SearchOption.AllDirectories)
                    .Where(x => Regex.IsMatch(Path.GetFileName(x), "(?i)(andromeda|Andromeda)"))
                    .ToArray();

                if (dlls.Length == 0)
                {
                    return "Andromeda archive did not contain an Andromeda DLL.";
                }

                foreach (var dll in dlls)
                {
                    string target = Path.Combine(modsDir, Path.GetFileName(dll));
                    File.Copy(dll, target, true);
                }
            }
            else
            {
                return $"Unsupported Andromeda artifact format '{ext}'.";
            }

            onProgress?.Invoke(0.8, "Applying MelonLoader console settings");
            ApplyConsoleHide(gameDir);
            onProgress?.Invoke(1.0, "Andromeda installation complete");

            return null;
        }
        catch (Exception ex)
        {
            return "Andromeda install failed: " + ex.Message;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    private static async Task<string?> ResolveAndromedaAssetUrlAsync()
    {
        HttpResponseMessage response;
        try
        {
            response = await InstallerUtils.Http.GetAsync(Config.AndromedaReleaseLatestApi).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var release = JsonNode.Parse(body);
        var assets = release?["assets"]?.AsArray();
        if (assets == null)
        {
            return null;
        }

        var regex = new Regex(Config.AndromedaAssetPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        foreach (var asset in assets)
        {
            string? name = asset?["name"]?.ToString();
            string? url = asset?["browser_download_url"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (regex.IsMatch(name))
            {
                return url;
            }
        }

        return null;
    }

    private static void ApplyConsoleHide(string gameDir)
    {
        var targets = new (string filePath, string section, string key, string value)[]
        {
            (Path.Combine(gameDir, "MelonLoader", "Loader.cfg"), "Console", "Enabled", "false"),
            (Path.Combine(gameDir, "MelonLoader", "Loader.cfg"), "General", "HideConsole", "true"),
            (Path.Combine(gameDir, "UserData", "Loader.cfg"), "Console", "Enabled", "false"),
            (Path.Combine(gameDir, "UserData", "Loader.cfg"), "General", "HideConsole", "true"),
            (Path.Combine(gameDir, "UserData", "MelonPreferences.cfg"), "Console", "Enabled", "false"),
            (Path.Combine(gameDir, "UserData", "MelonPreferences.cfg"), "General", "HideConsole", "true")
        };

        foreach (var target in targets)
        {
            SetIniValue(target.filePath, target.section, target.key, target.value);
        }
    }

    private static void SetIniValue(string filePath, string section, string key, string value)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (!File.Exists(filePath))
        {
            File.WriteAllText(filePath, string.Empty);
        }

        string content = File.ReadAllText(filePath);
        var sectionRegex = new Regex($@"(?ms)^\[{Regex.Escape(section)}\]\s*(.*?)(?=^\[|\z)");
        var keyRegex = new Regex($@"(?im)^\s*{Regex.Escape(key)}\s*=.*$");

        if (sectionRegex.IsMatch(content))
        {
            string sectionBlock = sectionRegex.Match(content).Value;
            string updated = keyRegex.IsMatch(sectionBlock)
                ? keyRegex.Replace(sectionBlock, $"{key}={value}")
                : sectionBlock.TrimEnd() + Environment.NewLine + $"{key}={value}" + Environment.NewLine;

            content = content.Replace(sectionBlock, updated);
        }
        else
        {
            if (!string.IsNullOrEmpty(content) && !content.EndsWith("\n"))
            {
                content += Environment.NewLine;
            }

            content += $"[{section}]" + Environment.NewLine;
            content += $"{key}={value}" + Environment.NewLine;
        }

        File.WriteAllText(filePath, content);
    }
}
