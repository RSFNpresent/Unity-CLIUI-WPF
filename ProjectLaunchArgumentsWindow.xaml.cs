using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using unity_cli_ui.Interop;

namespace unity_cli_ui;

public partial class ProjectLaunchArgumentsWindow : Window
{
    public string HeaderText { get; }
    public string LaunchArguments => ArgumentsTextBox.Text.Trim();

    public ProjectLaunchArgumentsWindow(string projectName, string launchArguments)
    {
        InitializeComponent();
        HeaderText = projectName;
        DataContext = this;
        ArgumentsTextBox.Text = launchArguments;
        ArgumentsTextBox.CaretIndex = ArgumentsTextBox.Text.Length;
        ArgumentsTextBox.Focus();

        SourceInitialized += (_, _) =>
        {
            if (!AcrylicWindow.Enable(this))
            {
                var fallback = new SolidColorBrush(Color.FromRgb(243, 243, 243));
                TitleBarSurface.Background = fallback;
                HeaderSurface.Background = fallback;
            }
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
