using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using unity_cli_ui.Interop;

namespace unity_cli_ui;

public partial class ModuleInstallerWindow : Window
{
    public ObservableCollection<UnityModuleInfo> Modules { get; } = [];
    public string HeaderText { get; }
    public IReadOnlyList<string> SelectedModuleIds => Modules
        .Where(module => module.IsSelected)
        .Select(module => module.Id)
        .ToArray();
    public bool DryRun => DryRunCheck.IsChecked == true;
    public bool AcceptEula => AcceptEulaCheck.IsChecked == true;

    public ModuleInstallerWindow(string editorVersion, IEnumerable<UnityModuleInfo> modules)
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            if (!AcrylicWindow.Enable(this))
            {
                var fallback = new SolidColorBrush(Color.FromRgb(243, 243, 243));
                TitleBarSurface.Background = fallback;
                HeaderSurface.Background = fallback;
            }
        };
        HeaderText = LocalizationService.Format("dialog.module.header", editorVersion);
        foreach (var module in modules)
        {
            Modules.Add(new UnityModuleInfo
            {
                Id = module.Id,
                Name = module.Name,
                Size = module.Size,
                Installed = false
            });
        }

        DataContext = this;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton is null)
        {
            return;
        }

        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = isMaximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = isMaximized
            ? LocalizationService.Get("common.restore")
            : LocalizationService.Get("common.maximize");
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var module in Modules)
        {
            module.IsSelected = true;
        }
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var module in Modules)
        {
            module.IsSelected = false;
        }
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if (!Modules.Any(module => module.IsSelected))
        {
            MessageBox.Show(
                this,
                LocalizationService.Get("message.selectModule"),
                LocalizationService.Get("dialog.noModuleSelected.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
