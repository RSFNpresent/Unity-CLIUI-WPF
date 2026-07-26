using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace unity_cli_ui.Services;

public sealed record UnityProjectCreationRequest(
    string ParentDirectory,
    string ProjectName,
    string EditorVersion,
    string EditorPath);

public sealed record UnityProjectEditorMetadata(
    string EditorVersion,
    string Revision,
    string VisualStudioPackageVersion,
    string VisualStudioCodePackageVersion);

public sealed record UnityProjectCreationResult(
    string ProjectPath,
    UnityProjectEditorMetadata EditorMetadata);

public static partial class UnityProjectCreator
{
    public const string VisualStudioPackageId = "com.unity.ide.visualstudio";
    public const string VisualStudioCodePackageId = "com.unity.ide.vscode";
    public const string VisualStudioCodePackageVersion = "1.2.5";

    public static UnityProjectEditorMetadata InspectEditor(string editorVersion, string editorPath)
    {
        if (!UnityVersionRegex().IsMatch(editorVersion))
        {
            throw new InvalidDataException(LocalizationService.Get("project.create.error.invalidEditorVersion"));
        }

        var executable = ResolveUnityExecutable(editorPath)
            ?? throw new FileNotFoundException(LocalizationService.Get("project.create.error.editorMissing"));
        var productVersion = FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? string.Empty;
        var installedVersion = ProductVersionRegex().Match(productVersion).Groups["version"].Value;
        if (!string.IsNullOrWhiteSpace(installedVersion) &&
            !string.Equals(installedVersion, editorVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(LocalizationService.Format(
                "project.create.error.editorVersionMismatch",
                editorVersion,
                installedVersion));
        }

        var packageManifestPath = Path.Combine(
            Path.GetDirectoryName(executable)!,
            "Data",
            "Resources",
            "PackageManager",
            "Editor",
            "manifest.json");
        if (!File.Exists(packageManifestPath))
        {
            throw new FileNotFoundException(
                LocalizationService.Get("project.create.error.packageMetadataMissing"),
                packageManifestPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(packageManifestPath));
        if (!document.RootElement.TryGetProperty("packages", out var packages) ||
            packages.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(LocalizationService.Get("project.create.error.packageMetadataInvalid"));
        }

        var visualStudioVersion = ReadPackageVersion(packages, VisualStudioPackageId)
            ?? throw new InvalidDataException(LocalizationService.Format(
                "project.create.error.packageVersionMissing",
                VisualStudioPackageId));
        var visualStudioCodeVersion = ReadPackageVersion(packages, VisualStudioCodePackageId)
            ?? VisualStudioCodePackageVersion;

        return new UnityProjectEditorMetadata(
            editorVersion,
            ReadRevision(productVersion),
            visualStudioVersion,
            visualStudioCodeVersion);
    }

    public static UnityProjectCreationResult Create(UnityProjectCreationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ParentDirectory))
        {
            throw new DirectoryNotFoundException(LocalizationService.Get("project.create.error.parentMissing"));
        }
        var parentDirectory = Path.GetFullPath(request.ParentDirectory.Trim());
        if (!Directory.Exists(parentDirectory))
        {
            throw new DirectoryNotFoundException(LocalizationService.Get("project.create.error.parentMissing"));
        }

        var projectName = request.ProjectName.Trim();
        ValidateProjectName(projectName);
        var projectPath = Path.Combine(parentDirectory, projectName);
        if (Directory.Exists(projectPath) || File.Exists(projectPath))
        {
            throw new IOException(LocalizationService.Format("project.create.error.targetExists", projectPath));
        }

        var metadata = InspectEditor(request.EditorVersion, request.EditorPath);
        var createdProjectDirectory = false;
        try
        {
            Directory.CreateDirectory(projectPath);
            createdProjectDirectory = true;
            Directory.CreateDirectory(Path.Combine(projectPath, "Assets"));
            Directory.CreateDirectory(Path.Combine(projectPath, "Packages"));
            Directory.CreateDirectory(Path.Combine(projectPath, "ProjectSettings"));

            WritePackageManifest(projectPath, metadata);
            WriteProjectVersion(projectPath, metadata);
            return new UnityProjectCreationResult(projectPath, metadata);
        }
        catch
        {
            if (createdProjectDirectory)
            {
                try
                {
                    Directory.Delete(projectPath, recursive: true);
                }
                catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
                {
                }
            }
            throw;
        }
    }

    private static string? ResolveUnityExecutable(string editorPath)
    {
        if (File.Exists(editorPath) &&
            string.Equals(Path.GetFileName(editorPath), "Unity.exe", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(editorPath);
        }

        if (!Directory.Exists(editorPath))
        {
            return null;
        }

        return new[]
        {
            Path.Combine(editorPath, "Editor", "Unity.exe"),
            Path.Combine(editorPath, "Unity.exe")
        }.FirstOrDefault(File.Exists);
    }

    private static string? ReadPackageVersion(JsonElement packages, string packageId)
    {
        if (!packages.TryGetProperty(packageId, out var package) ||
            package.ValueKind != JsonValueKind.Object ||
            !package.TryGetProperty("version", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var version = versionElement.GetString();
        return !string.IsNullOrWhiteSpace(version) && PackageVersionRegex().IsMatch(version)
            ? version
            : null;
    }

    private static string ReadRevision(string productVersion)
    {
        var match = ProductVersionRegex().Match(productVersion);
        return match.Success ? match.Groups["revision"].Value : string.Empty;
    }

    private static void ValidateProjectName(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName) ||
            projectName is "." or ".." ||
            projectName.EndsWith(' ') ||
            projectName.EndsWith('.') ||
            projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(Path.GetFileName(projectName), projectName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(LocalizationService.Get("project.create.error.invalidName"));
        }
    }

    private static void WritePackageManifest(string projectPath, UnityProjectEditorMetadata metadata)
    {
        var manifest = new
        {
            dependencies = new Dictionary<string, string>
            {
                [VisualStudioPackageId] = metadata.VisualStudioPackageVersion,
                [VisualStudioCodePackageId] = metadata.VisualStudioCodePackageVersion
            }
        };
        var content = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(projectPath, "Packages", "manifest.json"), content + Environment.NewLine);
    }

    private static void WriteProjectVersion(string projectPath, UnityProjectEditorMetadata metadata)
    {
        var lines = new List<string> { $"m_EditorVersion: {metadata.EditorVersion}" };
        if (!string.IsNullOrWhiteSpace(metadata.Revision))
        {
            lines.Add($"m_EditorVersionWithRevision: {metadata.EditorVersion} ({metadata.Revision})");
        }
        File.WriteAllText(
            Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt"),
            string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+[a-z]\d+(?:[a-z]\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex UnityVersionRegex();

    [GeneratedRegex(@"^(?<version>\d+\.\d+\.\d+[a-z]\d+(?:[a-z]\d+)?)_(?<revision>[0-9a-f]{12,40})$", RegexOptions.IgnoreCase)]
    private static partial Regex ProductVersionRegex();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$")]
    private static partial Regex PackageVersionRegex();
}
