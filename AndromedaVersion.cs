using Semver;

namespace Andromeda.Installer;

public enum AndromedaReleaseSource
{
    Stable,
    BleedingEdge
}

public class AndromedaVersion
{
    public required string TagName { get; init; }
    public required string DownloadUrl { get; init; }
    public required SemVersion Version { get; init; }
    public required AndromedaReleaseSource Source { get; init; }
    public bool IsPrerelease { get; init; }

    public override string ToString()
    {
        string label = Source == AndromedaReleaseSource.BleedingEdge ? " [Bleeding Edge]" : string.Empty;
        string preLabel = IsPrerelease ? " (pre-release)" : string.Empty;
        return $"v{Version}{preLabel}{label}";
    }
}
