using Gameloop.Vdf;
using Gameloop.Vdf.Linq;
using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace Andromeda.Installer.GameLaunchers;

#pragma warning disable CA1416

public class SteamLauncher : GameLauncher
{
    private static readonly string? steamPath;

    static SteamLauncher()
    {
#if WINDOWS
        var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        key ??= Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
        steamPath = (string?)key?.GetValue("InstallPath");
#elif LINUX
        steamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "steam");
#elif OSX
        steamPath = Path.Combine(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.Personal)), "Library", "Application Support", "Steam");
        if ((steamPath != null)
            && !Directory.Exists(steamPath))
            steamPath = "/Applications/Steam.app";
#endif
        if ((steamPath != null)
            && !Directory.Exists(steamPath))
            steamPath = null;
    }

    internal SteamLauncher() : base("/Assets/steam.png") { }

    public override void AddGames()
    {
        if (steamPath == null)
            return;

        var libDirs = GetSteamLibraryDirectories();

        foreach (var library in libDirs)
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps))
                continue;

            var acfs = Directory.EnumerateFiles(steamapps, "*.acf");
            foreach (var acfPath in acfs)
            {
                if (!TryReadAppManifest(acfPath, out var id, out var name, out var dirName))
                    continue;

                if (id == null || name == null || dirName == null)
                    continue;

                var appDir = Path.Combine(steamapps, "common", dirName);
                if (!Directory.Exists(appDir))
                    continue;

                var iconPath = Path.Combine(steamPath, "appcache", "librarycache", id);
                iconPath = Directory.Exists(iconPath)
                    ? Directory.EnumerateFiles(iconPath, "*.jpg").FirstOrDefault(x =>
                    {
                        var fileName = Path.GetFileName(x);
                        return !fileName.StartsWith("library") && !fileName.StartsWith("header") &&
                               !fileName.StartsWith("logo");
                    })
                    : null;

                GameManager.TryAddGame(appDir, name, this, iconPath, out _);
            }
        }
    }

    private static HashSet<string> GetSteamLibraryDirectories()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            steamPath!
        };

        var libPath = Path.Combine(steamPath!, "config", "libraryfolders.vdf");
        if (!File.Exists(libPath))
            return libraries;

        string raw;
        try
        {
            raw = File.ReadAllText(libPath);
        }
        catch
        {
            return libraries;
        }

        // Parse libraryfolders.vdf paths using regex so we support multiple Steam formats
        // without depending on VObject enumeration APIs.

        foreach (Match match in Regex.Matches(raw, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
        {
            var lib = match.Groups[1].Value.Replace("\\\\", "\\");
            if (!string.IsNullOrWhiteSpace(lib))
                libraries.Add(lib);
        }

        return libraries;
    }

    private static bool TryReadAppManifest(string acfPath, out string? id, out string? name, out string? dirName)
    {
        id = null;
        name = null;
        dirName = null;

        string raw;
        try
        {
            raw = File.ReadAllText(acfPath);
        }
        catch
        {
            return false;
        }

        try
        {
            var acf = VdfConvert.Deserialize(raw).Value;
            id = ((VProperty?)acf.FirstOrDefault(x => ((VProperty)x).Key == "appid"))?.Value?.ToString();
            name = ((VProperty?)acf.FirstOrDefault(x => ((VProperty)x).Key == "name"))?.Value?.ToString();
            dirName = ((VProperty?)acf.FirstOrDefault(x => ((VProperty)x).Key == "installdir"))?.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(dirName))
                return true;
        }
        catch
        {
            // regex fallback below
        }

        id = Regex.Match(raw, "\"appid\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
        name = Regex.Match(raw, "\"name\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
        dirName = Regex.Match(raw, "\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(dirName))
            return false;

        return true;
    }
}
