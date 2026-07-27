using System.IO;

namespace unity_cli_ui.Services;

public static class InstalledModuleCatalog
{
    public static IReadOnlyList<string> Read(
        string editorPath,
        IEnumerable<string> registeredModuleIds)
    {
        var modules = new HashSet<string>(
            registeredModuleIds.Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            modules.UnionWith(InstalledEditorScanner.DetectInstalledModules(editorPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // CLI/cache registrations remain usable when an editor directory cannot be fully scanned.
        }

        return modules
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
