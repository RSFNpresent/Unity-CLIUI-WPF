using System.IO;
using unity_cli_ui.Models;

namespace unity_cli_ui.Services;

public static class PackageSafetyPolicy
{
    public const string ExcludedFileName = "System.Security.Cryptography.Xml.dll";

    public static void ValidateManifestPackage(DirectPackagePlan package)
    {
        if (package.Url.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException($"Package {package.Id} does not use HTTPS.");
        }

        RejectExcludedFile(Path.GetFileName(package.Url.AbsolutePath));
        RejectExcludedFile(package.Id);
        RejectExcludedFile(package.Slug);

        if (package.Type is not ("EXE" or "ZIP" or "PO"))
        {
            throw new InvalidDataException($"Package {package.Id} has unsupported type {package.Type}.");
        }

        if (package.Type == "EXE")
        {
            var isUnityHost = package.Url.Host.Equals("download.unity3d.com", StringComparison.OrdinalIgnoreCase);
            var expectedPath = package.IsEditor
                ? package.Url.AbsolutePath.Contains("/Windows64EditorInstaller/", StringComparison.OrdinalIgnoreCase)
                : package.Url.AbsolutePath.Contains("/TargetSupportInstaller/", StringComparison.OrdinalIgnoreCase);
            if (!isUnityHost || !expectedPath || string.IsNullOrWhiteSpace(package.Integrity))
            {
                throw new InvalidDataException($"Executable package {package.Id} is not an integrity-protected Unity installer.");
            }
        }
    }

    public static void RejectExcludedFile(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            value.Contains(ExcludedFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Excluded test file was rejected: {ExcludedFileName}");
        }
    }
}
