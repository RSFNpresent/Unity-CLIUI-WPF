using System.Text.Json;
using unity_cli_ui.Models;

namespace UnityCliUi.DirectTests;

internal static class UnityReleaseModelTests
{
    public static Task RunAsync()
    {
        const string json = """
        {
          "offset": 0,
          "limit": 1,
          "total": 1,
          "results": [{
            "version": "6000.5.2f1",
            "downloads": [{
              "platform": "WINDOWS",
              "architecture": "X86_64",
              "downloadSize": { "value": "4141852744", "unit": "BYTE" },
              "installedSize": { "value": 8192.0, "unit": "BYTE" },
              "modules": [{
                "id": "webgl",
                "downloadSize": { "value": 2048.0, "unit": "BYTE" },
                "installedSize": { "value": "4096", "unit": "BYTE" }
              }]
            }]
          }]
        }
        """;

        var response = JsonSerializer.Deserialize<UnityReleaseResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Response did not parse.");
        var download = response.Results.Single().GetWindowsX64Download()
            ?? throw new InvalidOperationException("Windows x64 download was not found.");
        Equal(4_141_852_744, download.DownloadSize.Value);
        Equal(8_192, download.InstalledSize.Value);
        var module = download.Modules.Single();
        Equal(2_048, module.DownloadSize.Value);
        Equal(4_096, module.InstalledSize.Value);
        return Task.CompletedTask;
    }

    private static void Equal(long expected, long actual)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }
}
