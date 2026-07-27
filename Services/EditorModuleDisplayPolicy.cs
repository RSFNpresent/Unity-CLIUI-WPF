namespace unity_cli_ui.Services;

public static class EditorModuleDisplayPolicy
{
    public static bool UsesInstalledModulesOnly(
        ManagementMode mode,
        bool remoteCatalogAvailable) =>
        mode == ManagementMode.Direct || !remoteCatalogAvailable;
}
