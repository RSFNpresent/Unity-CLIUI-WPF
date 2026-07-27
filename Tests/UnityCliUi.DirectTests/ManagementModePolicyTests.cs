using unity_cli_ui.Services;

namespace UnityCliUi.DirectTests;

internal static class ManagementModePolicyTests
{
    public static Task RunAsync()
    {
        Require(!BackendModePolicy.UsesDirectDownloads(ManagementMode.Auto));
        Require(BackendModePolicy.UsesDirectDownloads(ManagementMode.Direct));
        Require(!BackendModePolicy.UsesDirectDownloads(ManagementMode.UnityCli));

        Require(EditorModuleDisplayPolicy.UsesInstalledModulesOnly(
            ManagementMode.Direct,
            remoteCatalogAvailable: true));
        Require(EditorModuleDisplayPolicy.UsesInstalledModulesOnly(
            ManagementMode.Auto,
            remoteCatalogAvailable: false));
        Require(EditorModuleDisplayPolicy.UsesInstalledModulesOnly(
            ManagementMode.UnityCli,
            remoteCatalogAvailable: false));
        Require(!EditorModuleDisplayPolicy.UsesInstalledModulesOnly(
            ManagementMode.Auto,
            remoteCatalogAvailable: true));
        Require(!EditorModuleDisplayPolicy.UsesInstalledModulesOnly(
            ManagementMode.UnityCli,
            remoteCatalogAvailable: true));

        var root = Path.Combine(Path.GetTempPath(), "UnityCliUi.ModulePolicyTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Editor", "Data", "PlaybackEngines", "AndroidPlayer"));
            Directory.CreateDirectory(Path.Combine(root, "Editor", "Data", "Documentation"));
            var installed = InstalledModuleCatalog.Read(root, ["custom-installed"]);
            Require(installed.Contains("android", StringComparer.OrdinalIgnoreCase));
            Require(installed.Contains("documentation", StringComparer.OrdinalIgnoreCase));
            Require(installed.Contains("custom-installed", StringComparer.OrdinalIgnoreCase));
            Require(!installed.Contains("webgl", StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        return Task.CompletedTask;
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Management mode policy assertion failed.");
        }
    }
}
