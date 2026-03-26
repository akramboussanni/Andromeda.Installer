using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Semver;

namespace Andromeda.Installer;

internal sealed class AndromedaVersionInfo
{
    public required string StatusText { get; init; }
    public SemVersion? InstalledVersion { get; init; }
    public SemVersion? LatestVersion { get; init; }
}

internal sealed class MelonLoaderOptions
{
    public bool ConsoleEnabled { get; init; }
    public bool HideConsole { get; init; }
}

internal static class AndromedaManager
{
    private const string SteamHideConsoleArg = "--melonloader.hideconsole";

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
            CleanupLegacyMods(modsDir);

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
            _ = ApplySteamHideConsoleLaunchOption(gameDir);
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

    public static async Task<AndromedaVersionInfo> GetVersionInfoAsync(string gameDir)
    {
        if (!ShouldInstall(gameDir))
        {
            return new AndromedaVersionInfo
            {
                StatusText = "Andromeda checks are only available for Enemy On Board."
            };
        }

        var installedVersion = GetInstalledAndromedaVersion(gameDir);
        string? latestTag = await GetLatestAndromedaTagAsync().ConfigureAwait(false);
        var latestVersion = ParseVersionTag(latestTag);

        if (installedVersion == null)
        {
            return new AndromedaVersionInfo
            {
                StatusText = "Andromeda: Not installed",
                LatestVersion = latestVersion
            };
        }

        if (latestTag == null)
        {
            return new AndromedaVersionInfo
            {
                StatusText = $"Andromeda: Installed v{installedVersion} (latest unavailable)",
                InstalledVersion = installedVersion
            };
        }

        if (latestVersion == null)
        {
            return new AndromedaVersionInfo
            {
                StatusText = $"Andromeda: Installed v{installedVersion} (latest tag: {latestTag})",
                InstalledVersion = installedVersion
            };
        }

        int comparison = installedVersion.ComparePrecedenceTo(latestVersion);
        string status = comparison switch
        {
            0 => $"Andromeda: Up to date (v{installedVersion})",
            < 0 => $"Andromeda: Outdated (installed v{installedVersion}, latest v{latestVersion})",
            > 0 => $"Andromeda: Installed v{installedVersion} (ahead of latest v{latestVersion})"
        };

        return new AndromedaVersionInfo
        {
            StatusText = status,
            InstalledVersion = installedVersion,
            LatestVersion = latestVersion
        };
    }

    public static MelonLoaderOptions GetMelonLoaderOptions(string gameDir)
    {
        bool? consoleEnabled = null;
        bool? hideConsole = null;

        foreach (var target in GetLoaderConfigTargets(gameDir))
        {
            if (consoleEnabled == null)
            {
                var rawConsole = GetIniValue(target.filePath, "Console", "Enabled");
                if (TryParseBool(rawConsole, out var parsedConsole))
                {
                    consoleEnabled = parsedConsole;
                }
            }

            if (hideConsole == null)
            {
                var rawHide = GetIniValue(target.filePath, "General", "HideConsole");
                if (TryParseBool(rawHide, out var parsedHide))
                {
                    hideConsole = parsedHide;
                }
            }

            if (consoleEnabled != null && hideConsole != null)
            {
                break;
            }
        }

        return new MelonLoaderOptions
        {
            ConsoleEnabled = consoleEnabled ?? false,
            HideConsole = hideConsole ?? true
        };
    }

    public static string? SaveMelonLoaderOptions(string gameDir, MelonLoaderOptions options)
    {
        try
        {
            foreach (var target in GetLoaderConfigTargets(gameDir))
            {
                SetIniValue(target.filePath, "Console", "Enabled", options.ConsoleEnabled ? "true" : "false");
                SetIniValue(target.filePath, "General", "HideConsole", options.HideConsole ? "true" : "false");
            }

            return null;
        }
        catch (Exception ex)
        {
            return "Failed to save MelonLoader config: " + ex.Message;
        }
    }

    public static string? Uninstall(string gameDir)
    {
        try
        {
            string modsDir = Path.Combine(gameDir, "Mods");
            if (!Directory.Exists(modsDir))
            {
                return null;
            }

            foreach (var file in Directory.EnumerateFiles(modsDir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(file);
                if (!Regex.IsMatch(fileName, "(?i)(andromeda|parasite)"))
                {
                    continue;
                }

                File.Delete(file);
            }

            return null;
        }
        catch (Exception ex)
        {
            return "Failed to uninstall Andromeda: " + ex.Message;
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

    private static SemVersion? GetInstalledAndromedaVersion(string gameDir)
    {
        string modsDir = Path.Combine(gameDir, "Mods");
        if (!Directory.Exists(modsDir))
        {
            return null;
        }

        SemVersion? bestVersion = null;
        foreach (var file in Directory.EnumerateFiles(modsDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(file);
            if (!Regex.IsMatch(fileName, "(?i)andromeda"))
            {
                continue;
            }

            var version = GetAssemblySemVersion(file) ?? ParseVersionFromFileName(fileName);
            if (version == null)
            {
                continue;
            }

            if (bestVersion == null || version.ComparePrecedenceTo(bestVersion) > 0)
            {
                bestVersion = version;
            }
        }

        return bestVersion;
    }

    private static async Task<string?> GetLatestAndromedaTagAsync()
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
        return JsonNode.Parse(body)?["tag_name"]?.ToString();
    }

    private static SemVersion? ParseVersionTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        string normalized = tag.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        if (SemVersion.TryParse(normalized, SemVersionStyles.Any, out var version))
        {
            return version;
        }

        return null;
    }

    private static SemVersion? ParseVersionFromFileName(string fileName)
    {
        var match = Regex.Match(fileName, @"(?<ver>\d+\.\d+\.\d+(?:[-+][0-9A-Za-z\.-]+)?)");
        if (!match.Success)
        {
            return null;
        }

        return ParseVersionTag(match.Groups["ver"].Value);
    }

    private static SemVersion? GetAssemblySemVersion(string filePath)
    {
        try
        {
            var rawVersion = AssemblyName.GetAssemblyName(filePath).Version;
            if (rawVersion == null)
            {
                return null;
            }

            int patch = rawVersion.Build < 0 ? 0 : rawVersion.Build;
            string prerelease = rawVersion.Revision > 0 ? $"ci.{rawVersion.Revision}" : string.Empty;
            return SemVersion.ParsedFrom(rawVersion.Major, rawVersion.Minor, patch, prerelease);
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyConsoleHide(string gameDir)
    {
        _ = SaveMelonLoaderOptions(gameDir, new MelonLoaderOptions
        {
            ConsoleEnabled = false,
            HideConsole = true
        });
    }

    private static void CleanupLegacyMods(string modsDir)
    {
        foreach (var file in Directory.EnumerateFiles(modsDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(file);
            if (!Regex.IsMatch(fileName, "(?i)parasite"))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch
            {
                // Non-fatal cleanup, Andromeda installation should still proceed.
            }
        }
    }

    private static bool ApplySteamHideConsoleLaunchOption(string gameDir)
    {
        if (!TryResolveSteamAppId(gameDir, out string? appId))
        {
            return false;
        }

        bool updatedAny = false;
        foreach (var localConfig in EnumerateSteamLocalConfigFiles())
        {
            string content;
            try
            {
                content = File.ReadAllText(localConfig);
            }
            catch
            {
                continue;
            }

            if (!TryUpsertLaunchOption(content, appId!, SteamHideConsoleArg, out string updatedContent))
            {
                continue;
            }

            try
            {
                File.WriteAllText(localConfig, updatedContent);
                updatedAny = true;
            }
            catch
            {
                // Ignore local config write errors.
            }
        }

        return updatedAny;
    }

    private static bool TryResolveSteamAppId(string gameDir, out string? appId)
    {
        appId = null;

        var steamAppsDir = Path.GetFullPath(Path.Combine(gameDir, "..", ".."));
        if (!Directory.Exists(steamAppsDir)
            || !string.Equals(Path.GetFileName(steamAppsDir), "steamapps", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string installDirName = Path.GetFileName(gameDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (var manifestPath in Directory.EnumerateFiles(steamAppsDir, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
        {
            string manifest;
            try
            {
                manifest = File.ReadAllText(manifestPath);
            }
            catch
            {
                continue;
            }

            var installDirMatch = Regex.Match(manifest, "\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (!installDirMatch.Success)
            {
                continue;
            }

            string manifestInstallDir = installDirMatch.Groups[1].Value;
            if (!string.Equals(manifestInstallDir, installDirName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var appIdMatch = Regex.Match(manifest, "\"appid\"\\s*\"(\\d+)\"", RegexOptions.IgnoreCase);
            if (!appIdMatch.Success)
            {
                return false;
            }

            appId = appIdMatch.Groups[1].Value;
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateSteamLocalConfigFiles()
    {
        var roots = new List<string>();

#if WINDOWS
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam"));
#else
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "steam"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support", "Steam"));
#endif

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            string userdataDir = Path.Combine(root, "userdata");
            if (!Directory.Exists(userdataDir))
            {
                continue;
            }

            foreach (var userDir in Directory.EnumerateDirectories(userdataDir))
            {
                string localConfig = Path.Combine(userDir, "config", "localconfig.vdf");
                if (File.Exists(localConfig))
                {
                    yield return localConfig;
                }
            }
        }
    }

    private static bool TryUpsertLaunchOption(string content, string appId, string launchArg, out string updatedContent)
    {
        updatedContent = content;

        string appToken = $"\"{appId}\"";
        int appStart = content.IndexOf(appToken, StringComparison.Ordinal);
        if (appStart < 0)
        {
            return false;
        }

        int blockOpen = content.IndexOf('{', appStart);
        if (blockOpen < 0)
        {
            return false;
        }

        int depth = 0;
        int blockClose = -1;
        for (int i = blockOpen; i < content.Length; i++)
        {
            char ch = content[i];
            if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    blockClose = i;
                    break;
                }
            }
        }

        if (blockClose < 0)
        {
            return false;
        }

        string block = content.Substring(appStart, blockClose - appStart + 1);
        var launchRegex = new Regex("(?im)^([\\t ]*\"LaunchOptions\"[\\t ]*\")(.*?)(\"[\\t ]*)$");
        string updatedBlock;

        if (launchRegex.IsMatch(block))
        {
            updatedBlock = launchRegex.Replace(block, match =>
            {
                string existing = match.Groups[2].Value;
                if (Regex.IsMatch(existing, $"(^|\\s){Regex.Escape(launchArg)}($|\\s)", RegexOptions.IgnoreCase))
                {
                    return match.Value;
                }

                string merged = string.IsNullOrWhiteSpace(existing)
                    ? launchArg
                    : (existing.Trim() + " " + launchArg);

                return match.Groups[1].Value + merged + match.Groups[3].Value;
            }, 1);
        }
        else
        {
            int insertAt = block.LastIndexOf('}');
            if (insertAt <= 0)
            {
                return false;
            }

            string indent = DetectIndentBeforeClosingBrace(block, insertAt);
            var sb = new StringBuilder(block);
            sb.Insert(insertAt, $"{indent}\"LaunchOptions\"\t\t\"{launchArg}\"{Environment.NewLine}");
            updatedBlock = sb.ToString();
        }

        if (updatedBlock == block)
        {
            return false;
        }

        updatedContent = content.Substring(0, appStart) + updatedBlock + content.Substring(blockClose + 1);
        return true;
    }

    private static string DetectIndentBeforeClosingBrace(string block, int closingBraceIndex)
    {
        int lineStart = block.LastIndexOf('\n', closingBraceIndex);
        if (lineStart < 0)
        {
            return "\t\t";
        }

        int i = lineStart + 1;
        while (i < block.Length && (block[i] == '\t' || block[i] == ' '))
        {
            i++;
        }

        string braceIndent = block.Substring(lineStart + 1, i - lineStart - 1);
        return braceIndent + "\t";
    }

    private static (string filePath, string displayName)[] GetLoaderConfigTargets(string gameDir)
    {
        return
        [
            (Path.Combine(gameDir, "MelonLoader", "Loader.cfg"), "MelonLoader/Loader.cfg"),
            (Path.Combine(gameDir, "UserData", "Loader.cfg"), "UserData/Loader.cfg"),
            (Path.Combine(gameDir, "UserData", "MelonPreferences.cfg"), "UserData/MelonPreferences.cfg")
        ];
    }

    private static string? GetIniValue(string filePath, string section, string key)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        string content = File.ReadAllText(filePath);
        var sectionRegex = new Regex($@"(?ms)^\[{Regex.Escape(section)}\]\s*(.*?)(?=^\[|\z)");
        var sectionMatch = sectionRegex.Match(content);
        if (!sectionMatch.Success)
        {
            return null;
        }

        var keyRegex = new Regex($@"(?im)^\s*{Regex.Escape(key)}\s*=\s*(?<value>.*?)\s*$");
        var keyMatch = keyRegex.Match(sectionMatch.Value);
        if (!keyMatch.Success)
        {
            return null;
        }

        return keyMatch.Groups["value"].Value;
    }

    private static bool TryParseBool(string? rawValue, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        string normalized = rawValue.Trim();
        if (normalized.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (normalized.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        return bool.TryParse(normalized, out value);
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
