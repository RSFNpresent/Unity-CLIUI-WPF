using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using unity_cli_ui.Interop;
using unity_cli_ui.Services;

namespace unity_cli_ui;

public partial class MainWindow : Window
{
    private const string DocsUrl = "https://docs.unity.com/zh-cn/unity-cli/unity-cli";
    private const string UsageDocsUrl = "https://docs.unity.com/zh-cn/unity-cli/use-unity-cli";
    private const string CliInstallDocsUrl = "https://docs.unity.com/zh-cn/unity-cli/use-unity-cli#install-the-unity-cli";
    private const string ReferenceDocsUrl = "https://docs.unity.com/zh-cn/unity-cli/unity-cli-reference";
    private const string CliInstallScriptUrl = "https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1";
    private const string UnityPipelinePackageId = "com.unity.pipeline";
    private const string UnityAiAssistantPackageId = "com.unity.ai.assistant";
    private const string UnityAiAssistantPackageVersion = "2.16.0-pre.1";
    private static readonly Regex AnsiEscapePattern = new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled);
    private static readonly Regex PercentPattern = new(@"(?<!\d)(?<value>\d{1,3}(?:\.\d+)?)\s*%", RegexOptions.Compiled);

    private readonly UnityCliService _cli = new();
    private readonly Queue<string> _recentOutput = new();
    private bool _isBusy;
    private string _loadedModulesVersion = string.Empty;
    private CliTaskItem? _currentTask;
    private CancellationTokenSource? _scanCancellation;
    private string _activeEditorFilter = "All";
    private string _projectSortMode = "LastOpened";
    private bool _projectSortDescending = true;
    private bool _isInitializingLanguage;
    private string _currentPage = "Dashboard";

    public ObservableCollection<EditorInstallation> InstalledEditors { get; } = [];
    public ObservableCollection<EditorInstallation> FilteredInstalledEditors { get; } = [];
    public ObservableCollection<EditorRelease> AvailableEditors { get; } = [];
    public ObservableCollection<UnityModuleInfo> AvailableModules { get; } = [];
    public ObservableCollection<UnityModuleInfo> InstalledModules { get; } = [];
    public ObservableCollection<UnityModuleInfo> InstallableModules { get; } = [];
    public ObservableCollection<CliTaskItem> Tasks { get; } = [];
    public ObservableCollection<RecentProject> RecentProjects { get; } = [];

    public MainWindow()
    {
        LocalizationService.Initialize(LoadManagerLanguage());
        InitializeComponent();
        DataContext = this;
        CliScriptInstallPathText.Text = LoadCliInstallDirectory();
        _isInitializingLanguage = true;
        LanguageCombo.SelectedValue = LocalizationService.CurrentLanguage;
        _isInitializingLanguage = false;
        LoadEditorInstallationsCache();
        LoadRecentProjects();
        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        Closed += (_, _) =>
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            SaveEditorInstallationsCache();
            SaveManagerSettings();
        };
        SourceInitialized += (_, _) =>
        {
            if (!AcrylicWindow.Enable(this))
            {
                var fallback = new SolidColorBrush(Color.FromRgb(243, 243, 243));
                TitleBarSurface.Background = fallback;
                NavigationSurface.Background = fallback;
            }
        };
        Loaded += MainWindow_Loaded;
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

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DetectCli();
        if (!string.IsNullOrWhiteSpace(_cli.ExecutablePath))
        {
            await VerifyCliAsync();
        }
        else
        {
            ShowPage("Settings");
        }
    }

    private void DetectCli()
    {
        var names = new[]
        {
            "unitycli-windows-x64.exe",
            "unity-cli.exe",
            "unity.exe"
        };

        var directories = new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }
            .Concat(GetPathDirectories(EnvironmentVariableTarget.Process))
            .Concat(GetPathDirectories(EnvironmentVariableTarget.User))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        string? discoveredPath = null;
        foreach (var directory in directories)
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(directory, name);
                    if (File.Exists(candidate))
                    {
                        discoveredPath = Path.GetFullPath(candidate);
                        break;
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                }
            }

            if (discoveredPath is not null)
            {
                break;
            }
        }

        _cli.ExecutablePath = discoveredPath ?? string.Empty;
        CliPathText.Text = _cli.ExecutablePath;
        CliPathSummaryText.Text = string.IsNullOrWhiteSpace(_cli.ExecutablePath)
            ? LocalizationService.Get("status.cli.binaryNotFound")
            : _cli.ExecutablePath;

        if (string.IsNullOrWhiteSpace(_cli.ExecutablePath))
        {
            SetCliUnavailable();
        }

        UpdateCliSetupState();
    }

    private static IEnumerable<string> GetPathDirectories(EnvironmentVariableTarget target)
    {
        var pathValue = Environment.GetEnvironmentVariable("Path", target);
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return [];
        }

        return pathValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => Environment.ExpandEnvironmentVariables(entry.Trim('"')))
            .Where(entry => !string.IsNullOrWhiteSpace(entry));
    }

    private async Task<bool> VerifyCliAsync()
    {
        var result = await ExecuteCliAsync(
            LocalizationService.Get("task.cli.verify"),
            ["--version"],
            showOutputPageOnFailure: false);
        if (result is null)
        {
            return false;
        }

        if (result.ExitCode == 0)
        {
            var version = FirstMeaningfulLine(result.StandardOutput) ?? LocalizationService.Get("status.connected");
            CliVersionText.Text = version.StartsWith('v') ? version : $"v{version}";
            SidebarStatusText.Text = LocalizationService.Get("status.cli.ready");
            SidebarStatusDot.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94));
            CliPathSummaryText.Text = _cli.ExecutablePath;
            return true;
        }

            SetCliUnavailable(LocalizationService.Get("status.cli.verifyFailed"));
        return false;
    }

    private void SetCliUnavailable(string? message = null)
    {
        message ??= LocalizationService.Get("status.cli.notFound");
        CliVersionText.Text = message;
        SidebarStatusText.Text = message;
        SidebarStatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
    }

    private async Task<CliResult?> ExecuteCliAsync(
        string taskName,
        IReadOnlyList<string> arguments,
        bool showOutputPageOnFailure = true,
        bool echoCommandOutput = true,
        bool trackTask = false)
    {
        if (_isBusy)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_cli.ExecutablePath) || !File.Exists(_cli.ExecutablePath))
        {
            SetCliUnavailable();
            UpdateCliSetupState();
            ShowPage("Settings");
            return null;
        }

        var task = trackTask ? StartTask(taskName, arguments) : null;
        SetBusy(true, taskName);
        AppendOutput($"> {Path.GetFileName(_cli.ExecutablePath)} {FormatArguments(arguments)}");

        try
        {
            var result = await _cli.RunAsync(arguments, line =>
            {
                if (echoCommandOutput)
                {
                    AppendOutput(line);
                }
                if (task is not null)
                {
                    UpdateTaskProgress(task, line);
                }
            });
            if (!echoCommandOutput && result.ExitCode != 0 && !string.IsNullOrWhiteSpace(result.StandardError))
            {
                foreach (var line in result.StandardError.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    AppendOutput($"[stderr] {line}");
                }
            }
            AppendOutput(result.ExitCode == 0
                ? LocalizationService.Format("task.done", taskName)
                : LocalizationService.Format("task.failedExit", taskName, result.ExitCode));
            CompleteTask(
                task,
                result.ExitCode == 0,
                result.ExitCode == 0
                    ? LocalizationService.Get("common.completed")
                    : LocalizationService.Get("common.failed"));

            if (result.ExitCode != 0 && showOutputPageOnFailure)
            {
                ShowPage("Output");
            }

            return result;
        }
        catch (Exception ex)
        {
            AppendOutput(LocalizationService.Format("task.error", ex.Message));
            CompleteTask(task, succeeded: false, LocalizationService.Get("common.failed"));
            if (showOutputPageOnFailure)
            {
                ShowPage("Output");
            }
            return new CliResult(-1, string.Empty, ex.Message);
        }
        finally
        {
            SetBusy(false, string.Empty);
        }
    }

    private CliTaskItem StartTask(string title, IReadOnlyList<string> arguments)
    {
        var task = new CliTaskItem
        {
            Title = title,
            Status = LocalizationService.Get("common.preparing"),
            Detail = FormatArguments(arguments),
            IsIndeterminate = true,
            StartedAt = DateTime.Now
        };

        Tasks.Insert(0, task);
        while (Tasks.Count > 20)
        {
            Tasks.RemoveAt(Tasks.Count - 1);
        }
        _currentTask = task;
        return task;
    }

    private void UpdateTaskProgress(CliTaskItem task, string output)
    {
        var detail = AnsiEscapePattern.Replace(output, string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(detail))
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            task.Detail = detail;
            task.Status = DetectTaskStage(detail, task.Status);

            var match = PercentPattern.Match(detail);
            if (match.Success && double.TryParse(match.Groups["value"].Value, out var progress))
            {
                task.Progress = Math.Clamp(progress, 0, 100);
                task.IsIndeterminate = false;
            }
        });
    }

    private static string DetectTaskStage(string detail, string currentStatus)
    {
        if (detail.Contains("打包", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("packag", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("building", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.Get("common.packaging");
        }
        if (detail.Contains("下载", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("download", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.Get("common.downloading");
        }
        if (detail.Contains("解压", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("extract", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.Get("common.extracting");
        }
        if (detail.Contains("校验", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("验证", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("verif", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.Get("common.verifying");
        }
        if (detail.Contains("安装", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("install", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.Get("common.installing");
        }

        return currentStatus == LocalizationService.Get("common.preparing")
            ? LocalizationService.Get("common.processing")
            : currentStatus;
    }

    private void CompleteTask(CliTaskItem? task, bool succeeded, string status)
    {
        if (task is null)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            task.IsIndeterminate = false;
            task.IsRunning = false;
            task.Status = task.Status == LocalizationService.Get("common.stopping")
                ? LocalizationService.Get("common.stopped")
                : status;
            if (succeeded)
            {
                task.Progress = 100;
            }
            task.CompletedAt = DateTime.Now;
        });

        if (ReferenceEquals(_currentTask, task))
        {
            _currentTask = null;
        }
    }

    private void SetBusy(bool isBusy, string taskName)
    {
        _isBusy = isBusy;
        BusyProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = isBusy;
        WorkspaceBusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        SidebarStatusText.Text = isBusy ? taskName : LocalizationService.Get("status.cli.ready");
    }

    private void AppendOutput(string line)
    {
        Dispatcher.Invoke(() =>
        {
            var timestamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
            OutputText.AppendText(timestamped + Environment.NewLine);
            OutputText.ScrollToEnd();

            _recentOutput.Enqueue(timestamped);
            while (_recentOutput.Count > 7)
            {
                _recentOutput.Dequeue();
            }
            RecentOutputText.Text = string.Join(Environment.NewLine, _recentOutput);
        });
    }

    private async Task RefreshEditorsAsync(bool showOutputPageOnFailure = true)
    {
        var result = await ExecuteCliAsync(
            LocalizationService.Get("task.editor.loadInstalled"),
            ["--no-banner", "editors", "-i", "--json"],
            showOutputPageOnFailure,
            echoCommandOutput: false);

        if (result?.ExitCode != 0)
        {
            EditorListStatusText.Text = LocalizationService.Get("editor.loadFailed");
            return;
        }

        var selectedVersion = ModuleEditorVersionCombo.SelectedValue as string;
        var cachedEditors = InstalledEditors.ToArray();
        var cliEditors = ParseEditors(result.StandardOutput).ToArray();
        var mergedEditors = new List<EditorInstallation>();

        foreach (var editor in cliEditors)
        {
            var cached = cachedEditors.FirstOrDefault(item =>
                string.Equals(item.Version, editor.Version, StringComparison.OrdinalIgnoreCase));
            mergedEditors.Add(MergeEditorLocation(editor, cached));
        }

        foreach (var cached in cachedEditors.Where(item => ResolveEditorExecutable(item.Path) is not null))
        {
            if (mergedEditors.Any(item => string.Equals(item.Version, cached.Version, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            mergedEditors.Add(CloneEditor(cached, isCachedOnly: true));
        }

        InstalledEditors.Clear();
        foreach (var editor in mergedEditors.OrderByDescending(item => item.Version, StringComparer.OrdinalIgnoreCase))
        {
            InstalledEditors.Add(editor);
        }

        SaveEditorInstallationsCache();

        if (!string.IsNullOrWhiteSpace(selectedVersion) && InstalledEditors.Any(editor => editor.Version == selectedVersion))
        {
            ModuleEditorVersionCombo.SelectedValue = selectedVersion;
        }
        else if (InstalledEditors.Count > 0)
        {
            ModuleEditorVersionCombo.SelectedIndex = 0;
        }

        EditorCountText.Text = InstalledEditors.Count.ToString();
        EditorListStatusText.Text = InstalledEditors.Count == 0
            ? LocalizationService.Get("editor.noneManaged")
            : LocalizationService.Format("editor.count", InstalledEditors.Count);
        UpdateEditorFilters();
    }

    private static EditorInstallation MergeEditorLocation(EditorInstallation editor, EditorInstallation? cached)
    {
        var path = ResolveEditorExecutable(editor.Path) is not null
            ? editor.Path
            : cached?.Path ?? editor.Path;

        return new EditorInstallation
        {
            Version = editor.Version,
            Architecture = string.IsNullOrWhiteSpace(editor.Architecture) ? cached?.Architecture ?? string.Empty : editor.Architecture,
            Path = path,
            Modules = string.IsNullOrWhiteSpace(editor.Modules) ? cached?.Modules ?? string.Empty : editor.Modules,
            Channel = string.IsNullOrWhiteSpace(editor.Channel) ? cached?.Channel ?? string.Empty : editor.Channel,
            IsLts = editor.IsLts || cached?.IsLts == true,
            IsDefault = editor.IsDefault,
            IsCachedOnly = false
        };
    }

    private static EditorInstallation CloneEditor(EditorInstallation editor, bool isCachedOnly) => new()
    {
        Version = editor.Version,
        Architecture = editor.Architecture,
        Path = editor.Path,
        Modules = editor.Modules,
        Channel = editor.Channel,
        IsLts = editor.IsLts,
        IsDefault = editor.IsDefault,
        IsCachedOnly = isCachedOnly
    };

    private static IEnumerable<EditorInstallation> ParseEditors(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var array = FindFirstArray(document.RootElement);
            if (array is null)
            {
                return [];
            }

            return array.Value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new EditorInstallation
                {
                    Version = ReadString(item, "version", "editorVersion", "displayName", "name"),
                    Architecture = ReadString(item, "architecture", "arch"),
                    Path = ReadString(item, "path", "location", "installPath", "installationPath"),
                    Modules = ReadArray(item, "modules", "installedModules"),
                    Channel = ReadString(item, "channel", "releaseType", "stream", "versionType"),
                    IsLts = ReadBoolean(item, "lts", "isLts"),
                    IsDefault = ReadBoolean(item, "default", "isDefault")
                })
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IEnumerable<EditorRelease> ParseReleases(string json)
    {
        var records = ParseJsonRecords(json);
        return records.Select(item => new EditorRelease
        {
            Version = ReadString(item, "version", "editorVersion", "name"),
            Alias = ReadString(item, "alias", "shortVersion", "shortRevision", "aliases"),
            Architecture = ReadString(item, "architecture", "arch"),
            Installed = ReadBoolean(item, "installed", "isInstalled"),
            IsDefault = ReadBoolean(item, "default", "isDefault"),
            Platforms = ReadStringOrArray(item, "platform", "platforms", "os")
        }).Where(item => !string.IsNullOrWhiteSpace(item.Version));
    }

    private static IEnumerable<UnityModuleInfo> ParseModules(string json)
    {
        var records = ParseJsonRecords(json).SelectMany(ExpandModuleRecords);
        return records.Select(item =>
        {
            var id = ReadString(item, "id", "moduleId", "module", "slug");
            var name = ReadString(item, "name", "displayName", "title");
            return new UnityModuleInfo
            {
                Id = string.IsNullOrWhiteSpace(id) ? name : id,
                Name = string.IsNullOrWhiteSpace(name) ? id : name,
                Size = ReadString(item, "size", "downloadSize", "installedSize"),
                Installed = ReadBoolean(item, "installed", "isInstalled")
            };
        }).Where(item => !string.IsNullOrWhiteSpace(item.Id));
    }

    private static IEnumerable<JsonElement> ExpandModuleRecords(JsonElement item)
    {
        yield return item;

        foreach (var childProperty in new[] { "children", "childModules", "submodules" })
        {
            if (!TryGetProperty(item, childProperty, out var children) || children.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var child in children.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object))
            {
                foreach (var descendant in ExpandModuleRecords(child.Clone()))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static IEnumerable<JsonElement> ParseJsonRecords(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var array = FindFirstArray(document.RootElement);
            return array?.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => item.Clone())
                .ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static JsonElement? FindFirstArray(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var preferredName in new[] { "editors", "releases", "modules", "items", "results", "data" })
        {
            if (!TryGetProperty(element, preferredName, out var preferredValue))
            {
                continue;
            }

            var preferredArray = FindFirstArray(preferredValue);
            if (preferredArray is not null)
            {
                return preferredArray;
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            var found = FindFirstArray(property.Value);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString() ?? string.Empty,
                    JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(item => item.ToString())),
                    JsonValueKind.Null => string.Empty,
                    _ => value.ToString()
                };
            }
        }

        return string.Empty;
    }

    private static string ReadArray(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return string.Join(", ", value.EnumerateArray().Select(item =>
                item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : ReadString(item, "id", "name", "moduleId")));
        }

        return string.Empty;
    }

    private static string ReadStringOrArray(JsonElement element, params string[] names) => ReadString(element, names);

    private static bool ReadBoolean(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                continue;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            if (bool.TryParse(value.ToString(), out var parsed))
            {
                return parsed;
            }
        }

        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static IReadOnlyList<string> SplitValues(string value) => value
        .Split([' ', ',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? FirstMeaningfulLine(string value) => value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault();

    private static string FormatArguments(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(argument =>
        argument.Any(char.IsWhiteSpace) ? $"\"{argument.Replace("\"", "\\\"")}\"" : argument));

    private static bool Confirm(string message) => ShowLocalizedMessage(
        message,
        LocalizationService.Get("dialog.confirmAction"),
        MessageBoxButton.OKCancel,
        MessageBoxImage.Question) == MessageBoxResult.OK;

    private static MessageBoxResult ShowLocalizedMessage(
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image) => MessageBox.Show(
            message,
            caption,
            buttons,
            image);

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string page)
        {
            ShowPage(page);
        }
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingLanguage || LanguageCombo?.SelectedValue is not string language)
        {
            return;
        }

        LocalizationService.SetLanguage(language);
        SaveManagerSettings();
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
    {
        Window_StateChanged(this, EventArgs.Empty);
        ShowPage(_currentPage);
        InstalledEditorsGrid.Items.Refresh();
        AvailableEditorsGrid.Items.Refresh();
        foreach (var module in AvailableModules.Concat(InstalledModules).Concat(InstallableModules).Distinct())
        {
            module.NotifyLocalizationChanged();
        }
        foreach (var project in RecentProjects)
        {
            project.NotifyLocalizationChanged();
        }
        foreach (var task in Tasks)
        {
            task.NotifyLocalizationChanged();
        }
        UpdateProjectListStatus();
        UpdateCliSetupState();
    }

    private void ShowPage(string page)
    {
        if (page == "Modules")
        {
            page = "Editors";
        }

        _currentPage = page;

        if (page == "Editors")
        {
            EditorListsTabs.SelectedIndex = 0;
            EditorPageHeadingText.Text = LocalizationService.Get("editor.heading.installed");
            InstalledEditorsActionsPanel.Visibility = Visibility.Visible;
        }

        DashboardPage.Visibility = page == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        EditorsPage.Visibility = page == "Editors" ? Visibility.Visible : Visibility.Collapsed;
        ProjectsPage.Visibility = page == "Projects" ? Visibility.Visible : Visibility.Collapsed;
        OutputPage.Visibility = page == "Output" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        var navigation = new Dictionary<Button, string>
        {
            [DashboardNav] = "Dashboard",
            [EditorsNav] = "Editors",
            [ProjectsNav] = "Projects",
            [OutputNav] = "Output",
            [SettingsNav] = "Settings"
        };

        foreach (var item in navigation)
        {
            item.Key.Tag = item.Value == page ? "Selected" : null;
        }

        var titles = new Dictionary<string, string>
        {
            ["Dashboard"] = LocalizationService.Get("nav.home"),
            ["Editors"] = LocalizationService.Get("nav.editors"),
            ["Projects"] = LocalizationService.Get("nav.projects"),
            ["Output"] = LocalizationService.Get("nav.tasks"),
            ["Settings"] = LocalizationService.Get("nav.settings")
        };

        PageTitleText.Text = titles[page];
    }

    private async void RefreshEditors_Click(object sender, RoutedEventArgs e) => await RefreshEditorsAsync();

    private void EditorFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string filter })
        {
            _activeEditorFilter = filter;
            ApplyEditorFilter();
        }
    }

    private void UpdateEditorFilters()
    {
        AllEditorCountText.Text = InstalledEditors.Count.ToString();
        LtsEditorCountText.Text = InstalledEditors.Count(editor => editor.IsLtsRelease).ToString();
        PreviewEditorCountText.Text = InstalledEditors.Count(editor => editor.IsPreview).ToString();
        ReleaseEditorCountText.Text = InstalledEditors.Count(editor => !editor.IsPreview).ToString();
        ApplyEditorFilter();
    }

    private void ApplyEditorFilter()
    {
        var selectedVersion = (InstalledEditorsGrid.SelectedItem as EditorInstallation)?.Version;
        var filtered = _activeEditorFilter switch
        {
            "Lts" => InstalledEditors.Where(editor => editor.IsLtsRelease),
            "Release" => InstalledEditors.Where(editor => !editor.IsPreview),
            "Preview" => InstalledEditors.Where(editor => editor.IsPreview),
            _ => InstalledEditors
        };

        FilteredInstalledEditors.Clear();
        foreach (var editor in filtered)
        {
            FilteredInstalledEditors.Add(editor);
        }

        var filterButtons = new Dictionary<string, Button>
        {
            ["All"] = AllEditorsFilterButton,
            ["Lts"] = LtsEditorsFilterButton,
            ["Release"] = ReleaseEditorsFilterButton,
            ["Preview"] = PreviewEditorsFilterButton
        };
        foreach (var pair in filterButtons)
        {
            var selected = pair.Key == _activeEditorFilter;
            pair.Value.Background = selected
                ? new SolidColorBrush(Color.FromRgb(229, 241, 251))
                : Brushes.Transparent;
            pair.Value.BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(140, 200, 242))
                : Brushes.Transparent;
            pair.Value.BorderThickness = selected ? new Thickness(1) : new Thickness(0);
        }

        InstalledEditorsGrid.SelectedItem = FilteredInstalledEditors.FirstOrDefault(editor => editor.Version == selectedVersion)
            ?? FilteredInstalledEditors.FirstOrDefault();
    }

    private void InstalledEditorsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InstalledEditorsGrid.SelectedItem is not EditorInstallation editor)
        {
            return;
        }

        ModuleEditorVersionCombo.SelectedValue = editor.Version;
        ShowCachedEditorModules(editor);
    }

    private void ShowCachedEditorModules(EditorInstallation editor)
    {
        AvailableModules.Clear();
        InstalledModules.Clear();
        InstallableModules.Clear();
        _loadedModulesVersion = string.Empty;

        foreach (var moduleName in SplitRegisteredModules(editor.Modules))
        {
            var module = new UnityModuleInfo
            {
                Id = moduleName,
                Name = moduleName,
                Installed = true
            };
            AvailableModules.Add(module);
            InstalledModules.Add(module);
        }

        InstalledModulesEmptyText.Visibility = InstalledModules.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ModuleListStatusText.Text = InstalledModules.Count == 0
            ? LocalizationService.Format("editor.cache.noModules", editor.Version)
            : LocalizationService.Format("editor.cache.modules", editor.Version, InstalledModules.Count);
    }

    private async void ListReleases_Click(object sender, RoutedEventArgs e)
    {
        ShowPage("Editors");
        EditorListsTabs.SelectedIndex = 1;
        EditorPageHeadingText.Text = LocalizationService.Get("editor.heading.install");
        InstalledEditorsActionsPanel.Visibility = Visibility.Collapsed;
        await RefreshReleasesAsync();
    }

    private void BackToInstalledEditors_Click(object sender, RoutedEventArgs e)
    {
        EditorListsTabs.SelectedIndex = 0;
        EditorPageHeadingText.Text = LocalizationService.Get("editor.heading.installed");
        InstalledEditorsActionsPanel.Visibility = Visibility.Visible;
    }

    private void ReleaseSearchText_Changed(object sender, TextChangedEventArgs e)
    {
        var query = ReleaseSearchText.Text.Trim();
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(AvailableEditors);
        view.Filter = item => item is EditorRelease release &&
            (string.IsNullOrWhiteSpace(query) ||
             release.Version.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             release.Alias.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             release.Architecture.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             release.Platforms.Contains(query, StringComparison.OrdinalIgnoreCase));
        view.Refresh();
    }

    private void AvailableEditorsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AvailableEditorsGrid.SelectedItem is EditorRelease release)
        {
            InstallVersionText.Text = release.Version;
            InstallPatchText.Clear();
        }
    }

    private async void RefreshCurrentEditorList_Click(object sender, RoutedEventArgs e)
    {
        if (EditorListsTabs.SelectedIndex == 1)
        {
            await RefreshReleasesAsync();
        }
        else
        {
            await RefreshEditorsAsync();
        }
    }

    private async Task RefreshReleasesAsync()
    {
        ReleaseListStatusText.Text = LocalizationService.Get("release.loading");
        var result = await ExecuteCliAsync(
            LocalizationService.Get("task.release.load"),
            ["--no-banner", "editors", "-r", "--json"],
            echoCommandOutput: false);

        if (result?.ExitCode != 0)
        {
            ReleaseListStatusText.Text = LocalizationService.Get("editor.loadFailed");
            return;
        }

        AvailableEditors.Clear();
        foreach (var release in ParseReleases(result.StandardOutput))
        {
            AvailableEditors.Add(release);
        }

        ReleaseListStatusText.Text = AvailableEditors.Count == 0
            ? LocalizationService.Get("release.none")
            : LocalizationService.Format("release.count", AvailableEditors.Count);
        if (AvailableEditorsGrid.SelectedItem is null && AvailableEditors.Count > 0)
        {
            AvailableEditorsGrid.SelectedIndex = 0;
        }
    }

    private async void InstallSelectedRelease_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableEditorsGrid.SelectedItem is not EditorRelease release)
        {
            ShowLocalizedMessage(
                LocalizationService.Get("message.selectVersion"),
                LocalizationService.Get("dialog.noVersionSelected.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (release.Installed)
        {
            ShowLocalizedMessage(
                LocalizationService.Get("message.versionInstalled"),
                LocalizationService.Get("dialog.versionInstalled.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (await InstallEditorVersionAsync(release.Version))
        {
            await RefreshReleasesAsync();
        }
    }

    private async void ManageSelectedEditorModules_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledEditorsGrid.SelectedItem is not EditorInstallation editor)
        {
            ShowLocalizedMessage(
                LocalizationService.Get("message.selectInstalledVersion"),
                LocalizationService.Get("dialog.noVersionSelected.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ShowPage("Modules");
        EditorListsTabs.SelectedIndex = 0;
        EditorDetailsTabs.SelectedIndex = 0;
        ModuleEditorVersionCombo.SelectedValue = editor.Version;
        await RefreshModulesAsync();
    }

    private async void LoadSelectedEditorModules_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledEditorsGrid.SelectedItem is not EditorInstallation editor)
        {
            ShowLocalizedMessage(
                LocalizationService.Get("message.selectInstalledEditor"),
                LocalizationService.Get("dialog.noEditorSelected.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ModuleEditorVersionCombo.SelectedValue = editor.Version;
        EditorDetailsTabs.SelectedIndex = 0;
        await RefreshModulesAsync();
    }

    private void LaunchSelectedEditor_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledEditorsGrid.SelectedItem is not EditorInstallation editor)
        {
            ShowLocalizedMessage(
                LocalizationService.Get("message.selectInstalledEditor"),
                LocalizationService.Get("dialog.noEditorSelected.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var executable = ResolveEditorExecutable(editor.Path);
        if (executable is null)
        {
            ShowLocalizedMessage(
                LocalizationService.Get("message.unityExecutableMissing"),
                LocalizationService.Get("dialog.launchEditorFailed.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }

    private void OpenSelectedEditorFolder_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledEditorsGrid.SelectedItem is not EditorInstallation editor)
        {
            ShowLocalizedMessage(
                LocalizationService.Get("message.selectInstalledEditor"),
                LocalizationService.Get("dialog.noEditorSelected.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var folder = File.Exists(editor.Path) ? Path.GetDirectoryName(editor.Path) : editor.Path;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            ShowLocalizedMessage(
                LocalizationService.Get("message.editorFolderMissing"),
                LocalizationService.Get("dialog.folderUnavailable.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add(folder);
        Process.Start(startInfo);
    }

    private void EditorMoreActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu })
        {
            return;
        }

        menu.PlacementTarget = (Button)sender;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void CopyEditorPath_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledEditorsGrid.SelectedItem is EditorInstallation editor &&
            !string.IsNullOrWhiteSpace(editor.Path))
        {
            Clipboard.SetText(editor.Path);
        }
    }

    private async void SetDefaultEditor_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledEditorsGrid.SelectedItem is not EditorInstallation editor)
        {
            return;
        }

        var result = await ExecuteCliAsync(
            LocalizationService.Format("task.editor.setDefault", editor.Version),
            ["--non-interactive", "editors", "default", editor.Version]);
        if (result?.ExitCode == 0)
        {
            await RefreshEditorsAsync();
            SelectEditorVersion(editor.Version);
        }
    }

    private async void CheckEditorUpgrade_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledEditorsGrid.SelectedItem is not EditorInstallation editor)
        {
            return;
        }

        var result = await ExecuteCliAsync(
            LocalizationService.Format("task.editor.checkUpdate", editor.Version),
            ["--non-interactive", "editors", "upgrade", editor.Version, "--check"]);
        if (result?.ExitCode == 0)
        {
            var message = string.IsNullOrWhiteSpace(result.StandardOutput)
                ? LocalizationService.Get("editor.noPatchUpdate")
                : result.StandardOutput.Trim();
            ShowLocalizedMessage(
                message,
                LocalizationService.Get("dialog.editorUpdate.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async void UpgradeSelectedEditor_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledEditorsGrid.SelectedItem is not EditorInstallation editor ||
            !Confirm(LocalizationService.Format("confirm.editor.upgrade", editor.Version)))
        {
            return;
        }

        var result = await ExecuteCliAsync(
            LocalizationService.Format("task.editor.upgrade", editor.Version),
            ["--non-interactive", "editors", "upgrade", editor.Version, "-y"],
            trackTask: true);
        if (result?.ExitCode == 0)
        {
            await RefreshEditorsAsync();
        }
    }

    private async void UninstallSelectedEditor_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledEditorsGrid.SelectedItem is not EditorInstallation editor ||
            !Confirm(LocalizationService.Format("confirm.editor.uninstall", editor.Version)))
        {
            return;
        }

        var result = await ExecuteCliAsync(
            LocalizationService.Format("task.editor.uninstall", editor.Version),
            ["--non-interactive", "uninstall", editor.Version, "-y"],
            trackTask: true);
        if (result?.ExitCode == 0)
        {
            await RefreshEditorsAsync();
        }
    }

    private void SelectEditorVersion(string version)
    {
        _activeEditorFilter = "All";
        ApplyEditorFilter();
        var editor = FilteredInstalledEditors.FirstOrDefault(item =>
            string.Equals(item.Version, version, StringComparison.OrdinalIgnoreCase));
        if (editor is not null)
        {
            InstalledEditorsGrid.SelectedItem = editor;
            InstalledEditorsGrid.ScrollIntoView(editor);
        }
    }

    private static string? ResolveEditorExecutable(string path)
    {
        if (File.Exists(path) && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        return new[]
        {
            Path.Combine(path, "Editor", "Unity.exe"),
            Path.Combine(path, "Unity.exe")
        }.FirstOrDefault(File.Exists);
    }

    private async void InstallLts_Click(object sender, RoutedEventArgs e)
    {
        InstallVersionText.Text = "lts";
        InstallPatchText.Clear();
        await InstallEditorVersionAsync("lts");
    }

    private async void InstallEditor_Click(object sender, RoutedEventArgs e)
    {
        var version = ComposeInstallVersion();
        if (string.IsNullOrWhiteSpace(version) || version.Any(char.IsWhiteSpace))
        {
            ShowLocalizedMessage(
                LocalizationService.Get("message.invalidVersion"),
                LocalizationService.Get("dialog.invalidVersion.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await InstallEditorVersionAsync(version);
    }

    private async Task<bool> InstallEditorVersionAsync(string version)
    {
        var modules = SplitValues(InstallModulesText.Text);
        var dryRun = InstallDryRunCheck.IsChecked == true;
        var moduleSummary = modules.Count > 0
            ? LocalizationService.Format("confirm.editor.modules", string.Join(", ", modules))
            : string.Empty;
        var confirmation = LocalizationService.Format(
            dryRun ? "confirm.editor.preview" : "confirm.editor.install",
            version,
            moduleSummary);
        if (!Confirm(confirmation))
        {
            return false;
        }

        var arguments = new List<string> { "--non-interactive", "install", version, "-y" };
        if (modules.Count > 0)
        {
            arguments.Add("-m");
            arguments.AddRange(modules);
        }
        if (dryRun)
        {
            arguments.Add("--dry-run");
        }
        if (InstallAcceptEulaCheck.IsChecked == true)
        {
            arguments.Add("--accept-eula");
        }

        var result = await ExecuteCliAsync(
            LocalizationService.Format(
                dryRun ? "task.editor.preview" : "task.editor.install",
                version),
            arguments,
            trackTask: !dryRun);
        if (result?.ExitCode == 0 && !dryRun)
        {
            await RefreshEditorsAsync();
        }
        return result?.ExitCode == 0;
    }

    private string ComposeInstallVersion()
    {
        var versionLine = InstallVersionText.Text.Trim();
        var patch = InstallPatchText.Text.Trim();
        if (string.IsNullOrWhiteSpace(patch))
        {
            return versionLine;
        }

        if (string.Equals(versionLine, "lts", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (versionLine.EndsWith('.') || patch.StartsWith('.'))
        {
            return versionLine + patch;
        }

        return $"{versionLine}.{patch}";
    }

    private async void ScanEditors_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Get("dialog.selectEditorScanFolder"),
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        IReadOnlyList<string> editorExecutables;
        var scanCancellation = new CancellationTokenSource();
        _scanCancellation = scanCancellation;
        SetEditorScanStatus(LocalizationService.Get("editor.scan.scanning"));
        SetBusy(true, LocalizationService.Get("editor.scan.scanning"));
        try
        {
            editorExecutables = await Task.Run(
                () => FindEditorExecutables(dialog.FolderName, scanCancellation.Token),
                scanCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            AppendOutput(LocalizationService.Get("editor.scan.canceled"));
            SetEditorScanStatus(LocalizationService.Get("editor.scan.canceled"));
            return;
        }
        finally
        {
            if (ReferenceEquals(_scanCancellation, scanCancellation))
            {
                _scanCancellation = null;
            }
            scanCancellation.Dispose();
            SetBusy(false, string.Empty);
        }

        if (editorExecutables.Count == 0)
        {
            SetEditorScanStatus(LocalizationService.Get("editor.scan.noUnity"), isError: true);
            return;
        }

        var alreadyRegistered = editorExecutables.Where(IsEditorRegistered).ToArray();
        var newEditors = editorExecutables.Except(alreadyRegistered, StringComparer.OrdinalIgnoreCase).ToArray();
        CacheScannedEditorInstallations(editorExecutables);
        EditorListsTabs.SelectedIndex = 0;
        SelectScannedEditor(editorExecutables);
        if (newEditors.Length == 0)
        {
            SetEditorScanStatus(LocalizationService.Format("editor.scan.cached", editorExecutables.Count));
            return;
        }

        var preview = string.Join(Environment.NewLine, newEditors.Take(6));
        if (newEditors.Length > 6)
        {
            preview += Environment.NewLine + LocalizationService.Format(
                "confirm.scan.more",
                newEditors.Length - 6);
        }
        var skippedText = alreadyRegistered.Length > 0
            ? Environment.NewLine + LocalizationService.Format(
                "confirm.scan.skipped",
                alreadyRegistered.Length)
            : string.Empty;
        if (!Confirm(LocalizationService.Format(
                "confirm.scan.addEditors",
                newEditors.Length,
                preview,
                skippedText)))
        {
            SetEditorScanStatus(LocalizationService.Format(
                "editor.scan.cachedUnregistered",
                editorExecutables.Count,
                newEditors.Length));
            return;
        }

        var arguments = new List<string> { "--non-interactive", "editors", "add" };
        arguments.AddRange(newEditors);
        var result = await ExecuteCliAsync(
            LocalizationService.Get("task.editor.addExisting"),
            arguments,
            trackTask: true);
        if (result?.ExitCode == 0)
        {
            await RefreshEditorsAsync();
            EditorListsTabs.SelectedIndex = 0;
            SelectScannedEditor(newEditors);
            var skippedSummary = alreadyRegistered.Length > 0
                ? LocalizationService.Format("editor.scan.skipped", alreadyRegistered.Length)
                : string.Empty;
            SetEditorScanStatus(LocalizationService.Format(
                "editor.scan.cachedRegistered",
                editorExecutables.Count,
                newEditors.Length,
                skippedSummary));
        }
        else
        {
            SetEditorScanStatus(
                LocalizationService.Format("editor.scan.registerFailed", editorExecutables.Count),
                isError: true);
        }
    }

    private void SetEditorScanStatus(string message, bool isError = false)
    {
        EditorScanStatusText.Text = message;
        EditorScanStatusText.Foreground = new SolidColorBrush(isError
            ? Color.FromRgb(196, 43, 28)
            : Color.FromRgb(105, 116, 137));
        EditorScanStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void CacheScannedEditorInstallations(IEnumerable<string> editorPaths)
    {
        foreach (var executablePath in editorPaths)
        {
            var version = TryGetVersionFromEditorPath(executablePath);
            if (string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            var existing = InstalledEditors.FirstOrDefault(editor =>
                string.Equals(editor.Version, version, StringComparison.OrdinalIgnoreCase));
            var cachedEditor = new EditorInstallation
            {
                Version = version,
                Architecture = string.IsNullOrWhiteSpace(existing?.Architecture) ? "x86_64" : existing.Architecture,
                Path = executablePath,
                Modules = existing?.Modules ?? string.Empty,
                Channel = existing?.Channel ?? string.Empty,
                IsLts = existing?.IsLts == true,
                IsDefault = existing?.IsDefault == true,
                IsCachedOnly = existing?.IsCachedOnly ?? true
            };

            if (existing is null)
            {
                InstalledEditors.Add(cachedEditor);
            }
            else
            {
                InstalledEditors[InstalledEditors.IndexOf(existing)] = cachedEditor;
            }
        }

        SaveEditorInstallationsCache();
        EditorCountText.Text = InstalledEditors.Count.ToString();
        EditorListStatusText.Text = LocalizationService.Format("editor.cachedCount", InstalledEditors.Count);
        UpdateEditorFilters();
    }

    private void SelectScannedEditor(IEnumerable<string> editorPaths)
    {
        var versions = editorPaths
            .Select(TryGetVersionFromEditorPath)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var editor = InstalledEditors.FirstOrDefault(item => versions.Contains(item.Version));
        if (editor is null)
        {
            return;
        }

        _activeEditorFilter = "All";
        ApplyEditorFilter();
        InstalledEditorsGrid.SelectedItem = editor;
        InstalledEditorsGrid.ScrollIntoView(editor);
    }

    private bool IsEditorRegistered(string executablePath)
    {
        var normalizedCandidate = NormalizePath(executablePath);
        var candidateVersion = TryGetVersionFromEditorPath(executablePath);

        return InstalledEditors.Where(editor => !editor.IsCachedOnly).Any(editor =>
        {
            var registeredExecutable = ResolveEditorExecutable(editor.Path);
            var samePath = registeredExecutable is not null &&
                           string.Equals(NormalizePath(registeredExecutable), normalizedCandidate, StringComparison.OrdinalIgnoreCase);
            var sameVersion = !string.IsNullOrWhiteSpace(candidateVersion) &&
                              string.Equals(editor.Version, candidateVersion, StringComparison.OrdinalIgnoreCase);
            return samePath || sameVersion;
        });
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? TryGetVersionFromEditorPath(string executablePath)
    {
        var editorDirectory = Directory.GetParent(executablePath);
        if (editorDirectory is null || !string.Equals(editorDirectory.Name, "Editor", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return editorDirectory.Parent?.Name;
    }

    private static IReadOnlyList<string> FindEditorExecutables(string rootPath, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        var results = new List<string>();
        foreach (var path in Directory.EnumerateFiles(rootPath, "Unity.exe", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(path);
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task RefreshModulesAsync()
    {
        var version = ModuleEditorVersionCombo.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(version))
        {
            ModuleListStatusText.Text = LocalizationService.Get("module.noEditors");
            ShowLocalizedMessage(
                LocalizationService.Get("message.refreshEditorsFirst"),
                LocalizationService.Get("dialog.noInstalledEditor.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        AvailableModules.Clear();
        InstalledModules.Clear();
        InstallableModules.Clear();
        _loadedModulesVersion = string.Empty;
        ModuleListStatusText.Text = LocalizationService.Format("module.loading", version);
        var result = await ExecuteCliAsync(
            LocalizationService.Get("task.module.load"),
            ["--no-banner", "--format", "json", "install-modules", "-e", version, "-l"],
            echoCommandOutput: false);

        if (result?.ExitCode != 0)
        {
            ModuleListStatusText.Text = LocalizationService.Get("module.loadFailed");
            return;
        }

        var selectedEditor = InstalledEditors.FirstOrDefault(editor =>
            string.Equals(editor.Version, version, StringComparison.OrdinalIgnoreCase));
        var registeredModules = SplitRegisteredModules(selectedEditor?.Modules);

        AvailableModules.Clear();
        foreach (var parsedModule in ParseModules(result.StandardOutput))
        {
            var module = CloneModule(parsedModule, parsedModule.Installed ||
                registeredModules.Any(registered => ModuleNamesMatch(registered, parsedModule.Name, parsedModule.Id)));
            AvailableModules.Add(module);
            if (module.Installed)
            {
                InstalledModules.Add(module);
            }
            else
            {
                InstallableModules.Add(module);
            }
        }
        _loadedModulesVersion = version;

        var installedCount = AvailableModules.Count(module => module.Installed);
        InstalledModulesEmptyText.Visibility = installedCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModuleListStatusText.Text = AvailableModules.Count == 0
            ? LocalizationService.Get("module.noneReturned")
            : installedCount == 0
                ? LocalizationService.Format("module.noneInstalled", version)
                : LocalizationService.Format("module.installedCount", version, installedCount);
    }

    private static IReadOnlyList<string> SplitRegisteredModules(string? modules) =>
        string.IsNullOrWhiteSpace(modules)
            ? []
            : modules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static UnityModuleInfo CloneModule(UnityModuleInfo module, bool installed) => new()
    {
        Id = module.Id,
        Name = module.Name,
        Size = module.Size,
        Installed = installed
    };

    private static bool ModuleNamesMatch(string registeredName, string moduleName, string moduleId)
    {
        var registered = NormalizeModuleName(registeredName);
        var name = NormalizeModuleName(moduleName);
        var id = NormalizeModuleName(moduleId);
        if (registered.Length < 3)
        {
            return false;
        }

        if (registered is "android" or "ios" or "linux" or "tvos" or "webgl" or "windows")
        {
            return registered == name || registered == id;
        }

        if (registered.Length <= 8)
        {
            return name.StartsWith(registered, StringComparison.Ordinal) ||
                   id.StartsWith(registered, StringComparison.Ordinal);
        }

        return registered == name || registered == id ||
               (name.Length >= 3 && (name.Contains(registered, StringComparison.Ordinal) || registered.Contains(name, StringComparison.Ordinal))) ||
               (id.Length >= 3 && (id.Contains(registered, StringComparison.Ordinal) || registered.Contains(id, StringComparison.Ordinal)));
    }

    private static string NormalizeModuleName(string value)
    {
        var normalized = value.ToLowerInvariant()
            .Replace("build support", string.Empty, StringComparison.Ordinal)
            .Replace("support", string.Empty, StringComparison.Ordinal)
            .Replace("(il2cpp)", string.Empty, StringComparison.Ordinal)
            .Replace("module", string.Empty, StringComparison.Ordinal);
        return Regex.Replace(normalized, @"[^\p{L}\p{N}]+", string.Empty);
    }

    private async void ListModules_Click(object sender, RoutedEventArgs e) => await RefreshModulesAsync();

    private void ModuleEditorVersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var version = ModuleEditorVersionCombo.SelectedValue as string;
        if (string.Equals(version, _loadedModulesVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AvailableModules.Clear();
        InstalledModules.Clear();
        InstallableModules.Clear();
        _loadedModulesVersion = string.Empty;
        InstalledModulesEmptyText.Visibility = Visibility.Collapsed;
        ModuleListStatusText.Text = string.IsNullOrWhiteSpace(version)
            ? LocalizationService.Get("module.selectEditor")
            : LocalizationService.Format("module.selectedEditor", version);
    }

    private async void OpenModuleInstaller_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledEditorsGrid.SelectedItem is not EditorInstallation editor)
        {
            ShowLocalizedMessage(
                LocalizationService.Get("message.selectInstalledEditor"),
                LocalizationService.Get("dialog.noEditorSelected.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ModuleEditorVersionCombo.SelectedValue = editor.Version;
        if (!string.Equals(_loadedModulesVersion, editor.Version, StringComparison.OrdinalIgnoreCase))
        {
            await RefreshModulesAsync();
        }

        if (InstallableModules.Count == 0)
        {
            ShowLocalizedMessage(
                LocalizationService.Get("message.noModules"),
                LocalizationService.Get("dialog.noModules.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new ModuleInstallerWindow(editor.Version, InstallableModules)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await InstallModulesAsync(editor.Version, dialog.SelectedModuleIds, dialog.DryRun, dialog.AcceptEula);
    }

    private async Task InstallModulesAsync(
        string version,
        IReadOnlyList<string> modules,
        bool dryRun,
        bool acceptEula)
    {
        var arguments = new List<string>
        {
            "--non-interactive", "install-modules", "-e", version, "-m"
        };
        arguments.AddRange(modules);
        arguments.Add("-y");
        if (dryRun)
        {
            arguments.Add("--dry-run");
        }
        if (acceptEula)
        {
            arguments.Add("--accept-eula");
        }

        var result = await ExecuteCliAsync(
            LocalizationService.Get(dryRun ? "task.module.preview" : "task.module.install"),
            arguments,
            trackTask: !dryRun);
        if (result?.ExitCode == 0 && !dryRun)
        {
            await RefreshEditorsAsync();
            SelectEditorVersion(version);
        }
    }

    private async void AuthLogin_Click(object sender, RoutedEventArgs e)
    {
        var result = await ExecuteCliAsync(
            LocalizationService.Get("task.account.login"),
            ["auth", "login"],
            showOutputPageOnFailure: false);
        var status = result?.ExitCode == 0
            ? LocalizationService.Get("account.signedIn")
            : LocalizationService.Get("account.signedOut");
        AuthStatusText.Text = status;
        SidebarAccountStatusText.Text = status;
    }

    private async void AuthStatus_Click(object sender, RoutedEventArgs e)
    {
        var result = await ExecuteCliAsync(
            LocalizationService.Get("task.account.status"),
            ["auth", "status"],
            showOutputPageOnFailure: false);
        var status = result?.ExitCode == 0
            ? LocalizationService.Get("account.signedIn")
            : LocalizationService.Get("account.signedOut");
        AuthStatusText.Text = status;
        SidebarAccountStatusText.Text = status;
    }

    private async void UpgradeCli_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm(LocalizationService.Get("confirm.cli.upgrade")))
        {
            return;
        }

        var result = await ExecuteCliAsync(
            LocalizationService.Get("task.cli.upgrade"),
            ["upgrade"],
            trackTask: true);
        if (result?.ExitCode == 0)
        {
            await VerifyCliAsync();
        }
    }

    private async void ScanProjects_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Get("dialog.selectProjectScanFolder"),
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var scanCancellation = new CancellationTokenSource();
        _scanCancellation = scanCancellation;
        ProjectScanStatusText.Text = LocalizationService.Format("project.scan.scanning", dialog.FolderName);
        ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(105, 116, 137));
        SetBusy(true, LocalizationService.Format("project.scan.scanning", dialog.FolderName));
        try
        {
            var projects = await Task.Run(
                () => FindUnityProjects(dialog.FolderName, scanCancellation.Token),
                scanCancellation.Token);
            var addedCount = 0;
            foreach (var projectPath in projects)
            {
                if (AddOrUpdateManagedProject(projectPath, markOpened: false))
                {
                    addedCount++;
                }
            }

            SortManagedProjects();
            SaveManagedProjects();
            UpdateProjectListStatus();
            ProjectScanStatusText.Text = projects.Count == 0
                ? LocalizationService.Get("project.scan.none")
                : LocalizationService.Format("project.scan.result", projects.Count, addedCount);
        }
        catch (OperationCanceledException)
        {
            ProjectScanStatusText.Text = LocalizationService.Get("project.scan.canceled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ProjectScanStatusText.Text = LocalizationService.Get("project.scan.failed");
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
        }
        finally
        {
            if (ReferenceEquals(_scanCancellation, scanCancellation))
            {
                _scanCancellation = null;
            }
            scanCancellation.Dispose();
            SetBusy(false, string.Empty);
        }
    }

    private void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Get("dialog.selectProjectFolder"),
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!IsUnityProject(dialog.FolderName))
        {
            ProjectScanStatusText.Text = LocalizationService.Get("project.invalidFolder");
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
            return;
        }

        var added = AddOrUpdateManagedProject(dialog.FolderName, markOpened: false);
        SortManagedProjects();
        SaveManagedProjects();
        UpdateProjectListStatus();
        ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(105, 116, 137));
        ProjectScanStatusText.Text = added
            ? LocalizationService.Get("project.added")
            : LocalizationService.Get("project.updated");
    }

    private async void OpenManagedProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: RecentProject project } || !IsUnityProject(project.Path))
        {
            ProjectScanStatusText.Text = LocalizationService.Get("project.folderUnavailable");
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
            return;
        }

        var editor = InstalledEditors.FirstOrDefault(candidate =>
            string.Equals(candidate.Version, project.EditorVersion, StringComparison.OrdinalIgnoreCase) &&
            ResolveEditorExecutable(candidate.Path) is not null);
        if (editor is null)
        {
            var availableEditors = InstalledEditors
                .Where(candidate => ResolveEditorExecutable(candidate.Path) is not null)
                .ToArray();
            var dialog = new MissingProjectEditorWindow(
                project.Name,
                project.EditorVersion,
                availableEditors)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            if (dialog.SelectedAction == MissingProjectEditorAction.UseInstalled)
            {
                editor = dialog.SelectedEditor;
            }
            else if (dialog.SelectedAction == MissingProjectEditorAction.InstallRequired)
            {
                if (!await InstallRequiredProjectEditorAsync(project))
                {
                    return;
                }

                editor = InstalledEditors.FirstOrDefault(candidate =>
                    string.Equals(candidate.Version, project.EditorVersion, StringComparison.OrdinalIgnoreCase) &&
                    ResolveEditorExecutable(candidate.Path) is not null);
            }
        }

        if (editor is null || ResolveEditorExecutable(editor.Path) is not { } executable)
        {
            ProjectScanStatusText.Text = LocalizationService.Format("project.editorMissing", project.EditorVersion);
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
            return;
        }

        var arguments = $"-projectPath {QuoteWindowsArgument(project.Path)}";
        if (!string.IsNullOrWhiteSpace(project.LaunchArguments))
        {
            arguments += $" {project.LaunchArguments.Trim()}";
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false
            });
            AddRecentProject(project.Path);
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(105, 116, 137));
            ProjectScanStatusText.Text = LocalizationService.Format("project.opening", editor.Version, project.Name);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            ProjectScanStatusText.Text = LocalizationService.Format("project.launchFailed", exception.Message);
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
        }
    }

    private async Task<bool> InstallRequiredProjectEditorAsync(RecentProject project)
    {
        if (string.IsNullOrWhiteSpace(project.EditorVersion) ||
            string.Equals(
                project.EditorVersion,
                LocalizationService.Get("common.unknownVersion"),
                StringComparison.OrdinalIgnoreCase))
        {
            ProjectScanStatusText.Text = LocalizationService.Get("project.noEditorVersion");
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
            return false;
        }

        if (!Confirm(LocalizationService.Format(
                "confirm.project.installEditor",
                project.EditorVersion)))
        {
            return false;
        }

        var result = await ExecuteCliAsync(
            LocalizationService.Format("task.editor.install", project.EditorVersion),
            ["--non-interactive", "install", project.EditorVersion, "-y"],
            trackTask: true);
        if (result?.ExitCode != 0)
        {
            return false;
        }

        await RefreshEditorsAsync();
        var installed = InstalledEditors.Any(candidate =>
            string.Equals(candidate.Version, project.EditorVersion, StringComparison.OrdinalIgnoreCase) &&
            ResolveEditorExecutable(candidate.Path) is not null);
        if (!installed)
        {
            ProjectScanStatusText.Text = LocalizationService.Format(
                "project.editorNotRecognized",
                project.EditorVersion);
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
        }

        return installed;
    }

    private static string QuoteWindowsArgument(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        if (!value.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return value;
        }

        var quoted = new System.Text.StringBuilder(value.Length + 2);
        quoted.Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                quoted.Append('\\', backslashes * 2 + 1);
                quoted.Append('"');
                backslashes = 0;
                continue;
            }

            quoted.Append('\\', backslashes);
            backslashes = 0;
            quoted.Append(character);
        }

        quoted.Append('\\', backslashes * 2);
        quoted.Append('"');
        return quoted.ToString();
    }

    private void ProjectPipelineManagement_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ProjectAiClientManagement_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ProjectSortHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sortMode } ||
            sortMode is not ("LastOpened" or "DirectoryName"))
        {
            return;
        }

        if (_projectSortMode == sortMode)
        {
            _projectSortDescending = !_projectSortDescending;
        }
        else
        {
            _projectSortMode = sortMode;
            _projectSortDescending = sortMode == "LastOpened";
        }

        _projectSortMode = sortMode;
        UpdateProjectSortHeader();
        SortManagedProjects();
    }

    private void UpdateProjectSortHeader()
    {
        ProjectNameSortGlyph.Visibility = _projectSortMode == "DirectoryName"
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProjectLastOpenedSortGlyph.Visibility = _projectSortMode == "LastOpened"
            ? Visibility.Visible
            : Visibility.Collapsed;
        var glyph = _projectSortDescending ? "\uE70D" : "\uE70E";
        ProjectNameSortGlyph.Text = glyph;
        ProjectLastOpenedSortGlyph.Text = glyph;
    }

    private void InstallProjectAiAssistant_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecentProject project ||
            !project.IsAiClientSupported || project.HasAiAssistantPackage)
        {
            return;
        }

        if (ShowLocalizedMessage(
                LocalizationService.Format(
                    "confirm.ai.add",
                    project.Name,
                    UnityAiAssistantPackageId,
                    UnityAiAssistantPackageVersion),
                LocalizationService.Get("dialog.installAi.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AddPackageToManifest(
                project.Path,
                UnityAiAssistantPackageId,
                UnityAiAssistantPackageVersion);
            RefreshProjectPipelineStatus(project);
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(16, 124, 16));
            ProjectScanStatusText.Text = LocalizationService.Format(
                "ai.added",
                UnityAiAssistantPackageVersion);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            ProjectScanStatusText.Text = LocalizationService.Format("ai.addFailed", exception.Message);
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
        }
    }

    private void RemoveProjectAiAssistant_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecentProject project ||
            !project.IsAiClientSupported || !project.HasAiAssistantPackage)
        {
            return;
        }

        if (ShowLocalizedMessage(
                LocalizationService.Format(
                    "confirm.ai.remove",
                    project.Name,
                    UnityAiAssistantPackageId),
                LocalizationService.Get("dialog.removeAi.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (!RemovePackageFromManifest(project.Path, UnityAiAssistantPackageId))
            {
                return;
            }

            RefreshProjectPipelineStatus(project);
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(105, 116, 137));
            ProjectScanStatusText.Text = LocalizationService.Format("ai.removed", project.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            ProjectScanStatusText.Text = LocalizationService.Format("ai.removeFailed", exception.Message);
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
        }
    }

    private void ConfigureProjectAiClient_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string client, DataContext: RecentProject project })
        {
            return;
        }

        RefreshProjectPipelineStatus(project);
        if (!project.IsAiClientSupported)
        {
            ProjectScanStatusText.Text = LocalizationService.Get("ai.requires60002");
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
            return;
        }

        if (!project.HasAiAssistantPackage)
        {
            ProjectScanStatusText.Text = LocalizationService.Format(
                "ai.packageMissing",
                project.Name,
                UnityAiAssistantPackageId);
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
            return;
        }

        var relayPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".unity",
            "relay",
            "relay_win.exe");
        if (!File.Exists(relayPath))
        {
            ProjectScanStatusText.Text = LocalizationService.Get("ai.relayMissing");
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
            return;
        }

        try
        {
            var clientName = client switch
            {
                "codex" => ConfigureCodexAiClient(relayPath),
                "cursor" => ConfigureJsonAiClient(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor", "mcp.json"),
                    "mcpServers",
                    relayPath,
                    includeType: false,
                    "Cursor"),
                "claude-code" => ConfigureJsonAiClient(
                    Path.Combine(project.Path, ".mcp.json"),
                    "mcpServers",
                    relayPath,
                    includeType: false,
                    "Claude Code"),
                "vscode" => ConfigureJsonAiClient(
                    Path.Combine(project.Path, ".vscode", "mcp.json"),
                    "servers",
                    relayPath,
                    includeType: true,
                    "VS Code / Copilot"),
                "claude" => ConfigureJsonAiClient(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json"),
                    "mcpServers",
                    relayPath,
                    includeType: false,
                    "Claude Desktop"),
                "windsurf" => ConfigureJsonAiClient(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".windsurf", "mcp.json"),
                    "mcpServers",
                    relayPath,
                    includeType: false,
                    "Windsurf"),
                "kiro" => ConfigureJsonAiClient(
                    Path.Combine(project.Path, ".kiro", "settings", "mcp.json"),
                    "mcpServers",
                    relayPath,
                    includeType: false,
                    "Kiro"),
                "gemini" => ConfigureJsonAiClient(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "settings.json"),
                    "mcpServers",
                    relayPath,
                    includeType: false,
                    "Gemini"),
                _ => throw new InvalidOperationException(
                    LocalizationService.Get("error.ai.unsupportedClient"))
            };

            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(16, 124, 16));
            ProjectScanStatusText.Text = LocalizationService.Format("ai.clientConfigured", clientName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            ProjectScanStatusText.Text = LocalizationService.Format("ai.clientFailed", exception.Message);
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
        }
    }

    private static string ConfigureJsonAiClient(
        string configPath,
        string serversProperty,
        string relayPath,
        bool includeType,
        string clientName)
    {
        JsonObject root;
        if (File.Exists(configPath) && !string.IsNullOrWhiteSpace(File.ReadAllText(configPath)))
        {
            root = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject
                ?? throw new JsonException(LocalizationService.Get("error.clientConfig.root"));
        }
        else
        {
            root = new JsonObject();
        }

        JsonObject servers;
        if (root[serversProperty] is null)
        {
            servers = new JsonObject();
            root[serversProperty] = servers;
        }
        else
        {
            servers = root[serversProperty] as JsonObject
            ?? throw new JsonException(LocalizationService.Format(
                "error.clientConfig.servers",
                serversProperty));
        }

        var server = new JsonObject
        {
            ["command"] = relayPath,
            ["args"] = new JsonArray("--mcp")
        };
        if (includeType)
        {
            server.Insert(0, "type", "stdio");
        }
        servers["unity-mcp"] = server;

        WriteTextAtomically(
            configPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        return clientName;
    }

    private static string ConfigureCodexAiClient(string relayPath)
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "config.toml");
        var lines = File.Exists(configPath)
            ? File.ReadAllText(configPath).Replace("\r\n", "\n").Split('\n').ToList()
            : [];
        const string sectionHeader = "[mcp_servers.unity-mcp]";
        var sectionStart = lines.FindIndex(line => string.Equals(line.Trim(), sectionHeader, StringComparison.Ordinal));
        if (sectionStart >= 0)
        {
            var sectionEnd = sectionStart + 1;
            while (sectionEnd < lines.Count && !IsTomlSectionHeader(lines[sectionEnd]))
            {
                sectionEnd++;
            }
            lines.RemoveRange(sectionStart, sectionEnd - sectionStart);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }
        if (lines.Count > 0)
        {
            lines.Add(string.Empty);
        }
        lines.Add(sectionHeader);
        lines.Add("type = \"stdio\"");
        lines.Add($"command = {JsonSerializer.Serialize(relayPath)}");
        lines.Add("args = [\"--mcp\"]");
        lines.Add(string.Empty);

        WriteTextAtomically(configPath, string.Join(Environment.NewLine, lines));
        return "Codex";
    }

    private static bool IsTomlSectionHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith('[') && trimmed.EndsWith(']');
    }

    private static void WriteTextAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".unitycli-ui.tmp";
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private async void InstallProjectPipeline_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecentProject project ||
            !project.IsPipelineSupported || project.HasPipelinePackage)
        {
            return;
        }

        var result = await ExecuteCliAsync(
            LocalizationService.Format("task.pipeline.install", project.Name),
            ["--non-interactive", "pipeline", "install", "--project-path", project.Path],
            trackTask: true);
        if (result?.ExitCode != 0)
        {
            return;
        }

        RefreshProjectPipelineStatus(project);
        UpdateProjectListStatus();
        ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(105, 116, 137));
        ProjectScanStatusText.Text = LocalizationService.Format(
            "pipeline.installed",
            project.Name,
            UnityPipelinePackageId);
    }

    private void RemoveProjectPipeline_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecentProject project ||
            !project.IsPipelineSupported || !project.HasPipelinePackage)
        {
            return;
        }

        if (ShowLocalizedMessage(
                LocalizationService.Format(
                    "confirm.pipeline.remove",
                    project.Name,
                    project.PipelinePackageName),
                LocalizationService.Get("dialog.removePipeline.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (!RemovePackageFromManifest(project.Path, UnityPipelinePackageId))
            {
                RefreshProjectPipelineStatus(project);
                ProjectScanStatusText.Text = LocalizationService.Get("pipeline.notInManifest");
                ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
                return;
            }

            RefreshProjectPipelineStatus(project);
            UpdateProjectListStatus();
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(105, 116, 137));
            ProjectScanStatusText.Text = LocalizationService.Format(
                "pipeline.removed",
                project.Name,
                UnityPipelinePackageId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            ProjectScanStatusText.Text = LocalizationService.Format("pipeline.removeFailed", exception.Message);
            ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
        }
    }

    private static void AddPackageToManifest(string projectPath, string packageId, string packageVersion)
    {
        var manifestPath = Path.Combine(projectPath, "Packages", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                LocalizationService.Get("error.manifest.missing"),
                manifestPath);
        }

        var root = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject
            ?? throw new JsonException(LocalizationService.Get("error.manifest.root"));
        var dependencies = root["dependencies"] as JsonObject
            ?? throw new JsonException(LocalizationService.Get("error.manifest.dependencies"));
        dependencies[packageId] = packageVersion;
        WriteTextAtomically(
            manifestPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static bool RemovePackageFromManifest(string projectPath, string packageId)
    {
        var manifestPath = Path.Combine(projectPath, "Packages", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        var root = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
        var dependencies = root?["dependencies"] as JsonObject;
        if (root is null || dependencies is null || !dependencies.Remove(packageId))
        {
            return false;
        }

        WriteTextAtomically(
            manifestPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

        return true;
    }

    private void ProjectMoreActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void EditProjectLaunchArguments_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecentProject project)
        {
            return;
        }

        var dialog = new ProjectLaunchArgumentsWindow(project.Name, project.LaunchArguments)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        project.LaunchArguments = dialog.LaunchArguments;
        SaveManagedProjects();
        ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(105, 116, 137));
        ProjectScanStatusText.Text = string.IsNullOrWhiteSpace(project.LaunchArguments)
            ? LocalizationService.Format("project.launchArgsCleared", project.Name)
            : LocalizationService.Format("project.launchArgsSaved", project.Name);
    }

    private void RemoveManagedProject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecentProject project ||
            ShowLocalizedMessage(
                LocalizationService.Format("confirm.project.remove", project.Name),
                LocalizationService.Get("dialog.removeProject.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        RecentProjects.Remove(project);
        SaveManagedProjects();
        UpdateProjectListStatus();
        ProjectScanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(105, 116, 137));
        ProjectScanStatusText.Text = LocalizationService.Get("project.removed");
    }

    private void RevealManagedProject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecentProject project || !Directory.Exists(project.Path))
        {
            return;
        }

        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add(project.Path);
        Process.Start(startInfo);
    }

    private void LoadRecentProjects()
    {
        try
        {
            var filePath = GetRecentProjectsFilePath();
            if (!File.Exists(filePath))
            {
                return;
            }

            var projects = JsonSerializer.Deserialize<List<RecentProject>>(File.ReadAllText(filePath)) ?? [];
            foreach (var project in projects.Where(project => IsUnityProject(project.Path)))
            {
                RefreshProjectMetadata(project);
                RefreshProjectPipelineStatus(project);
                RecentProjects.Add(project);
            }
            SortManagedProjects();
            UpdateProjectListStatus();
        }
        catch (IOException)
        {
            // Recent projects are a convenience; startup should not fail if the cache is unavailable.
        }
        catch (JsonException)
        {
        }
    }

    private void AddRecentProject(string projectPath)
    {
        AddOrUpdateManagedProject(projectPath, markOpened: true);
        SortManagedProjects();
        SaveManagedProjects();
        UpdateProjectListStatus();
    }

    private bool AddOrUpdateManagedProject(string projectPath, bool markOpened)
    {
        var fullPath = Path.GetFullPath(projectPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var project = RecentProjects.FirstOrDefault(item =>
            string.Equals(item.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        var added = project is null;
        if (project is null)
        {
            project = new RecentProject { Path = fullPath };
            RecentProjects.Add(project);
        }

        RefreshProjectMetadata(project);
        RefreshProjectPipelineStatus(project);
        if (markOpened)
        {
            project.LastOpened = DateTime.Now;
        }
        return added;
    }

    private void SortManagedProjects()
    {
        RecentProject[] sorted;
        if (_projectSortMode == "DirectoryName")
        {
            sorted = _projectSortDescending
                ? RecentProjects
                    .OrderByDescending(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenByDescending(project => project.LastOpened)
                    .ToArray()
                : RecentProjects
                    .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenByDescending(project => project.LastOpened)
                    .ToArray();
        }
        else
        {
            sorted = _projectSortDescending
                ? RecentProjects
                    .OrderByDescending(project => project.LastOpened)
                    .ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray()
                : RecentProjects
                    .OrderBy(project => project.LastOpened)
                    .ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
        }

        RecentProjects.Clear();
        foreach (var project in sorted)
        {
            RecentProjects.Add(project);
        }
    }

    private void SaveManagedProjects()
    {
        try
        {
            var filePath = GetRecentProjectsFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(RecentProjects, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void UpdateProjectListStatus()
    {
        var pipelineCount = RecentProjects.Count(project => project.HasPipelinePackage);
        ProjectListStatusText.Text = pipelineCount == 0
            ? LocalizationService.Format("project.count", RecentProjects.Count)
            : LocalizationService.Format("project.countPipeline", RecentProjects.Count, pipelineCount);
        ProjectEmptyText.Visibility = RecentProjects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void RefreshProjectPipelineStatus(RecentProject project)
    {
        var pipelinePackage = ReadProjectPackage(project.Path, UnityPipelinePackageId);
        var aiPackage = ReadProjectPackage(project.Path, UnityAiAssistantPackageId);
        project.SetComponentStatus(
            SupportsUnityPipeline(project.EditorVersion),
            pipelinePackage?.Name ?? string.Empty,
            pipelinePackage?.Version ?? string.Empty,
            SupportsUnityAiClient(project.EditorVersion),
            aiPackage?.Name ?? string.Empty,
            aiPackage?.Version ?? string.Empty);
    }

    private static (string Name, string Version)? ReadProjectPackage(string projectPath, string packageId)
    {
        var manifestPath = Path.Combine(projectPath, "Packages", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("dependencies", out var dependencies) ||
                dependencies.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (string.Equals(dependency.Name, packageId, StringComparison.OrdinalIgnoreCase))
                {
                    return (dependency.Name, dependency.Value.GetString() ?? string.Empty);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return null;
    }

    private static bool SupportsUnityPipeline(string editorVersion)
    {
        return IsUnityVersionAtLeast(editorVersion, 6000, 0, 0);
    }

    private static bool SupportsUnityAiClient(string editorVersion)
    {
        return IsUnityVersionAtLeast(editorVersion, 6000, 2, 0);
    }

    private static bool IsUnityVersionAtLeast(
        string editorVersion,
        int requiredMajor,
        int requiredMinor,
        int requiredPatch)
    {
        var match = Regex.Match(editorVersion, @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)");
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, out var patch))
        {
            return false;
        }

        if (major != requiredMajor)
        {
            return major > requiredMajor;
        }

        if (minor != requiredMinor)
        {
            return minor > requiredMinor;
        }

        return patch >= requiredPatch;
    }

    private static bool IsUnityProject(string path) =>
        Directory.Exists(path) && File.Exists(Path.Combine(path, "ProjectSettings", "ProjectVersion.txt"));

    private static string GetProjectName(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static void RefreshProjectMetadata(RecentProject project)
    {
        project.Name = GetProjectName(project.Path);
        project.EditorVersion = ReadProjectEditorVersion(project.Path);
        var playerSettings = ReadProjectPlayerSettings(project.Path);
        project.ProductName = playerSettings.ProductName;
        project.ProductVersion = playerSettings.ProductVersion;
    }

    private static IReadOnlyList<string> FindUnityProjects(string rootPath, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var versionFile in Directory.EnumerateFiles(rootPath, "ProjectVersion.txt", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectSettings = Directory.GetParent(versionFile);
            if (projectSettings is null || !string.Equals(projectSettings.Name, "ProjectSettings", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var projectPath = projectSettings.Parent?.FullName;
            if (!string.IsNullOrWhiteSpace(projectPath))
            {
                projects.Add(projectPath);
            }
        }

        return projects.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ReadProjectEditorVersion(string projectPath)
    {
        var versionFile = Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(versionFile))
        {
            return LocalizationService.Get("common.unknownVersion");
        }

        try
        {
            const string prefix = "m_EditorVersion:";
            var versionLine = File.ReadLines(versionFile)
                .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
            return versionLine?[prefix.Length..].Trim() ?? LocalizationService.Get("common.unknownVersion");
        }
        catch (IOException)
        {
            return LocalizationService.Get("common.unknownVersion");
        }
    }

    private static (string ProductName, string ProductVersion) ReadProjectPlayerSettings(string projectPath)
    {
        var settingsFile = Path.Combine(projectPath, "ProjectSettings", "ProjectSettings.asset");
        if (!File.Exists(settingsFile))
        {
            return (string.Empty, string.Empty);
        }

        try
        {
            var productName = string.Empty;
            var productVersion = string.Empty;
            foreach (var line in File.ReadLines(settingsFile))
            {
                if (TryReadUnityYamlScalar(line, "productName", out var value))
                {
                    productName = value;
                }
                else if (TryReadUnityYamlScalar(line, "bundleVersion", out value))
                {
                    productVersion = value;
                }

                if (!string.IsNullOrWhiteSpace(productName) && !string.IsNullOrWhiteSpace(productVersion))
                {
                    break;
                }
            }

            return (productName, productVersion);
        }
        catch (IOException)
        {
            return (string.Empty, string.Empty);
        }
        catch (UnauthorizedAccessException)
        {
            return (string.Empty, string.Empty);
        }
    }

    private static bool TryReadUnityYamlScalar(string line, string propertyName, out string value)
    {
        var trimmed = line.TrimStart();
        var prefix = $"{propertyName}:";
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = string.Empty;
            return false;
        }

        value = DecodeUnityYamlScalar(trimmed[prefix.Length..].Trim());
        return true;
    }

    private static string DecodeUnityYamlScalar(string value)
    {
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(value) ?? string.Empty;
            }
            catch (JsonException)
            {
                return value[1..^1];
            }
        }

        return value;
    }

    private static string GetRecentProjectsFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "unityCLI-UI",
        "recent-projects.json");

    private static string GetDefaultCliInstallDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Unity",
        "bin");

    private string LoadCliInstallDirectory()
    {
        try
        {
            var filePath = GetManagerSettingsFilePath();
            if (!File.Exists(filePath))
            {
                return GetDefaultCliInstallDirectory();
            }

            var settings = JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(filePath));
            return string.IsNullOrWhiteSpace(settings?.CliInstallDirectory)
                ? GetDefaultCliInstallDirectory()
                : settings.CliInstallDirectory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return GetDefaultCliInstallDirectory();
        }
    }

    private static string LoadManagerLanguage()
    {
        try
        {
            var filePath = GetManagerSettingsFilePath();
            if (!File.Exists(filePath))
            {
                return LocalizationService.Chinese;
            }

            var settings = JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(filePath));
            return settings?.Language ?? LocalizationService.Chinese;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return LocalizationService.Chinese;
        }
    }

    private void SaveManagerSettings()
    {
        try
        {
            var filePath = GetManagerSettingsFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(new ManagerSettings
            {
                CliInstallDirectory = CliScriptInstallPathText.Text.Trim(),
                Language = LocalizationService.CurrentLanguage
            }, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string GetManagerSettingsFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "unityCLI-UI",
        "settings.json");

    private void LoadEditorInstallationsCache()
    {
        try
        {
            var filePath = GetEditorInstallationsCacheFilePath();
            if (!File.Exists(filePath))
            {
                return;
            }

            var cachedEditors = JsonSerializer.Deserialize<List<EditorInstallationCache>>(
                File.ReadAllText(filePath)) ?? [];
            foreach (var cached in cachedEditors
                         .Where(item => !string.IsNullOrWhiteSpace(item.Version))
                         .Where(item => ResolveEditorExecutable(item.Path) is not null)
                         .GroupBy(item => item.Version, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First()))
            {
                InstalledEditors.Add(new EditorInstallation
                {
                    Version = cached.Version,
                    Architecture = cached.Architecture,
                    Path = cached.Path,
                    Modules = cached.Modules,
                    Channel = cached.Channel,
                    IsLts = cached.IsLts,
                    IsDefault = cached.IsDefault,
                    IsCachedOnly = cached.RegisteredWithCli == false
                });
            }

            if (InstalledEditors.Count > 0)
            {
                EditorCountText.Text = InstalledEditors.Count.ToString();
                EditorListStatusText.Text = LocalizationService.Format(
                    "editor.cache.detected",
                    InstalledEditors.Count);
                UpdateEditorFilters();
            }
        }
        catch (IOException)
        {
            // The CLI refresh remains authoritative when the local convenience cache is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }
    }

    private void SaveEditorInstallationsCache()
    {
        try
        {
            var cachedEditors = InstalledEditors
                .Where(editor => ResolveEditorExecutable(editor.Path) is not null)
                .GroupBy(editor => editor.Version, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(editor => new EditorInstallationCache
                {
                    Version = editor.Version,
                    Architecture = editor.Architecture,
                    Path = editor.Path,
                    Modules = editor.Modules,
                    Channel = editor.Channel,
                    IsLts = editor.IsLts,
                    IsDefault = editor.IsDefault,
                    RegisteredWithCli = !editor.IsCachedOnly
                })
                .ToArray();

            var filePath = GetEditorInstallationsCacheFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(cachedEditors, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetEditorInstallationsCacheFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "unityCLI-UI",
        "editor-installations.json");

    private async void BrowseCli_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("dialog.selectCliBinary"),
            Filter = LocalizationService.Get("dialog.executableFilter"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            _cli.ExecutablePath = dialog.FileName;
            CliPathText.Text = dialog.FileName;
            CliPathSummaryText.Text = dialog.FileName;
            UpdateCliSetupState();
            await VerifyCliAsync();
        }
    }

    private async void DetectCli_Click(object sender, RoutedEventArgs e)
    {
        DetectCli();
        if (!string.IsNullOrWhiteSpace(_cli.ExecutablePath))
        {
            await VerifyCliAsync();
        }
    }

    private async void VerifyCli_Click(object sender, RoutedEventArgs e) => await VerifyCliAsync();

    private void BrowseCliInstallDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Get("dialog.selectCliInstallFolder"),
            Multiselect = false,
            InitialDirectory = Directory.Exists(CliScriptInstallPathText.Text)
                ? CliScriptInstallPathText.Text
                : GetDefaultCliInstallDirectory()
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        CliScriptInstallPathText.Text = dialog.FolderName;
        SaveManagerSettings();
    }

    private async void InstallCliWithOfficialScript_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || !TryGetCliInstallDirectory(out var installDirectory))
        {
            return;
        }

        if (ShowLocalizedMessage(
                LocalizationService.Format("confirm.cli.install", installDirectory),
                LocalizationService.Get("dialog.installCli.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        const string channel = "beta";
        var command = string.Join(' ',
            "$ErrorActionPreference='Stop';",
            "$ProgressPreference='Continue';",
            "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;",
            "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;",
            $"$installer=Invoke-RestMethod -UseBasicParsing -Uri '{CliInstallScriptUrl}';",
            "$defaultInstall='$InstallDir = Join-Path $env:LOCALAPPDATA \"Unity\\bin\"';",
            "if (-not $installer.Contains($defaultInstall)) { throw 'Unity installer layout changed; custom install directory cannot be applied.' };",
            "$installer=$installer.Replace($defaultInstall, '$InstallDir = $env:UNITY_CLI_INSTALL_DIR');",
            "Invoke-Expression $installer");
        var arguments = new[]
        {
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-Command",
            command
        };
        var environmentVariables = new Dictionary<string, string>
        {
            ["UNITY_CLI_CHANNEL"] = channel,
            ["UNITY_CLI_INSTALL_DIR"] = installDirectory
        };
        var task = StartTask(
            LocalizationService.Get("task.cli.install"),
            ["beta", installDirectory, CliInstallScriptUrl]);
        CliInstallScriptStatusText.Text = LocalizationService.Format("cli.install.running", installDirectory);
        CliInstallScriptStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 212));
        SetBusy(true, LocalizationService.Get("common.installing"));
        AppendOutput($"> Unity official installer ({channel}) -> {installDirectory}");
        SaveManagerSettings();

        var succeeded = false;
        try
        {
            var result = await _cli.RunProcessAsync(
                "powershell.exe",
                arguments,
                line =>
                {
                    AppendOutput(line);
                    UpdateTaskProgress(task, line);
                },
                environmentVariables);
            var wasStopped = task.Status == LocalizationService.Get("common.stopping");
            succeeded = !wasStopped && result.ExitCode == 0;
            AppendOutput(wasStopped
                ? LocalizationService.Format("task.stopped", "Unity CLI")
                : succeeded
                    ? LocalizationService.Format("task.done", LocalizationService.Get("cli.install.redetecting"))
                    : LocalizationService.Format("task.failedExit", "Unity CLI", result.ExitCode));
            CompleteTask(
                task,
                succeeded,
                wasStopped
                    ? LocalizationService.Get("common.stopped")
                    : succeeded
                        ? LocalizationService.Get("common.completed")
                        : LocalizationService.Get("common.failed"));
            CliInstallScriptStatusText.Text = wasStopped
                ? LocalizationService.Get("cli.install.stopped")
                : succeeded
                    ? LocalizationService.Get("cli.install.redetecting")
                    : LocalizationService.Get("cli.install.failed");
            CliInstallScriptStatusText.Foreground = new SolidColorBrush(wasStopped
                ? Color.FromRgb(100, 112, 135)
                : succeeded
                    ? Color.FromRgb(16, 124, 16)
                    : Color.FromRgb(196, 43, 28));
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            AppendOutput(LocalizationService.Format("task.error", exception.Message));
            CompleteTask(task, succeeded: false, LocalizationService.Get("common.failed"));
            CliInstallScriptStatusText.Text = LocalizationService.Format(
                "cli.install.runFailed",
                exception.Message);
            CliInstallScriptStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
        }
        finally
        {
            SetBusy(false, string.Empty);
        }

        if (!succeeded)
        {
            return;
        }

        var installedCliPath = Path.Combine(installDirectory, "unity.exe");
        if (File.Exists(installedCliPath))
        {
            _cli.ExecutablePath = installedCliPath;
            CliPathText.Text = installedCliPath;
            CliPathSummaryText.Text = installedCliPath;
            UpdateCliSetupState();
            var verified = await VerifyCliAsync();
            CliInstallScriptStatusText.Text = verified
                ? LocalizationService.Format("cli.install.detected", installedCliPath)
                : LocalizationService.Get("cli.install.verifyFailed");
            CliInstallScriptStatusText.Foreground = new SolidColorBrush(verified
                ? Color.FromRgb(16, 124, 16)
                : Color.FromRgb(196, 43, 28));
        }
        else
        {
            CliInstallScriptStatusText.Text = LocalizationService.Get("cli.install.outputMissing");
            CliInstallScriptStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
        }
    }

    private bool TryGetCliInstallDirectory(out string installDirectory)
    {
        var input = Environment.ExpandEnvironmentVariables(CliScriptInstallPathText.Text.Trim());
        if (string.IsNullOrWhiteSpace(input) || !Path.IsPathFullyQualified(input))
        {
            installDirectory = string.Empty;
            CliInstallScriptStatusText.Text = LocalizationService.Get("cli.install.absolutePath");
            CliInstallScriptStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
            return false;
        }

        try
        {
            installDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
            if (File.Exists(installDirectory))
            {
                CliInstallScriptStatusText.Text = LocalizationService.Get("cli.install.pathIsFile");
                CliInstallScriptStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
                return false;
            }

            CliScriptInstallPathText.Text = installDirectory;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            installDirectory = string.Empty;
            CliInstallScriptStatusText.Text = LocalizationService.Format(
                "cli.install.invalidPath",
                exception.Message);
            CliInstallScriptStatusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
            return false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_scanCancellation is not null)
        {
            _scanCancellation.Cancel();
            SidebarStatusText.Text = LocalizationService.Get("common.stopping");
            return;
        }

        if (!_isBusy)
        {
            return;
        }

        if (_currentTask is not null)
        {
            _currentTask.Status = LocalizationService.Get("common.stopping");
            _currentTask.Detail = LocalizationService.Get("task.cancelingCli");
        }
        _cli.Cancel();
        AppendOutput(LocalizationService.Get("task.userStopped"));
    }

    private void ClearOutput_Click(object sender, RoutedEventArgs e)
    {
        OutputText.Clear();
        _recentOutput.Clear();
        RecentOutputText.Text = LocalizationService.Get("output.waiting");
    }

    private void ShowOutput_Click(object sender, RoutedEventArgs e) => ShowPage("Output");

    private void OpenCliDownload_Click(object sender, RoutedEventArgs e) => OpenUrl(CliInstallDocsUrl);

    private void RegisterCliPath_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_cli.ExecutablePath) || !File.Exists(_cli.ExecutablePath))
        {
            UpdateCliSetupState(LocalizationService.Get("cli.path.selectBinaryFirst"), isError: true);
            return;
        }

        var cliDirectory = Path.GetDirectoryName(Path.GetFullPath(_cli.ExecutablePath));
        if (string.IsNullOrWhiteSpace(cliDirectory))
        {
            UpdateCliSetupState(LocalizationService.Get("cli.path.unknownDirectory"), isError: true);
            return;
        }

        try
        {
            var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
            if (!ContainsPathDirectory(userPath, cliDirectory))
            {
                var updatedUserPath = string.IsNullOrWhiteSpace(userPath)
                    ? cliDirectory
                    : $"{userPath.TrimEnd(';')};{cliDirectory}";
                Environment.SetEnvironmentVariable("Path", updatedUserPath, EnvironmentVariableTarget.User);
            }

            var processPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process) ?? string.Empty;
            if (!ContainsPathDirectory(processPath, cliDirectory))
            {
                var updatedProcessPath = string.IsNullOrWhiteSpace(processPath)
                    ? cliDirectory
                    : $"{processPath.TrimEnd(';')};{cliDirectory}";
                Environment.SetEnvironmentVariable("Path", updatedProcessPath, EnvironmentVariableTarget.Process);
            }

            EnvironmentVariableNotifier.NotifyEnvironmentChanged();
            UpdateCliSetupState(LocalizationService.Get("cli.path.added"), isSuccess: true);
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            UpdateCliSetupState(
                LocalizationService.Format("cli.path.addFailed", exception.Message),
                isError: true);
        }
    }

    private void UpdateCliSetupState(string? status = null, bool isError = false, bool isSuccess = false)
    {
        var cliExists = !string.IsNullOrWhiteSpace(_cli.ExecutablePath) && File.Exists(_cli.ExecutablePath);
        CliDownloadPrompt.Visibility = cliExists ? Visibility.Collapsed : Visibility.Visible;
        RegisterCliPathButton.IsEnabled = cliExists;

        var cliDirectory = cliExists ? Path.GetDirectoryName(Path.GetFullPath(_cli.ExecutablePath)) : null;
        var isRegistered = !string.IsNullOrWhiteSpace(cliDirectory) &&
                           ContainsPathDirectory(
                               Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty,
                               cliDirectory);

        if (status is null)
        {
            status = !cliExists
                ? LocalizationService.Get("cli.path.downloadFirst")
                : isRegistered
                    ? LocalizationService.Get("cli.path.registered")
                    : LocalizationService.Get("cli.path.notRegistered");
        }

        CliEnvironmentStatusText.Text = status;
        CliEnvironmentStatusText.Foreground = new SolidColorBrush(isError
            ? Color.FromRgb(196, 43, 28)
            : isSuccess || isRegistered
                ? Color.FromRgb(16, 124, 16)
                : Color.FromRgb(100, 112, 135));
        RegisterCliPathButton.Content = isRegistered
            ? LocalizationService.Get("cli.path.button.registered")
            : LocalizationService.Get("cli.path.button.register");
        RegisterCliPathButton.IsEnabled = cliExists && !isRegistered;
    }

    private static bool ContainsPathDirectory(string pathValue, string directory)
    {
        var normalizedDirectory = NormalizeEnvironmentPath(directory);
        return pathValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => NormalizeEnvironmentPath(Environment.ExpandEnvironmentVariables(entry.Trim('"'))))
            .Any(entry => string.Equals(entry, normalizedDirectory, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeEnvironmentPath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OpenDocs_Click(object sender, RoutedEventArgs e) => OpenUrl(DocsUrl);
    private void OpenUsageDocs_Click(object sender, RoutedEventArgs e) => OpenUrl(UsageDocsUrl);
    private void OpenReferenceDocs_Click(object sender, RoutedEventArgs e) => OpenUrl(ReferenceDocsUrl);
}

public sealed class ManagerSettings
{
    public string CliInstallDirectory { get; init; } = string.Empty;
    public string Language { get; init; } = LocalizationService.Chinese;
}

public sealed class EditorInstallation
{
    public string Version { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Modules { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public bool IsLts { get; init; }
    public bool IsDefault { get; init; }
    public bool IsCachedOnly { get; init; }

    public bool IsPreview =>
        Version.Contains("a", StringComparison.OrdinalIgnoreCase) ||
        Version.Contains("b", StringComparison.OrdinalIgnoreCase) ||
        Channel.Contains("alpha", StringComparison.OrdinalIgnoreCase) ||
        Channel.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
        Channel.Contains("preview", StringComparison.OrdinalIgnoreCase);

    public bool IsLtsRelease =>
        IsLts ||
        Channel.Contains("lts", StringComparison.OrdinalIgnoreCase) ||
        (!IsPreview && (Version.StartsWith("6000.0.", StringComparison.OrdinalIgnoreCase) ||
                        Version.Contains(".3.", StringComparison.OrdinalIgnoreCase)));

    public string ChannelLabel => IsPreview ? "PREVIEW" : IsLtsRelease ? "LTS" : "RELEASE";
    public string DefaultLabel => IsDefault
        ? LocalizationService.Get("common.yes")
        : LocalizationService.Get("common.no");
    public string SourceLabel => IsCachedOnly
        ? LocalizationService.Get("model.localCache")
        : "Unity Hub / CLI";
    public string InstallationDateText
    {
        get
        {
            try
            {
                var executable = ResolveExecutableForDisplay(Path);
        return executable is null
            ? LocalizationService.Get("common.unknown")
            : File.GetCreationTime(executable).ToString("yyyy-MM-dd");
            }
            catch (IOException)
            {
        return LocalizationService.Get("common.unknown");
            }
            catch (UnauthorizedAccessException)
            {
        return LocalizationService.Get("common.unknown");
            }
        }
    }

    public string AvailabilityLabel => ResolveExecutableForDisplay(Path) is null
        ? LocalizationService.Get("model.pathUnavailable")
        : LocalizationService.Get("model.launchable");
    public string ModuleSummary => string.IsNullOrWhiteSpace(Modules)
        ? LocalizationService.Get("model.modulesNotRead")
        : Modules;
    public string ModuleCountText => string.IsNullOrWhiteSpace(Modules)
        ? LocalizationService.Get("model.notRead")
        : LocalizationService.Format(
            "model.moduleCount",
            Modules.Split(',', StringSplitOptions.RemoveEmptyEntries).Length);

    private static string? ResolveExecutableForDisplay(string path)
    {
        if (File.Exists(path))
        {
            return path;
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        return new[] { System.IO.Path.Combine(path, "Editor", "Unity.exe"), System.IO.Path.Combine(path, "Unity.exe") }
            .FirstOrDefault(File.Exists);
    }
}

public sealed class EditorInstallationCache
{
    public string Version { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Modules { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public bool IsLts { get; set; }
    public bool IsDefault { get; set; }
    public bool? RegisteredWithCli { get; set; }
}

public sealed class EditorRelease
{
    public string Version { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public bool Installed { get; init; }
    public bool IsDefault { get; init; }
    public string Platforms { get; init; } = string.Empty;

    public bool IsPreview =>
        Version.Contains("a", StringComparison.OrdinalIgnoreCase) ||
        Version.Contains("b", StringComparison.OrdinalIgnoreCase);
    public bool IsLts => !IsPreview &&
        (Version.StartsWith("6000.0.", StringComparison.OrdinalIgnoreCase) ||
         Version.StartsWith("6000.3.", StringComparison.OrdinalIgnoreCase) ||
         Version.StartsWith("2022.3.", StringComparison.OrdinalIgnoreCase));
    public string ReleaseTypeLabel => IsPreview ? "PREVIEW" : IsLts ? "LTS" : "RELEASE";
    public string InstallStateLabel => Installed
        ? LocalizationService.Get("common.installed")
        : LocalizationService.Get("common.available");
    public string ArchitecturePlatformLabel => $"{(string.IsNullOrWhiteSpace(Architecture) ? LocalizationService.Get("common.unknownArchitecture") : Architecture)} · {(string.IsNullOrWhiteSpace(Platforms) ? "Windows" : Platforms)}";
}

public sealed class UnityModuleInfo : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public bool Installed { get; init; }
    public bool CanInstall => !Installed;
    public string StateLabel => Installed
        ? LocalizationService.Get("common.installed")
        : LocalizationService.Get("common.notInstalled");
    public string Glyph
    {
        get
        {
            var value = $"{Id} {Name}";
            if (value.Contains("sdk", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("ndk", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("jdk", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("visual studio", StringComparison.OrdinalIgnoreCase)) return "\uE943";
            if (value.Contains("windows", StringComparison.OrdinalIgnoreCase)) return "\uE782";
            if (value.Contains("android", StringComparison.OrdinalIgnoreCase)) return "\uE8EA";
            if (value.Contains("ios", StringComparison.OrdinalIgnoreCase) || value.Contains("apple", StringComparison.OrdinalIgnoreCase)) return "\uE8A9";
            if (value.Contains("web", StringComparison.OrdinalIgnoreCase)) return "\uE774";
            if (value.Contains("documentation", StringComparison.OrdinalIgnoreCase)) return "\uE82D";
            return "\uE7B8";
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyLocalizationChanged() => OnPropertyChanged(nameof(StateLabel));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class RecentProject : INotifyPropertyChanged
{
    private bool _isPipelineSupported;
    private string _pipelinePackageName = string.Empty;
    private string _pipelinePackageVersion = string.Empty;
    private bool _isAiClientSupported;
    private string _aiAssistantPackageName = string.Empty;
    private string _aiAssistantPackageVersion = string.Empty;
    private string _productName = string.Empty;
    private string _productVersion = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string EditorVersion { get; set; } = string.Empty;
    public string LaunchArguments { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; }
    public string ProductName
    {
        get => _productName;
        set
        {
            if (string.Equals(_productName, value, StringComparison.Ordinal))
            {
                return;
            }

            _productName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProductInfoText));
        }
    }

    public string ProductVersion
    {
        get => _productVersion;
        set
        {
            if (string.Equals(_productVersion, value, StringComparison.Ordinal))
            {
                return;
            }

            _productVersion = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProductInfoText));
        }
    }

    [JsonIgnore]
    public string ProductInfoText => LocalizationService.Format(
        "project.productInfo",
        string.IsNullOrWhiteSpace(ProductName) ? LocalizationService.Get("project.notSet") : ProductName,
        string.IsNullOrWhiteSpace(ProductVersion) ? LocalizationService.Get("project.notSet") : ProductVersion);

    public string LastOpenedText => LastOpened == default
        ? LocalizationService.Get("project.neverOpened")
        : LastOpened.ToString("yyyy-MM-dd HH:mm");

    [JsonIgnore]
    public bool IsPipelineSupported => _isPipelineSupported;

    [JsonIgnore]
    public string PipelinePackageName => _pipelinePackageName;

    [JsonIgnore]
    public string PipelinePackageVersion => _pipelinePackageVersion;

    [JsonIgnore]
    public bool HasPipelinePackage => !string.IsNullOrWhiteSpace(PipelinePackageName);

    [JsonIgnore]
    public bool CanInstallPipelinePackage => IsPipelineSupported && !HasPipelinePackage;

    [JsonIgnore]
    public bool ShowPipelineStatus => !IsPipelineSupported || HasPipelinePackage;

    [JsonIgnore]
    public string PipelineStatusText => IsPipelineSupported
        ? HasPipelinePackage ? LocalizationService.Get("pipeline.status.installed") : string.Empty
        : LocalizationService.Get("pipeline.status.unsupported");

    [JsonIgnore]
    public string PipelinePackageDetails => !IsPipelineSupported
        ? HasPipelinePackage
            ? LocalizationService.Format(
                "pipeline.detail.unsupportedDetected",
                PipelinePackageName,
                PipelinePackageVersion).TrimEnd()
            : LocalizationService.Get("pipeline.detail.unsupported")
        : HasPipelinePackage
            ? LocalizationService.Format(
                "pipeline.detail.installed",
                PipelinePackageName,
                PipelinePackageVersion).TrimEnd()
            : LocalizationService.Get("pipeline.status.notInstalled");

    [JsonIgnore]
    public string PipelineManagementHint => IsPipelineSupported
        ? LocalizationService.Get("pipeline.hint.manage")
        : LocalizationService.Get("pipeline.hint.requires6");

    [JsonIgnore]
    public bool IsAiClientSupported => _isAiClientSupported;

    [JsonIgnore]
    public string AiAssistantPackageName => _aiAssistantPackageName;

    [JsonIgnore]
    public string AiAssistantPackageVersion => _aiAssistantPackageVersion;

    [JsonIgnore]
    public bool HasAiAssistantPackage => !string.IsNullOrWhiteSpace(AiAssistantPackageName);

    [JsonIgnore]
    public bool CanInstallAiAssistantPackage => IsAiClientSupported && !HasAiAssistantPackage;

    [JsonIgnore]
    public bool CanConfigureAiClient => IsAiClientSupported && HasAiAssistantPackage;

    [JsonIgnore]
    public string AiComponentStatusText => !IsAiClientSupported
        ? LocalizationService.Get("ai.status.unsupported")
        : HasAiAssistantPackage
            ? LocalizationService.Get("ai.status.installed")
            : LocalizationService.Get("ai.status.notInstalled");

    [JsonIgnore]
    public string AiComponentDetails => !IsAiClientSupported
        ? HasAiAssistantPackage
            ? LocalizationService.Format(
                "ai.detail.unsupportedDetected",
                AiAssistantPackageName,
                AiAssistantPackageVersion).TrimEnd()
            : LocalizationService.Get("ai.detail.unsupported")
        : HasAiAssistantPackage
            ? $"{AiAssistantPackageName} {AiAssistantPackageVersion}".TrimEnd()
            : LocalizationService.Get("ai.detail.installHint");

    [JsonIgnore]
    public string AiClientManagementHint => !IsAiClientSupported
        ? LocalizationService.Get("ai.hint.requires60002")
        : HasAiAssistantPackage
            ? LocalizationService.Get("ai.hint.configure")
            : LocalizationService.Get("ai.hint.install");

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyLocalizationChanged()
    {
        OnPropertyChanged(nameof(ProductInfoText));
        OnPropertyChanged(nameof(LastOpenedText));
        OnPropertyChanged(nameof(PipelineStatusText));
        OnPropertyChanged(nameof(PipelinePackageDetails));
        OnPropertyChanged(nameof(PipelineManagementHint));
        OnPropertyChanged(nameof(AiComponentStatusText));
        OnPropertyChanged(nameof(AiComponentDetails));
        OnPropertyChanged(nameof(AiClientManagementHint));
    }

    public void SetComponentStatus(
        bool isPipelineSupported,
        string pipelinePackageName,
        string pipelinePackageVersion,
        bool isAiClientSupported,
        string aiAssistantPackageName,
        string aiAssistantPackageVersion)
    {
        _isPipelineSupported = isPipelineSupported;
        _pipelinePackageName = pipelinePackageName;
        _pipelinePackageVersion = pipelinePackageVersion;
        _isAiClientSupported = isAiClientSupported;
        _aiAssistantPackageName = aiAssistantPackageName;
        _aiAssistantPackageVersion = aiAssistantPackageVersion;

        OnPropertyChanged(nameof(IsPipelineSupported));
        OnPropertyChanged(nameof(PipelinePackageName));
        OnPropertyChanged(nameof(PipelinePackageVersion));
        OnPropertyChanged(nameof(HasPipelinePackage));
        OnPropertyChanged(nameof(CanInstallPipelinePackage));
        OnPropertyChanged(nameof(ShowPipelineStatus));
        OnPropertyChanged(nameof(PipelineStatusText));
        OnPropertyChanged(nameof(PipelinePackageDetails));
        OnPropertyChanged(nameof(PipelineManagementHint));
        OnPropertyChanged(nameof(IsAiClientSupported));
        OnPropertyChanged(nameof(AiAssistantPackageName));
        OnPropertyChanged(nameof(AiAssistantPackageVersion));
        OnPropertyChanged(nameof(HasAiAssistantPackage));
        OnPropertyChanged(nameof(CanInstallAiAssistantPackage));
        OnPropertyChanged(nameof(CanConfigureAiClient));
        OnPropertyChanged(nameof(AiComponentStatusText));
        OnPropertyChanged(nameof(AiComponentDetails));
        OnPropertyChanged(nameof(AiClientManagementHint));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class CliTaskItem : INotifyPropertyChanged
{
    private string _status = string.Empty;
    private string _detail = string.Empty;
    private double _progress;
    private bool _isIndeterminate;
    private bool _isRunning = true;
    private DateTime? _completedAt;

    public string Title { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }
    public string StartedText => LocalizationService.Format("task.startedAt", StartedAt);

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetField(ref _detail, value);
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (SetField(ref _progress, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set
        {
            if (SetField(ref _isIndeterminate, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetField(ref _isRunning, value);
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set => SetField(ref _completedAt, value);
    }

    public string ProgressText => IsIndeterminate ? Status : $"{Progress:0.#}%";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyLocalizationChanged() => OnPropertyChanged(nameof(StartedText));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
