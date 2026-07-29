using System.IO;
using unity_cli_ui.Models;

namespace unity_cli_ui.Services;

public enum ManagementMode
{
    Auto,
    Direct,
    UnityCli
}

public static class BackendModePolicy
{
    public static bool UsesDirectDownloads(ManagementMode mode) => mode == ManagementMode.Direct;
}

public enum DirectInstallPhase
{
    Planning,
    Downloading,
    Verifying,
    Installing,
    Completed,
    Failed,
    Cancelled
}

public sealed record DirectOperationProgress(
    DirectInstallPhase Phase,
    string Detail,
    double? Percent = null,
    bool WriteToLog = false);

public sealed record DirectInstallRequest(
    string Version,
    string InstallRoot,
    IReadOnlyList<string> ModuleIds,
    bool DryRun,
    bool AcceptEula,
    string PackageCacheDirectory = "",
    bool KeepPackageCache = true);

public sealed record DirectModuleInstallRequest(
    string Version,
    string EditorPath,
    IReadOnlyList<string> ModuleIds,
    bool DryRun,
    bool AcceptEula,
    string PackageCacheDirectory = "",
    bool KeepPackageCache = true);

public sealed record DirectInstallResult(
    string Version,
    string EditorPath,
    IReadOnlyList<string> InstalledModuleIds,
    bool DryRun);

public sealed record DirectModuleStatus(
    UnityModulePackage Package,
    bool Installed);

public sealed record InstalledEditorInfo(
    string Version,
    string Path,
    string Architecture,
    IReadOnlyList<string> ModuleIds);

public sealed class DirectEditorState
{
    public string Version { get; init; } = string.Empty;
    public string Revision { get; init; } = string.Empty;
    public string EditorPath { get; init; } = string.Empty;
    public HashSet<string> InstalledModuleIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class DirectInstallTransaction
{
    public string Id { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string EditorPath { get; init; } = string.Empty;
    public DirectInstallPhase Phase { get; init; }
    public string Detail { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public static class DirectInstallPaths
{
    private static readonly string AppDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "unityCLI-UI");

    public static string Packages => Path.Combine(AppDataRoot, "packages");
    public static string Staging => Path.Combine(AppDataRoot, "staging");
    public static string State => Path.Combine(AppDataRoot, "install-state");

    public static string DefaultEditorRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Unity",
        "Editors");
}
