using System.Diagnostics;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace unity_cli_ui.Controls;

public partial class AboutPanel : UserControl
{
    private const string FallbackRepositoryUrl =
        "https://github.com/RSFNpresent/Unity-CLIUI-WPF";

    public string ProductVersion { get; }
    public string RepositoryUrl { get; }
    public Uri RepositoryUri { get; }

    public AboutPanel()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AboutPanel).Assembly;
        ProductVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        RepositoryUrl = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "RepositoryUrl")?
            .Value ?? FallbackRepositoryUrl;
        RepositoryUri = new Uri(RepositoryUrl, UriKind.Absolute);
        InitializeComponent();
    }

    private void Repository_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
