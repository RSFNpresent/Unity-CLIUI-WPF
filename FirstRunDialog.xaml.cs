using System.Windows;
using System.Windows.Controls;
using unity_cli_ui.Services;

namespace unity_cli_ui;

public partial class FirstRunDialog : Window
{
    private bool _isInitializing = true;

    public string SelectedLanguage { get; private set; } = LocalizationService.Chinese;
    public ManagementMode? SelectedManagementMode { get; private set; }

    public FirstRunDialog(string currentLanguage)
    {
        InitializeComponent();
        if (string.Equals(currentLanguage, LocalizationService.English, StringComparison.OrdinalIgnoreCase))
        {
            EnglishLanguageRadio.IsChecked = true;
        }
        else
        {
            ChineseLanguageRadio.IsChecked = true;
        }
        _isInitializing = false;
    }

    private void Language_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string language })
        {
            return;
        }

        SelectedLanguage = language;
        if (!_isInitializing)
        {
            LocalizationService.SetLanguage(language);
        }
    }

    private void ManagementMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string mode } ||
            !Enum.TryParse<ManagementMode>(mode, ignoreCase: true, out var selectedMode) ||
            selectedMode == ManagementMode.Auto)
        {
            return;
        }

        SelectedManagementMode = selectedMode;
        ContinueButton.IsEnabled = true;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedManagementMode is null)
        {
            return;
        }

        DialogResult = true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
