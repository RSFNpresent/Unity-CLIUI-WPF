using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using unity_cli_ui.Interop;

namespace unity_cli_ui;

public partial class MissingProjectEditorWindow : Window
{
    public ObservableCollection<EditorInstallation> Editors { get; } = [];
    public string HeaderText { get; }
    public string InstallButtonText { get; }
    public MissingProjectEditorAction SelectedAction { get; private set; }
    public EditorInstallation? SelectedEditor => InstalledEditorsList.SelectedItem as EditorInstallation;

    public MissingProjectEditorWindow(
        string projectName,
        string requiredVersion,
        IEnumerable<EditorInstallation> installedEditors)
    {
        InitializeComponent();
        HeaderText = LocalizationService.Format(
            "dialog.missingEditor.header",
            projectName,
            requiredVersion);
        InstallButtonText = LocalizationService.Format(
            "dialog.missingEditor.install",
            requiredVersion);

        foreach (var editor in installedEditors
                     .OrderByDescending(editor => editor.IsDefault)
                     .ThenByDescending(editor => editor.Version, StringComparer.OrdinalIgnoreCase))
        {
            Editors.Add(editor);
        }

        DataContext = this;
        EmptyEditorsText.Visibility = Editors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        InstalledEditorsList.SelectedIndex = Editors.Count == 0 ? -1 : 0;
        UseSelectedButton.IsEnabled = InstalledEditorsList.SelectedItem is not null;

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

    private void InstalledEditorsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UseSelectedButton is not null)
        {
            UseSelectedButton.IsEnabled = InstalledEditorsList.SelectedItem is not null;
        }
    }

    private void UseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEditor is null)
        {
            return;
        }

        SelectedAction = MissingProjectEditorAction.UseInstalled;
        DialogResult = true;
    }

    private void InstallRequired_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = MissingProjectEditorAction.InstallRequired;
        DialogResult = true;
    }
}

public enum MissingProjectEditorAction
{
    None,
    UseInstalled,
    InstallRequired
}
