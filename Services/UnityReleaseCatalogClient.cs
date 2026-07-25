using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using unity_cli_ui.Models;

namespace unity_cli_ui.Services;

public sealed class UnityReleaseCatalogClient
{
    private const string ReleasesEndpoint = "https://services.api.unity.com/unity/editor/release/v1/releases";
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public UnityReleaseCatalogClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<UnityReleaseInfo>> GetReleasesAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(offset, 0);
        var releases = new List<UnityReleaseInfo>(limit);
        while (releases.Count < limit)
        {
            var pageLimit = Math.Min(25, limit - releases.Count);
            var url = $"{ReleasesEndpoint}?limit={pageLimit}&offset={offset + releases.Count}&platform=WINDOWS&architecture=X86_64";
            var payload = await GetResponseAsync(url, cancellationToken);
            releases.AddRange(payload.Results);
            if (payload.Results.Count < pageLimit || offset + releases.Count >= payload.Total)
            {
                break;
            }
        }
        return releases;
    }

    public async Task<UnityReleaseInfo> GetReleaseAsync(string requestedVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            throw new ArgumentException("Unity version cannot be empty.", nameof(requestedVersion));
        }

        if (string.Equals(requestedVersion, "lts", StringComparison.OrdinalIgnoreCase))
        {
            var releases = await GetReleasesAsync(100, 0, cancellationToken);
            return releases
                .Where(release => release.GetWindowsX64Download() is not null)
                .Where(release => IsLtsVersion(release.Version))
                .OrderByDescending(release => release.ReleaseDate)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Unity Release API did not return an LTS editor.");
        }

        var encodedVersion = Uri.EscapeDataString(requestedVersion.Trim());
        var url = $"{ReleasesEndpoint}?limit=1&offset=0&platform=WINDOWS&architecture=X86_64&version={encodedVersion}";
        using var request = CreateRequest(url);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<UnityReleaseResponse>(
            stream,
            _jsonOptions,
            cancellationToken);
        var release = payload?.Results.FirstOrDefault(item =>
            string.Equals(item.Version, requestedVersion, StringComparison.OrdinalIgnoreCase));
        return release ?? throw new InvalidOperationException($"Unity {requestedVersion} was not found in the official Release API.");
    }

    public async Task<IReadOnlyList<UnityReleaseInfo>> SearchReleasesAsync(
        string versionPrefix,
        CancellationToken cancellationToken)
    {
        var encodedVersion = Uri.EscapeDataString(versionPrefix.Trim());
        var url = $"{ReleasesEndpoint}?limit=25&offset=0&platform=WINDOWS&architecture=X86_64&version={encodedVersion}";
        var payload = await GetResponseAsync(url, cancellationToken);
        return payload?.Results ?? [];
    }

    public static bool IsLtsVersion(string version) =>
        !version.Contains("a", StringComparison.OrdinalIgnoreCase) &&
        !version.Contains("b", StringComparison.OrdinalIgnoreCase) &&
        (version.StartsWith("2022.3.", StringComparison.OrdinalIgnoreCase) ||
         version.StartsWith("6000.0.", StringComparison.OrdinalIgnoreCase) ||
         version.StartsWith("6000.3.", StringComparison.OrdinalIgnoreCase));

    private static HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Unity-CLIUI/1.0.1");
        return request;
    }

    private async Task<UnityReleaseResponse> GetResponseAsync(string url, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(url);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<UnityReleaseResponse>(
                   stream,
                   _jsonOptions,
                   cancellationToken)
               ?? new UnityReleaseResponse();
    }
}
