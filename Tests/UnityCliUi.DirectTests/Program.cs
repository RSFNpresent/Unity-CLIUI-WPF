using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using unity_cli_ui;
using unity_cli_ui.Models;
using unity_cli_ui.Services;

namespace UnityCliUi.DirectTests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Test)> Tests =
    [
        ("Release API parsing", TestReleaseApiParsingAsync),
        ("Release API flexible size values", UnityReleaseModelTests.RunAsync),
        ("Management mode and module fallback policies", ManagementModePolicyTests.RunAsync),
        ("System animation policy", SystemAnimationPolicyTests.RunAsync),
        ("Maximized window taskbar bounds", WindowMaximizeBoundsTests.RunAsync),
        ("Project folder drop selection", ProjectFolderDropTests.RunAsync),
        ("Unity version normalization and matching", UnityVersionPolicyTests.RunAsync),
        ("First-run language and management choices", TestFirstRunChoicesAsync),
        ("Minimal Unity project creation", TestMinimalUnityProjectCreationAsync),
        ("Project creation path safety", TestProjectCreationPathSafetyAsync),
        ("Release API pagination limit", TestReleaseApiPaginationAsync),
        ("Integrity encodings", TestIntegrityEncodingsAsync),
        ("Dependency DAG and version guard", TestDependencyPlannerAsync),
        ("Manifest path traversal", TestManifestPathTraversalAsync),
        ("ZIP traversal and exclusion", TestZipSecurityAsync),
        ("ZIP rename rules", TestRenameRulesAsync),
        ("Download cancellation and resume", TestDownloadResumeAsync),
        ("Mutable package ETag revalidation", TestMutablePackageEtagAsync)
    ];

    public static async Task<int> Main()
    {
        var failures = new List<string>();
        foreach (var (name, test) in Tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures.Add($"{name}: {exception.Message}");
                Console.WriteLine($"FAIL  {name}: {exception}");
            }
        }

        Console.WriteLine($"{Tests.Count - failures.Count}/{Tests.Count} tests passed");
        return failures.Count == 0 ? 0 : 1;
    }

    private static Task TestReleaseApiParsingAsync()
    {
        const string json = """
        {
          "offset": 0,
          "limit": 1,
          "total": 1,
          "results": [{
            "version": "6000.5.2f1",
            "releaseDate": "2026-07-01T10:41:48.988Z",
            "shortRevision": "eb73d3b415a1",
            "stream": "SUPPORTED",
            "downloads": [{
              "url": "https://download.unity3d.com/download_unity/eb73d3b415a1/Windows64EditorInstaller/UnitySetup64-6000.5.2f1.exe",
              "type": "EXE",
              "platform": "WINDOWS",
              "architecture": "X86_64",
              "downloadSize": { "value": 4141852744, "unit": "BYTE" },
              "integrity": "md5-/krtNlVyuttMGfiZQsIBSQ==",
              "modules": [{
                "id": "android",
                "slug": "6000.5.2f1-windows-x86_64-android",
                "url": "https://download.unity3d.com/TargetSupportInstaller/android.exe",
                "type": "EXE",
                "downloadSize": { "value": 10, "unit": "BYTE" },
                "subModules": [{
                  "id": "android-ndk-r27c",
                  "slug": "6000.5.2f1-windows-x86_64-android-ndk-r27c",
                  "url": "https://dl.google.com/android/repository/android-ndk-r27c-windows.zip",
                  "type": "ZIP",
                  "downloadSize": { "value": 20, "unit": "BYTE" },
                  "destination": "{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer/NDK",
                  "extractedPathRename": {
                    "from": "{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer/NDK/android-ndk-r27c",
                    "to": "{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer/NDK"
                  }
                }]
              }]
            }]
          }]
        }
        """;
        var response = JsonSerializer.Deserialize<UnityReleaseResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        Equal("6000.5.2f1", response?.Results.Single().Version);
        Equal("eb73d3b415a1", response?.Results.Single().ShortRevision);
        var ndk = response?.Results.Single().GetWindowsX64Download()?.Modules.Single().SubModules.Single();
        Equal("{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer/NDK", ndk?.ExtractedPathRename?.To);
        return Task.CompletedTask;
    }

    private static Task TestFirstRunChoicesAsync()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                LocalizationService.Initialize(LocalizationService.Chinese);
                var dialog = new FirstRunDialog(LocalizationService.Chinese);
                Equal(LocalizationService.Chinese, dialog.SelectedLanguage);
                Equal<ManagementMode?>(null, dialog.SelectedManagementMode);

                SetNamedBooleanProperty(dialog, "EnglishLanguageRadio", "IsChecked", true);
                Equal(LocalizationService.English, dialog.SelectedLanguage);

                SetNamedBooleanProperty(dialog, "DirectModeRadio", "IsChecked", true);
                Equal<ManagementMode?>(ManagementMode.Direct, dialog.SelectedManagementMode);
                Equal(true, GetNamedBooleanProperty(dialog, "ContinueButton", "IsEnabled"));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                LocalizationService.Initialize(LocalizationService.Chinese);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("First-run dialog interaction failed.", failure);
        }
        return Task.CompletedTask;
    }

    private static Task TestMinimalUnityProjectCreationAsync()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var editor2022 = CreateEditorFixture(root, "editor-2022", "2.0.22", "1.2.5");
            var result2022 = UnityProjectCreator.Create(new UnityProjectCreationRequest(
                root,
                "Minimal2022",
                "2022.3.62f3",
                editor2022));
            Equal("2.0.22", result2022.EditorMetadata.VisualStudioPackageVersion);
            Equal("1.2.5", result2022.EditorMetadata.VisualStudioCodePackageVersion);
            AssertMinimalProject(result2022.ProjectPath, "2022.3.62f3", "2.0.22", "1.2.5");

            var editor6 = CreateEditorFixture(root, "editor-6", "2.0.26", null);
            var result6 = UnityProjectCreator.Create(new UnityProjectCreationRequest(
                root,
                "Minimal6",
                "6000.5.2f1",
                editor6));
            Equal("2.0.26", result6.EditorMetadata.VisualStudioPackageVersion);
            Equal("1.2.5", result6.EditorMetadata.VisualStudioCodePackageVersion);
            AssertMinimalProject(result6.ProjectPath, "6000.5.2f1", "2.0.26", "1.2.5");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static Task TestProjectCreationPathSafetyAsync()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var editor = CreateEditorFixture(root, "editor", "2.0.26", null);
            var existing = Path.Combine(root, "Existing");
            Directory.CreateDirectory(existing);
            var sentinel = Path.Combine(existing, "keep.txt");
            File.WriteAllText(sentinel, "keep");

            Throws<IOException>(() => UnityProjectCreator.Create(new UnityProjectCreationRequest(
                root,
                "Existing",
                "6000.5.2f1",
                editor)));
            True(File.Exists(sentinel));
            Throws<InvalidDataException>(() => UnityProjectCreator.Create(new UnityProjectCreationRequest(
                root,
                "..\\escape",
                "6000.5.2f1",
                editor)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static string CreateEditorFixture(
        string root,
        string name,
        string visualStudioVersion,
        string? visualStudioCodeVersion)
    {
        var editorRoot = Path.Combine(root, name);
        var editorDirectory = Path.Combine(editorRoot, "Editor");
        var metadataDirectory = Path.Combine(
            editorDirectory,
            "Data",
            "Resources",
            "PackageManager",
            "Editor");
        Directory.CreateDirectory(metadataDirectory);
        File.Copy(Environment.ProcessPath!, Path.Combine(editorDirectory, "Unity.exe"));

        var vscodePackage = visualStudioCodeVersion is null
            ? new Dictionary<string, object> { ["deprecated"] = "Fixture" }
            : new Dictionary<string, object> { ["version"] = visualStudioCodeVersion };
        var manifest = new
        {
            schemaVersion = 4,
            packages = new Dictionary<string, object>
            {
                [UnityProjectCreator.VisualStudioPackageId] = new { version = visualStudioVersion },
                [UnityProjectCreator.VisualStudioCodePackageId] = vscodePackage
            }
        };
        File.WriteAllText(
            Path.Combine(metadataDirectory, "manifest.json"),
            JsonSerializer.Serialize(manifest));
        return editorRoot;
    }

    private static void AssertMinimalProject(
        string projectPath,
        string editorVersion,
        string visualStudioVersion,
        string visualStudioCodeVersion)
    {
        var directoryNames = Directory.GetDirectories(projectPath)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Equal("Assets,Packages,ProjectSettings", string.Join(',', directoryNames));
        Equal(2, Directory.GetFiles(projectPath, "*", SearchOption.AllDirectories).Length);

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(projectPath, "Packages", "manifest.json")));
        var dependencies = manifest.RootElement.GetProperty("dependencies");
        Equal(2, dependencies.EnumerateObject().Count());
        Equal(visualStudioVersion, dependencies.GetProperty(UnityProjectCreator.VisualStudioPackageId).GetString());
        Equal(visualStudioCodeVersion, dependencies.GetProperty(UnityProjectCreator.VisualStudioCodePackageId).GetString());

        var projectVersion = File.ReadAllText(Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt"));
        True(projectVersion.StartsWith($"m_EditorVersion: {editorVersion}", StringComparison.Ordinal));
    }

    private static async Task TestReleaseApiPaginationAsync()
    {
        var handler = new ReleasePageHandler(total: 60);
        using var httpClient = new HttpClient(handler);
        var releases = await new UnityReleaseCatalogClient(httpClient).GetReleasesAsync(60, 0, CancellationToken.None);
        Equal(60, releases.Count);
        Equal(3, handler.RequestCount);
        True(handler.Limits.All(limit => limit <= 25));
    }

    private static async Task TestIntegrityEncodingsAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "fixture.bin");
            var bytes = Encoding.UTF8.GetBytes("official-unity-package-fixture");
            await File.WriteAllBytesAsync(path, bytes);
            var rawMd5 = MD5.HashData(bytes);
            var hexMd5 = Encoding.ASCII.GetBytes(Convert.ToHexString(rawMd5).ToLowerInvariant());
            var sha384 = SHA384.HashData(bytes);

            await PackageIntegrityVerifier.VerifyAsync(path, CreatePlan("md5-" + Convert.ToBase64String(rawMd5)), CancellationToken.None);
            await PackageIntegrityVerifier.VerifyAsync(path, CreatePlan("md5-" + Convert.ToBase64String(hexMd5)), CancellationToken.None);
            await PackageIntegrityVerifier.VerifyAsync(path, CreatePlan("sha384-" + Convert.ToBase64String(sha384)), CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task TestDependencyPlannerAsync()
    {
        var child = new UnityModulePackage
        {
            Id = "android-ndk",
            Slug = "6000.5.2f1-windows-x86_64-android-ndk",
            Url = "https://dl.google.com/android.zip",
            Type = "ZIP"
        };
        var android = new UnityModulePackage
        {
            Id = "android",
            Slug = "6000.5.2f1-windows-x86_64-android",
            Url = "https://download.unity3d.com/android.exe",
            Type = "EXE",
            SubModules = [child]
        };
        var plan = DirectPackagePlanner.Build("6000.5.2f1", [android], ["android"]);
        Equal(2, plan.Packages.Count);
        Equal("android", plan.Packages[1].ParentId);

        var wrongVersion = new UnityModulePackage
        {
            Id = "webgl",
            Slug = "6000.3.0a3-windows-x86_64-webgl",
            Url = "https://download.unity3d.com/webgl.exe",
            Type = "EXE"
        };
        Throws<InvalidDataException>(() => DirectPackagePlanner.Build("6000.3.13f1", [wrongVersion], ["webgl"]));
        return Task.CompletedTask;
    }

    private static Task TestManifestPathTraversalAsync()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Equal(Path.Combine(root, "Editor", "Data"), SafePackageExtractor.ResolveManifestPath("{UNITY_PATH}/Editor/Data", root));
            Throws<InvalidDataException>(() => SafePackageExtractor.ResolveManifestPath("{UNITY_PATH}/../escape", root));
            Throws<InvalidDataException>(() => SafePackageExtractor.ResolveManifestPath("C:/escape", root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static async Task TestZipSecurityAsync()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var traversalZip = Path.Combine(root, "traversal.zip");
            CreateZip(traversalZip, [("../outside.txt", "blocked")]);
            await ThrowsAsync<InvalidDataException>(() => StageZipAsync(root, traversalZip, "traversal"));

            var excludedZip = Path.Combine(root, "excluded.zip");
            CreateZip(excludedZip, [(PackageSafetyPolicy.ExcludedFileName, "blocked")]);
            await ThrowsAsync<InvalidDataException>(() => StageZipAsync(root, excludedZip, "excluded"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TestRenameRulesAsync()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var editor = Path.Combine(root, "editor");
            Directory.CreateDirectory(editor);
            var ndkZip = Path.Combine(root, "ndk.zip");
            CreateZip(ndkZip, [("android-ndk-r27c/tool.txt", "ndk")]);
            var ndkStage = Path.Combine(root, "stage-ndk");
            var ndkPlan = CreateZipPlan(
                "android-ndk-r27c",
                "{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer/NDK",
                new UnityExtractedPathRename
                {
                    From = "{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer/NDK/android-ndk-r27c",
                    To = "{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer/NDK"
                });
            var extractor = new SafePackageExtractor();
            await extractor.StageAsync(new DownloadedPackage(ndkPlan, ndkZip, null, new FileInfo(ndkZip).Length), editor, ndkStage, CancellationToken.None);
            True(File.Exists(Path.Combine(ndkStage, "Editor", "Data", "PlaybackEngines", "AndroidPlayer", "NDK", "tool.txt")));

            var toolsZip = Path.Combine(root, "tools.zip");
            CreateZip(toolsZip, [("android-16/aapt.exe", "tool")]);
            var toolsStage = Path.Combine(root, "stage-tools");
            var toolsPlan = CreateZipPlan(
                "android-sdk-build-tools-36.0.0",
                "{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/build-tools",
                new UnityExtractedPathRename
                {
                    From = "{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/build-tools/android-16",
                    To = "{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/build-tools/36.0.0"
                });
            await extractor.StageAsync(new DownloadedPackage(toolsPlan, toolsZip, null, new FileInfo(toolsZip).Length), editor, toolsStage, CancellationToken.None);
            True(File.Exists(Path.Combine(toolsStage, "Editor", "Data", "PlaybackEngines", "AndroidPlayer", "SDK", "build-tools", "36.0.0", "aapt.exe")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TestDownloadResumeAsync()
    {
        var cache = CreateTemporaryDirectory();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("0123456789abcdef");
            var integrity = "sha384-" + Convert.ToBase64String(SHA384.HashData(bytes));
            var package = CreatePlan(integrity) with
            {
                Id = "resume",
                Slug = "6000.5.2f1-resume",
                DownloadSize = bytes.Length
            };
            var handler = new ResumeHandler(bytes);
            using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var downloader = new PackageDownloadService(httpClient, cache, maximumConcurrency: 1);
            await ThrowsAsync<OperationCanceledException>(() => downloader.DownloadAsync(package, null, CancellationToken.None));
            var result = await downloader.DownloadAsync(package, null, CancellationToken.None);
            True(handler.SawRangeRequest);
            True(handler.SawIfRange);
            var downloadedBytes = await File.ReadAllBytesAsync(result.FilePath);
            True(bytes.SequenceEqual(downloadedBytes));
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    private static async Task TestMutablePackageEtagAsync()
    {
        var cache = CreateTemporaryDirectory();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("mutable-language-pack");
            var package = CreatePlan(string.Empty) with
            {
                Id = "language-zh-hans",
                Slug = "6000.5.2f1-language-zh-hans",
                Type = "PO",
                Url = new Uri("https://new-translate.unity3d.jp/v1/live/54/6000.5/zh-hans"),
                DownloadSize = bytes.Length
            };
            var handler = new MutableEtagHandler(bytes);
            using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var downloader = new PackageDownloadService(httpClient, cache, maximumConcurrency: 1);
            var first = await downloader.DownloadAsync(package, null, CancellationToken.None);
            var second = await downloader.DownloadAsync(package, null, CancellationToken.None);
            Equal(first.FilePath, second.FilePath);
            True(handler.SawIfNoneMatch);
            Equal(2, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    private static async Task StageZipAsync(string root, string zipPath, string id)
    {
        var editor = Path.Combine(root, "editor-" + id);
        var staging = Path.Combine(root, "staging-" + id);
        Directory.CreateDirectory(editor);
        var plan = CreateZipPlan(id, "{UNITY_PATH}/Editor/Data", null);
        await new SafePackageExtractor().StageAsync(
            new DownloadedPackage(plan, zipPath, null, new FileInfo(zipPath).Length),
            editor,
            staging,
            CancellationToken.None);
    }

    private static DirectPackagePlan CreatePlan(string integrity) => new(
        "fixture",
        "6000.5.2f1-fixture",
        "Fixture",
        new Uri("https://download.unity3d.com/fixture.zip"),
        "ZIP",
        0,
        integrity,
        "{UNITY_PATH}/Editor/Data",
        null,
        false,
        null);

    private static DirectPackagePlan CreateZipPlan(
        string id,
        string destination,
        UnityExtractedPathRename? rename) => new(
        id,
        "6000.5.2f1-" + id,
        id,
        new Uri("https://download.unity3d.com/" + id + ".zip"),
        "ZIP",
        0,
        null,
        destination,
        rename,
        false,
        null);

    private static void CreateZip(string path, IReadOnlyList<(string Name, string Content)> entries)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "UnityCliUi.DirectTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static object GetNamedControl(object instance, string fieldName) =>
        instance.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(instance)
        ?? throw new InvalidOperationException($"Control '{fieldName}' was not found.");

    private static void SetNamedBooleanProperty(
        object instance,
        string fieldName,
        string propertyName,
        bool value)
    {
        var control = GetNamedControl(instance, fieldName);
        control.GetType().GetProperty(propertyName)?.SetValue(control, value);
    }

    private static bool GetNamedBooleanProperty(object instance, string fieldName, string propertyName)
    {
        var control = GetNamedControl(instance, fieldName);
        return control.GetType().GetProperty(propertyName)?.GetValue(control) as bool? == true;
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void True(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected condition to be true.");
        }
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class ResumeHandler(byte[] bytes) : HttpMessageHandler
    {
        private const string ETag = "\"fixture-v1\"";
        public bool SawRangeRequest { get; private set; }
        public bool SawIfRange { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var start = request.Headers.Range?.Ranges.Single().From;
            if (!start.HasValue)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new InterruptingStream(bytes, 5))
                };
                response.Headers.ETag = EntityTagHeaderValue.Parse(ETag);
                response.Content.Headers.ContentLength = bytes.Length;
                return Task.FromResult(response);
            }

            SawRangeRequest = true;
            SawIfRange = request.Headers.IfRange?.EntityTag?.Tag == ETag;
            var offset = checked((int)start.Value);
            var remaining = bytes[offset..];
            var partial = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(remaining)
            };
            partial.Headers.ETag = EntityTagHeaderValue.Parse(ETag);
            partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
            partial.Content.Headers.ContentLength = remaining.Length;
            return Task.FromResult(partial);
        }
    }

    private sealed class ReleasePageHandler(int total) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public List<int> Limits { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var query = request.RequestUri!.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(part => part[0], part => part[1], StringComparer.OrdinalIgnoreCase);
            var limit = int.Parse(query["limit"]);
            var offset = int.Parse(query["offset"]);
            Limits.Add(limit);
            var count = Math.Min(limit, total - offset);
            var results = Enumerable.Range(offset, count)
                .Select(index => new { version = $"6000.5.{index}f1" })
                .ToArray();
            var json = JsonSerializer.Serialize(new { offset, limit, total, results });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class MutableEtagHandler(byte[] bytes) : HttpMessageHandler
    {
        private const string ETag = "\"mutable-v1\"";
        public int RequestCount { get; private set; }
        public bool SawIfNoneMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 2)
            {
                SawIfNoneMatch = request.Headers.IfNoneMatch.Any(item => item.Tag == ETag);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
            response.Headers.ETag = EntityTagHeaderValue.Parse(ETag);
            response.Content.Headers.ContentLength = bytes.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class InterruptingStream(byte[] bytes, int firstReadLength) : MemoryStream(bytes)
    {
        private bool _hasRead;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_hasRead)
            {
                throw new OperationCanceledException("Synthetic interrupted download.");
            }
            _hasRead = true;
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, firstReadLength)], cancellationToken);
        }
    }
}
