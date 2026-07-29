using System.Windows;

namespace unity_cli_ui;

public partial class InstallEditorConfirmationWindow : Window
{
    public InstallEditorConfirmationChoice Choice { get; private set; } = InstallEditorConfirmationChoice.Cancel;
    public string Version { get; }
    public string InstallDirectory { get; }
    public string ModuleText { get; }
    public string SummaryText { get; }

    public InstallEditorConfirmationWindow(
        string version,
        string installDirectory,
        IReadOnlyList<string> modules,
        bool dryRun)
    {
        Version = version;
        InstallDirectory = installDirectory;
        ModuleText = modules.Count == 0
            ? LocalizationService.Get("dialog.installConfirm.noModules")
            : string.Join(", ", modules);
        SummaryText = LocalizationService.Format(
            dryRun ? "dialog.installConfirm.previewSummary" : "dialog.installConfirm.summary",
            version);
        InitializeComponent();
        DataContext = this;
    }

    private void ModifyDirectory_Click(object sender, RoutedEventArgs e)
    {
        Choice = InstallEditorConfirmationChoice.ModifyDirectory;
        DialogResult = true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Choice = InstallEditorConfirmationChoice.Confirm;
        DialogResult = true;
    }
}

public enum InstallEditorConfirmationChoice
{
    Cancel,
    Confirm,
    ModifyDirectory
}
