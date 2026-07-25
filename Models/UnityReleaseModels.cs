using System.Text.Json.Serialization;

namespace unity_cli_ui.Models;

public sealed class UnityReleaseResponse
{
    public int Offset { get; init; }
    public int Limit { get; init; }
    public int Total { get; init; }
    public List<UnityReleaseInfo> Results { get; init; } = [];
}

public sealed class UnityReleaseInfo
{
    public string Version { get; init; } = string.Empty;
    public DateTimeOffset ReleaseDate { get; init; }
    public List<UnityEditorDownload> Downloads { get; init; } = [];
    public string ShortRevision { get; init; } = string.Empty;
    public string Stream { get; init; } = string.Empty;
    public bool Recommended { get; init; }

    public UnityEditorDownload? GetWindowsX64Download() => Downloads.FirstOrDefault(download =>
        string.Equals(download.Platform, "WINDOWS", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(download.Architecture, "X86_64", StringComparison.OrdinalIgnoreCase));
}

public sealed class UnityEditorDownload
{
    public string Url { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public UnityPackageSize DownloadSize { get; init; } = new();
    public UnityPackageSize InstalledSize { get; init; } = new();
    public List<UnityModulePackage> Modules { get; init; } = [];
    public string? Integrity { get; init; }
}

public sealed class UnityModulePackage
{
    public string Id { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public UnityPackageSize DownloadSize { get; init; } = new();
    public UnityPackageSize InstalledSize { get; init; } = new();
    public bool Required { get; init; }
    public bool Hidden { get; init; }
    public bool PreSelected { get; init; }
    public string? Integrity { get; init; }
    public List<UnityModulePackage> SubModules { get; init; } = [];
    public UnityExtractedPathRename? ExtractedPathRename { get; init; }
    public string? Destination { get; init; }
    public List<UnityPackageEula> Eula { get; init; } = [];
}

public sealed class UnityPackageSize
{
    public long Value { get; init; }
    public string Unit { get; init; } = "BYTE";
}

public sealed class UnityExtractedPathRename
{
    [JsonPropertyName("from")]
    public string From { get; init; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; init; } = string.Empty;
}

public sealed class UnityPackageEula
{
    public string Url { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record DirectPackagePlan(
    string Id,
    string Slug,
    string Name,
    Uri Url,
    string Type,
    long DownloadSize,
    string? Integrity,
    string? Destination,
    UnityExtractedPathRename? ExtractedPathRename,
    bool IsEditor,
    string? ParentId);
