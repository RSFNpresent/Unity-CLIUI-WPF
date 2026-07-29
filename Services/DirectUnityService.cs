using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using unity_cli_ui.Models;

namespace unity_cli_ui.Services;

public sealed class DirectUnityService
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly UnityReleaseCatalogClient _catalog = new(SharedHttpClient);
    private readonly SafePackageExtractor _extractor = new();
    private readonly DirectInstallStateStore _state = new(DirectInstallPaths.State);

    public Task<IReadOnlyList<UnityReleaseInfo>> GetReleasesAsync(CancellationToken cancellationToken) =>
        _catalog.GetReleasesAsync(100, 0, cancellationToken);

    public Task<UnityReleaseInfo> GetReleaseAsync(string version, CancellationToken cancellationToken) =>
        _catalog.GetReleaseAsync(version, cancellationToken);

    public async Task<UnityReleaseInfo?> GetLatestPatchAsync(string version, CancellationToken cancellationToken)
    {
        var parts = version.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }
        var releaseLine = $"{parts[0]}.{parts[1]}";
        var releases = await _catalog.SearchReleasesAsync(releaseLine, cancellationToken);
        return releases
            .Where(release => release.GetWindowsX64Download() is not null)
            .Where(release => release.Version.StartsWith(releaseLine + ".", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(release => release.ReleaseDate)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<DirectModuleStatus>> GetModulesAsync(
        string version,
        string editorPath,
        CancellationToken cancellationToken)
    {
        var release = await _catalog.GetReleaseAsync(version, cancellationToken);
        var download = release.GetWindowsX64Download()
            ?? throw new InvalidOperationException($"Unity {release.Version} has no Windows x64 download.");
        var state = await _state.LoadEditorAsync(release.Version, cancellationToken);
        var detected = InstalledEditorScanner.DetectInstalledModules(editorPath)
            .Concat(state?.InstalledModuleIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return download.Modules
            .Where(IsSelectableModule)
            .Select(module => new DirectModuleStatus(module, detected.Contains(module.Id)))
            .ToArray();
    }

    public Task<IReadOnlyList<InstalledEditorInfo>> ScanInstalledEditorsAsync(
        string root,
        CancellationToken cancellationToken) =>
        Task.Run(() => InstalledEditorScanner.Scan(root, cancellationToken), cancellationToken);

    public Task<DirectInstallResult> InstallEditorAsync(
        DirectInstallRequest request,
        IProgress<DirectOperationProgress>? progress,
        CancellationToken cancellationToken) =>
        InstallAsync(request, progress, cancellationToken);

    public async Task<DirectInstallResult> InstallModulesAsync(
        DirectModuleInstallRequest request,
        IProgress<DirectOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var installRequest = new DirectInstallRequest(
            request.Version,
            request.EditorPath,
            request.ModuleIds,
            request.DryRun,
            request.AcceptEula,
            request.PackageCacheDirectory,
            request.KeepPackageCache);
        return await InstallAsync(installRequest, progress, cancellationToken, installEditor: false);
    }

    public async Task UninstallEditorAsync(
        string version,
        string editorPath,
        IProgress<DirectOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        editorPath = Path.GetFullPath(editorPath);
        var uninstaller = Path.Combine(editorPath, "Editor", "Uninstall.exe");
        if (!File.Exists(uninstaller))
        {
            throw new FileNotFoundException("Unity's official Uninstall.exe was not found. Direct mode will not delete editor files manually.", uninstaller);
        }

        progress?.Report(new DirectOperationProgress(
            DirectInstallPhase.Installing,
            $"Running Unity {version} uninstaller",
            WriteToLog: true));
        await RunProcessAsync(uninstaller, ["/S"], cancellationToken);
        if (File.Exists(Path.Combine(editorPath, "Editor", "Unity.exe")))
        {
            throw new InvalidOperationException($"Unity {version} is still present after the official uninstaller exited.");
        }
        _state.RemoveEditor(version);
        progress?.Report(new DirectOperationProgress(DirectInstallPhase.Completed, $"Uninstalled Unity {version}", 100, true));
    }

    private async Task<DirectInstallResult> InstallAsync(
        DirectInstallRequest request,
        IProgress<DirectOperationProgress>? progress,
        CancellationToken cancellationToken,
        bool installEditor = true)
    {
        var transactionId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var transactionDirectory = Path.Combine(DirectInstallPaths.Staging, transactionId);
        string editorPath = string.Empty;
        try
        {
            progress?.Report(new DirectOperationProgress(DirectInstallPhase.Planning, $"Resolving Unity {request.Version}", WriteToLog: true));
            var release = await _catalog.GetReleaseAsync(request.Version, cancellationToken);
            var editorDownload = release.GetWindowsX64Download()
                ?? throw new InvalidOperationException($"Unity {release.Version} has no Windows x64 download.");
            editorPath = installEditor
                ? ResolveEditorInstallPath(request.InstallRoot, release.Version)
                : ValidateExistingEditorPath(request.InstallRoot, release.Version);
            await SaveTransactionAsync(transactionId, release.Version, editorPath, DirectInstallPhase.Planning, "Resolved official release", cancellationToken);

            var modulePlan = DirectPackagePlanner.Build(release.Version, editorDownload.Modules, request.ModuleIds);
            var selectedModules = modulePlan.SelectedModules;
            var modulePackages = modulePlan.Packages;
            if (!request.AcceptEula && modulePackages.Any(package => package.Package.Eula.Count > 0))
            {
                throw new InvalidOperationException("One or more selected modules require accepting their license terms.");
            }

            var packagePlans = new List<DirectPackagePlan>();
            if (installEditor)
            {
                packagePlans.Add(CreateEditorPlan(release, editorDownload));
            }
            packagePlans.AddRange(modulePackages.Select(item => CreateModulePlan(item.Package, item.ParentId)));
            packagePlans = packagePlans
                .GroupBy(package => package.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            foreach (var package in packagePlans)
            {
                PackageSafetyPolicy.ValidateManifestPackage(package);
            }

            if (request.DryRun)
            {
                foreach (var package in packagePlans)
                {
                    progress?.Report(new DirectOperationProgress(
                        DirectInstallPhase.Planning,
                        $"{package.Type} {package.Id}: {package.Url}",
                        WriteToLog: true));
                }
                return new DirectInstallResult(release.Version, editorPath, selectedModules.Select(module => module.Id).ToArray(), true);
            }

            var downloads = new PackageDownloadService(
                SharedHttpClient,
                ResolvePackageCacheDirectory(request.PackageCacheDirectory),
                3);
            await SaveTransactionAsync(transactionId, release.Version, editorPath, DirectInstallPhase.Downloading, "Downloading packages", cancellationToken);
            var receivedByPackage = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var expectedTotal = packagePlans.Sum(package => Math.Max(0, package.DownloadSize));
            var downloadTasks = packagePlans.Select(package => downloads.DownloadAsync(
                package,
                item =>
                {
                    receivedByPackage[item.PackageId] = item.BytesReceived;
                    var received = receivedByPackage.Values.Sum();
                    var percent = expectedTotal > 0 ? Math.Min(99, received * 100d / expectedTotal) : (double?)null;
                    progress?.Report(new DirectOperationProgress(
                        DirectInstallPhase.Downloading,
                        $"Downloading {item.PackageId}",
                        percent));
                },
                cancellationToken));
            var downloaded = await Task.WhenAll(downloadTasks);

            await SaveTransactionAsync(transactionId, release.Version, editorPath, DirectInstallPhase.Verifying, "Packages verified", cancellationToken);
            progress?.Report(new DirectOperationProgress(DirectInstallPhase.Verifying, "All package integrity checks passed", WriteToLog: true));

            await SaveTransactionAsync(transactionId, release.Version, editorPath, DirectInstallPhase.Installing, "Installing packages", cancellationToken);
            if (installEditor)
            {
                var editorPackage = downloaded.Single(package => package.Package.IsEditor);
                progress?.Report(new DirectOperationProgress(DirectInstallPhase.Installing, $"Installing Unity {release.Version}", WriteToLog: true));
                await RunUnityInstallerAsync(editorPackage, editorPath, cancellationToken);
                VerifyInstalledEditor(editorPath, release);
            }

            foreach (var executable in downloaded.Where(package => package.Package.Type == "EXE" && !package.Package.IsEditor))
            {
                progress?.Report(new DirectOperationProgress(DirectInstallPhase.Installing, $"Installing {executable.Package.Name}", WriteToLog: true));
                await RunUnityInstallerAsync(executable, editorPath, cancellationToken);
            }

            var stagedTasks = downloaded
                .Where(package => package.Package.Type is "ZIP" or "PO")
                .Select(package =>
                {
                    var packageStaging = Path.Combine(transactionDirectory, SanitizePathSegment(package.Package.Id));
                    return _extractor.StageAsync(package, editorPath, packageStaging, cancellationToken);
                });
            var stagedPackages = await Task.WhenAll(stagedTasks);
            foreach (var staged in stagedPackages)
            {
                progress?.Report(new DirectOperationProgress(DirectInstallPhase.Installing, $"Committing {staged.Package.Name}", WriteToLog: true));
                await _extractor.CommitAsync(staged, editorPath, cancellationToken);
            }

            var previousState = await _state.LoadEditorAsync(release.Version, cancellationToken);
            var installedModuleIds = new HashSet<string>(previousState?.InstalledModuleIds ?? [], StringComparer.OrdinalIgnoreCase);
            installedModuleIds.UnionWith(selectedModules.Select(module => module.Id));
            installedModuleIds.UnionWith(InstalledEditorScanner.DetectInstalledModules(editorPath));
            await _state.SaveEditorAsync(new DirectEditorState
            {
                Version = release.Version,
                Revision = release.ShortRevision,
                EditorPath = editorPath,
                InstalledModuleIds = installedModuleIds,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken);

            await SaveTransactionAsync(transactionId, release.Version, editorPath, DirectInstallPhase.Completed, "Installation completed", cancellationToken);
            progress?.Report(new DirectOperationProgress(DirectInstallPhase.Completed, $"Unity {release.Version} installation completed", 100, true));
            if (!request.KeepPackageCache)
            {
                DeleteDownloadedCacheFiles(downloaded);
            }
            return new DirectInstallResult(release.Version, editorPath, installedModuleIds.ToArray(), false);
        }
        catch (OperationCanceledException)
        {
            await SaveTransactionAsync(transactionId, request.Version, editorPath, DirectInstallPhase.Cancelled, "Operation cancelled", CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await SaveTransactionAsync(transactionId, request.Version, editorPath, DirectInstallPhase.Failed, exception.Message, CancellationToken.None);
            throw;
        }
        finally
        {
            if (Directory.Exists(transactionDirectory))
            {
                try
                {
                    Directory.Delete(transactionDirectory, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private Task SaveTransactionAsync(
        string id,
        string version,
        string editorPath,
        DirectInstallPhase phase,
        string detail,
        CancellationToken cancellationToken) =>
        _state.SaveTransactionAsync(new DirectInstallTransaction
        {
            Id = id,
            Version = version,
            EditorPath = editorPath,
            Phase = phase,
            Detail = detail,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        }, cancellationToken);

    private static bool IsSelectableModule(UnityModulePackage module)
    {
        if (module.Hidden || string.IsNullOrWhiteSpace(module.Id) || string.IsNullOrWhiteSpace(module.Url))
        {
            return false;
        }
        try
        {
            PackageSafetyPolicy.ValidateManifestPackage(CreateModulePlan(module, null));
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static DirectPackagePlan CreateEditorPlan(UnityReleaseInfo release, UnityEditorDownload download) => new(
        "editor",
        $"{release.Version}-windows-x86_64-editor",
        $"Unity Editor {release.Version}",
        new Uri(download.Url, UriKind.Absolute),
        download.Type.ToUpperInvariant(),
        download.DownloadSize.Value,
        download.Integrity,
        "{UNITY_PATH}",
        null,
        true,
        null);

    private static DirectPackagePlan CreateModulePlan(UnityModulePackage module, string? parentId) => new(
        module.Id,
        string.IsNullOrWhiteSpace(module.Slug) ? module.Id : module.Slug,
        string.IsNullOrWhiteSpace(module.Name) ? module.Id : module.Name,
        new Uri(module.Url, UriKind.Absolute),
        module.Type.ToUpperInvariant(),
        module.DownloadSize.Value,
        module.Integrity,
        module.Destination,
        module.ExtractedPathRename,
        false,
        parentId);

    private static string ResolveEditorInstallPath(string installRoot, string version)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            throw new InvalidOperationException("Editor install root is empty.");
        }
        if (version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || version.Contains('/') || version.Contains('\\'))
        {
            throw new InvalidDataException($"Unity version cannot be used as an install directory: {version}");
        }
        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(installRoot));
        Directory.CreateDirectory(root);
        var editorPath = Path.GetFullPath(Path.Combine(root, version));
        if (!editorPath.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Editor install path escaped its configured root.");
        }
        if (File.Exists(Path.Combine(editorPath, "Editor", "Unity.exe")))
        {
            throw new InvalidOperationException($"Unity {version} is already installed at {editorPath}.");
        }
        return editorPath;
    }

    private static string ResolvePackageCacheDirectory(string packageCacheDirectory)
    {
        var input = string.IsNullOrWhiteSpace(packageCacheDirectory)
            ? DirectInstallPaths.Packages
            : Environment.ExpandEnvironmentVariables(packageCacheDirectory);
        var cacheDirectory = Path.GetFullPath(input);
        if (File.Exists(cacheDirectory))
        {
            throw new InvalidDataException("Package cache path points to a file.");
        }
        Directory.CreateDirectory(cacheDirectory);
        return cacheDirectory;
    }

    private static void DeleteDownloadedCacheFiles(IEnumerable<DownloadedPackage> downloadedPackages)
    {
        foreach (var path in downloadedPackages
                     .SelectMany(package => new[] { package.FilePath, package.FilePath + ".metadata.json" })
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string ValidateExistingEditorPath(string editorPath, string version)
    {
        editorPath = Path.GetFullPath(editorPath);
        var unity = Path.Combine(editorPath, "Editor", "Unity.exe");
        if (!File.Exists(unity))
        {
            throw new FileNotFoundException($"Unity {version} was not found at the selected install path.", unity);
        }
        return editorPath;
    }

    private static async Task RunUnityInstallerAsync(
        DownloadedPackage package,
        string editorPath,
        CancellationToken cancellationToken)
    {
        PackageSafetyPolicy.ValidateManifestPackage(package.Package);
        await RunProcessAsync(package.FilePath, ["/S", $"/D={editorPath}"], cancellationToken);
    }

    private static async Task RunProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        });
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Installer {Path.GetFileName(executablePath)} exited with code {process.ExitCode}.");
        }
    }

    private static void VerifyInstalledEditor(string editorPath, UnityReleaseInfo release)
    {
        var executable = Path.Combine(editorPath, "Editor", "Unity.exe");
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException("Unity installer completed but Editor\\Unity.exe was not created.");
        }
        var productVersion = FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? string.Empty;
        if (!productVersion.StartsWith(release.Version, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(release.ShortRevision) &&
             !productVersion.Contains(release.ShortRevision, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"Installed editor version '{productVersion}' does not match {release.Version}_{release.ShortRevision}.");
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }
}
