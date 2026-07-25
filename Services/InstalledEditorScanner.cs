using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace unity_cli_ui.Services;

public static partial class InstalledEditorScanner
{
    public static IReadOnlyList<InstalledEditorInfo> Scan(string editorInstallRoot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(editorInstallRoot) || !Directory.Exists(editorInstallRoot))
        {
            return [];
        }

        var candidates = new List<string>();
        AddCandidate(editorInstallRoot, candidates);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(editorInstallRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddCandidate(directory, candidates);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => CreateInfo(path, cancellationToken))
            .Where(info => info is not null)
            .Cast<InstalledEditorInfo>()
            .GroupBy(info => info.Version, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(info => info.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> DetectInstalledModules(string editorPath)
    {
        var root = Directory.Exists(editorPath)
            ? editorPath
            : Directory.GetParent(Directory.GetParent(editorPath)?.FullName ?? string.Empty)?.FullName ?? string.Empty;
        var playbackEngines = Path.Combine(root, "Editor", "Data", "PlaybackEngines");
        if (!Directory.Exists(playbackEngines))
        {
            return [];
        }

        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIfExists(modules, "android", Path.Combine(playbackEngines, "AndroidPlayer"));
        AddIfExists(modules, "ios", Path.Combine(playbackEngines, "iOSSupport"));
        AddIfExists(modules, "appletv", Path.Combine(playbackEngines, "AppleTVSupport"));
        AddIfExists(modules, "visionos", Path.Combine(playbackEngines, "VisionOSSupport"));
        AddIfExists(modules, "webgl", Path.Combine(playbackEngines, "WebGLSupport"));
        AddIfExists(modules, "linux-il2cpp", Path.Combine(playbackEngines, "LinuxStandaloneSupport"));
        AddIfExists(modules, "mac-mono", Path.Combine(playbackEngines, "MacStandaloneSupport"));

        var windowsSupport = Path.Combine(playbackEngines, "WindowsStandaloneSupport");
        if (Directory.Exists(windowsSupport))
        {
            if (Directory.EnumerateFiles(windowsSupport, "*il2cpp*", SearchOption.AllDirectories).Any())
            {
                modules.Add("windows-il2cpp");
            }
            if (Directory.EnumerateDirectories(windowsSupport, "*Server*", SearchOption.AllDirectories).Any())
            {
                modules.Add("windows-server");
            }
        }

        var localization = Path.Combine(root, "Editor", "Data", "Localization");
        if (Directory.Exists(localization))
        {
            foreach (var file in Directory.EnumerateFiles(localization, "*.po", SearchOption.TopDirectoryOnly))
            {
                modules.Add("language-" + Path.GetFileNameWithoutExtension(file));
            }
        }
        if (Directory.Exists(Path.Combine(root, "Editor", "Data", "Documentation")))
        {
            modules.Add("documentation");
        }
        return modules.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddCandidate(string root, ICollection<string> candidates)
    {
        var unity = Path.Combine(root, "Editor", "Unity.exe");
        if (File.Exists(unity))
        {
            candidates.Add(root);
        }
    }

    private static InstalledEditorInfo? CreateInfo(string root, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unity = Path.Combine(root, "Editor", "Unity.exe");
        if (!File.Exists(unity))
        {
            return null;
        }
        var versionInfo = FileVersionInfo.GetVersionInfo(unity);
        var productVersion = versionInfo.ProductVersion ?? versionInfo.FileVersion ?? string.Empty;
        var match = UnityVersionPattern().Match(productVersion);
        var version = match.Success ? match.Value : Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        return new InstalledEditorInfo(version, root, "x86_64", DetectInstalledModules(root));
    }

    private static void AddIfExists(ISet<string> modules, string id, string path)
    {
        if (Directory.Exists(path))
        {
            modules.Add(id);
        }
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+[abfp]\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnityVersionPattern();
}
