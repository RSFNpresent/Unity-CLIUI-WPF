using System.Windows;

namespace unity_cli_ui;

public partial class CliInstallChoiceWindow : Window
{
    public CliInstallChoice Choice { get; private set; } = CliInstallChoice.Cancel;

    public CliInstallChoiceWindow()
    {
        InitializeComponent();
    }

    private void SwitchMode_Click(object sender, RoutedEventArgs e)
    {
        Choice = CliInstallChoice.SwitchToDirect;
        DialogResult = true;
    }

    private void InstallOnce_Click(object sender, RoutedEventArgs e)
    {
        Choice = CliInstallChoice.InstallOnceWithDirect;
        DialogResult = true;
    }
}

public enum CliInstallChoice
{
    Cancel,
    SwitchToDirect,
    InstallOnceWithDirect
}
