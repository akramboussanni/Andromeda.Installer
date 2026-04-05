using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
    public bool Enabled { get; init; } = true;
    public bool ShowConsole { get; init; }
}

internal static class AndromedaManager
{
    // ── Version list (populated by InitVersionsAsync) ──────────────────────
    public static List<AndromedaVersion> Versions { get; } = [];

    private static bool _bleedingEdgeEnabled;
    public static bool BleedingEdgeEnabled
    {
        get => _bleedingEdgeEnabled;
        set
        {
            _bleedingEdgeEnabled = value;
            SaveSettings();
        }
    }

    static AndromedaManager()
    {
        LoadSettings();
    }

    // Populate Versions from GitHub (both stable + bleeding-edge if enabled).
    public static async Task<bool> InitVersionsAsync(bool bleedingEdge)
    {
        Versions.Clear();

        bool stableOk = await FetchReleasesAsync(Config.AndromedaReleasesApi, AndromedaReleaseSource.Stable);

        if (bleedingEdge)
        {
            await FetchReleasesAsync(Config.AndromedaModReleasesApi, AndromedaReleaseSource.BleedingEdge);
        }

        // Sort: newest first (by semver precedence), stable over bleeding-edge within same version.
        Versions.Sort((a, b) =>
        {
            int cmp = b.Version.ComparePrecedenceTo(a.Version);
            if (cmp != 0) return cmp;
            // If same version, prefer stable
            return a.Source == AndromedaReleaseSource.Stable ? -1 : 1;
        });

        return stableOk;
    }

    private static async Task<bool> FetchReleasesAsync(string apiUrl, AndromedaReleaseSource source)
    {
        HttpResponseMessage response;
        try
        {
            response = await InstallerUtils.Http.GetAsync(apiUrl).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
            return false;

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var releases = JsonNode.Parse(body)?.AsArray();
        if (releases == null)
            return false;

        var assetRegex = new Regex(Config.AndromedaAssetPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        foreach (var release in releases)
        {
            if (release == null) continue;

            string? tagName = release["tag_name"]?.ToString();
            if (string.IsNullOrWhiteSpace(tagName)) continue;

            var semVer = ParseVersionTag(tagName);
            if (semVer == null) continue;

            bool prerelease = release["prerelease"]?.GetValue<bool>() ?? false;

            var assets = release["assets"]?.AsArray();
            if (assets == null) continue;

            string? downloadUrl = null;
            foreach (var asset in assets)
            {
                string? name = asset?["name"]?.ToString();
                string? url = asset?["browser_download_url"]?.ToString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
                if (assetRegex.IsMatch(name))
                {
                    downloadUrl = url;
                    break;
                }
            }

            if (downloadUrl == null) continue;

            Versions.Add(new AndromedaVersion
            {
                TagName = tagName,
                DownloadUrl = downloadUrl,
                Version = semVer,
                Source = source,
                IsPrerelease = prerelease
            });
        }

        return true;
    }

    private static void LoadSettings()
    {
        try
        {
            if (!File.Exists(Config.AndromedaSettingsPath)) return;
            string json = File.ReadAllText(Config.AndromedaSettingsPath);
            var node = JsonNode.Parse(json);
            _bleedingEdgeEnabled = node?["bleedingEdge"]?.GetValue<bool>() ?? false;
        }
        catch { /* defaults */ }
    }

    private static void SaveSettings()
    {
        try
        {
            string? dir = Path.GetDirectoryName(Config.AndromedaSettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var obj = new { bleedingEdge = _bleedingEdgeEnabled };
            File.WriteAllText(Config.AndromedaSettingsPath, JsonSerializer.Serialize(obj));
        }
        catch { }
    }

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

    // Install the latest Andromeda (using the appropriate latest API based on bleeding edge).
    public static async Task<string?> InstallAsync(string gameDir, InstallProgressEventHandler? onProgress)
    {
        string latestApi = _bleedingEdgeEnabled
            ? Config.AndromedaModReleaseLatestApi
            : Config.AndromedaReleaseLatestApi;

        string? modUrl = await ResolveLatestAndromedaAssetUrlAsync(latestApi);
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

    // Install a specific Andromeda version.
    public static async Task<string?> InstallVersionAsync(string gameDir, AndromedaVersion version, InstallProgressEventHandler? onProgress)
    {
        return await InstallFromUrlAsync(gameDir, version.DownloadUrl, onProgress);
    }

    // Shared install-from-URL core used by both overloads.
    private static async Task<string?> InstallFromUrlAsync(string gameDir, string modUrl, InstallProgressEventHandler? onProgress)
    {
        onProgress?.Invoke(0.15, "Downloading Andromeda mod");

        string tempDir = Path.Combine(Path.GetTempPath(), "AndromedaInstaller_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string fileName = Path.GetFileName(new Uri(modUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "Andromeda.Mod.zip";

            string artifactPath = Path.Combine(tempDir, fileName);
            await using (var fs = File.Create(artifactPath))
            {
                string? downloadError = await InstallerUtils.DownloadFileAsync(modUrl, fs, null);
                if (downloadError != null)
                    return "Failed to download Andromeda mod: " + downloadError;
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
                    return "Andromeda archive did not contain an Andromeda DLL.";

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
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
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
        string latestApi = _bleedingEdgeEnabled
            ? Config.AndromedaModReleaseLatestApi
            : Config.AndromedaReleaseLatestApi;
        string? latestTag = await GetLatestAndromedaTagAsync(latestApi).ConfigureAwait(false);
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
        bool? enabled = null;
        bool? showConsole = null;

        foreach (var target in GetLoaderConfigTargets(gameDir))
        {
            if (enabled == null)
            {
                var rawDisable = GetIniValue(target.filePath, "loader", "disable");
                if (TryParseBool(rawDisable, out var parsedDisable))
                {
                    enabled = !parsedDisable;
                }
            }

            if (showConsole == null)
            {
                var rawHide = GetIniValue(target.filePath, "console", "hide_console") 
                           ?? GetIniValue(target.filePath, "General", "HideConsole");
                if (TryParseBool(rawHide, out var parsedHide))
                {
                    showConsole = !parsedHide;
                }
            }

            if (enabled != null && showConsole != null)
            {
                break;
            }
        }

        return new MelonLoaderOptions
        {
            Enabled = enabled ?? true,
            ShowConsole = showConsole ?? false
        };
    }

    public static string? SaveMelonLoaderOptions(string gameDir, MelonLoaderOptions options)
    {
        try
        {
            foreach (var target in GetLoaderConfigTargets(gameDir))
            {
                // Old keys (v0.5)
                SetIniValue(target.filePath, "Console", "Enabled", options.ShowConsole ? "true" : "false");
                SetIniValue(target.filePath, "General", "HideConsole", !options.ShowConsole ? "true" : "false");
                
                // New keys (v0.6+)
                SetIniValue(target.filePath, "console", "hide_console", !options.ShowConsole ? "true" : "false");
                SetIniValue(target.filePath, "loader", "disable", !options.Enabled ? "true" : "false");
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

    private static async Task<string?> ResolveLatestAndromedaAssetUrlAsync(string apiUrl)
    {
        HttpResponseMessage response;
        try
        {
            response = await InstallerUtils.Http.GetAsync(apiUrl).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
            return null;

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var release = JsonNode.Parse(body);
        var assets = release?["assets"]?.AsArray();
        if (assets == null)
            return null;

        var regex = new Regex(Config.AndromedaAssetPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        foreach (var asset in assets)
        {
            string? name = asset?["name"]?.ToString();
            string? url = asset?["browser_download_url"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
            if (regex.IsMatch(name)) return url;
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

    private static async Task<string?> GetLatestAndromedaTagAsync(string apiUrl)
    {
        HttpResponseMessage response;
        try
        {
            response = await InstallerUtils.Http.GetAsync(apiUrl).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
            return null;

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
            Enabled = true,
            ShowConsole = false
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

    // Legacy config updater removed to rely on Loader.cfg overrides

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
