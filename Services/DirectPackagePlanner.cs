using System.IO;
using unity_cli_ui.Models;

namespace unity_cli_ui.Services;

public sealed record PlannedModulePackage(UnityModulePackage Package, string? ParentId);

public sealed record DirectModulePlan(
    IReadOnlyList<UnityModulePackage> SelectedModules,
    IReadOnlyList<PlannedModulePackage> Packages);

public static class DirectPackagePlanner
{
    public static DirectModulePlan Build(
        string editorVersion,
        IReadOnlyList<UnityModulePackage> availableModules,
        IReadOnlyList<string> requestedIds)
    {
        var selected = new List<UnityModulePackage>();
        foreach (var requestedId in requestedIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var module = availableModules.FirstOrDefault(item =>
                !item.Hidden &&
                string.Equals(item.Id, requestedId, StringComparison.OrdinalIgnoreCase));
            if (module is null)
            {
                throw new InvalidOperationException($"Module {requestedId} is not available for this exact Unity version.");
            }
            selected.Add(module);
        }

        var packages = new List<PlannedModulePackage>();
        foreach (var module in selected)
        {
            AddPackage(editorVersion, module, null, packages);
        }
        return new DirectModulePlan(selected, packages);
    }

    private static void AddPackage(
        string editorVersion,
        UnityModulePackage package,
        string? parentId,
        ICollection<PlannedModulePackage> packages)
    {
        if (string.IsNullOrWhiteSpace(package.Slug) ||
            !package.Slug.StartsWith(editorVersion + "-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Module {package.Id} does not belong to Unity {editorVersion}: {package.Slug}");
        }
        packages.Add(new PlannedModulePackage(package, parentId));
        foreach (var child in package.SubModules)
        {
            AddPackage(editorVersion, child, package.Id, packages);
        }
    }
}
