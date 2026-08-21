using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using unity_cli_ui.Interop;
using unity_cli_ui.Services;

namespace unity_cli_ui;

public partial class CreateProjectWindow : Window
{
    public ObservableCollection<EditorInstallation> Editors { get; } = [];
    public UnityProjectCreationResult? CreationResult { get; private set; }
    public EditorInstallation? SelectedEditor => EditorComboBox.SelectedItem as EditorInstallation;

    private UnityProjectEditorMetadata? _editorMetadata;

    public CreateProjectWindow(IEnumerable<EditorInstallation> installedEditors)
    {
        InitializeComponent();
        foreach (var editor in installedEditors
                     .OrderByDescending(editor => editor.IsDefault)
                     .ThenByDescending(editor => editor.Version, StringComparer.OrdinalIgnoreCase))
        {
            Editors.Add(editor);
        }

        DataContext = this;
        LocationTextBox.Text = ResolveInitialLocation();
        EditorComboBox.SelectedIndex = Editors.Count == 0 ? -1 : 0;
        UpdatePreview();

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

    private static string ResolveInitialLocation()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents)
            ? documents
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void BrowseLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Get("project.create.selectLocation"),
            Multiselect = false
        };
        if (Directory.Exists(LocationTextBox.Text))
        {
            dialog.InitialDirectory = LocationTextBox.Text;
        }
        if (dialog.ShowDialog(this) == true)
        {
            LocationTextBox.Text = dialog.FolderName;
        }
    }

    private void ProjectInput_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void EditorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _editorMetadata = null;
        if (SelectedEditor is { } editor)
        {
            try
            {
                _editorMetadata = UnityProjectCreator.InspectEditor(editor.Version, editor.Path);
                SetError(string.Empty);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                SetError(exception.Message);
            }
        }
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (TargetPathText is null || PackageVersionsText is null || CreateButton is null)
        {
            return;
        }

        var parent = LocationTextBox?.Text.Trim() ?? string.Empty;
        var name = ProjectNameTextBox?.Text.Trim() ?? string.Empty;
        try
        {
            TargetPathText.Text = string.IsNullOrWhiteSpace(parent)
                ? name
                : Path.Combine(parent, name);
        }
        catch (ArgumentException)
        {
            TargetPathText.Text = string.Empty;
        }

        PackageVersionsText.Text = _editorMetadata is null
            ? LocalizationService.Get("project.create.packagesUnavailable")
            : LocalizationService.Format(
                "project.create.packageVersions",
                _editorMetadata.VisualStudioPackageVersion,
                _editorMetadata.VisualStudioCodePackageVersion);
        CreateButton.IsEnabled =
            !string.IsNullOrWhiteSpace(parent) &&
            !string.IsNullOrWhiteSpace(name) &&
            _editorMetadata is not null;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEditor is not { } editor)
        {
            return;
        }

        try
        {
            CreationResult = UnityProjectCreator.Create(new UnityProjectCreationRequest(
                LocationTextBox.Text,
                ProjectNameTextBox.Text,
                editor.Version,
                editor.Path));
            DialogResult = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
        {
            SetError(exception.Message);
        }
    }

    private void SetError(string message)
    {
        if (ErrorText is null)
        {
            return;
        }
        ErrorText.Text = message;
        ErrorText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }
}
