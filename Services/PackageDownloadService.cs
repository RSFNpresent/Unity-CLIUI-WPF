using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using unity_cli_ui.Models;

namespace unity_cli_ui.Services;

public sealed record PackageDownloadProgress(string PackageId, long BytesReceived, long? TotalBytes);

public sealed record DownloadedPackage(DirectPackagePlan Package, string FilePath, string? ETag, long Length);

internal sealed class PackageCacheMetadata
{
    public string Url { get; init; } = string.Empty;
    public string? ETag { get; init; }
    public long? ContentLength { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class PackageDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _downloadGate;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public PackageDownloadService(HttpClient httpClient, string cacheDirectory, int maximumConcurrency = 3)
    {
        _httpClient = httpClient;
        _cacheDirectory = cacheDirectory;
        _downloadGate = new SemaphoreSlim(Math.Clamp(maximumConcurrency, 1, 8));
    }

    public async Task<DownloadedPackage> DownloadAsync(
        DirectPackagePlan package,
        Action<PackageDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        PackageSafetyPolicy.ValidateManifestPackage(package);
        await _downloadGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var finalPath = GetCacheFilePath(package);
            var partialPath = finalPath + ".partial";
            var metadataPath = finalPath + ".metadata.json";

            if (File.Exists(finalPath))
            {
                try
                {
                    await PackageIntegrityVerifier.VerifyAsync(finalPath, package, cancellationToken);
                    var cachedMetadata = await ReadMetadataAsync(metadataPath, cancellationToken);
                    var cachedLength = new FileInfo(finalPath).Length;
                    if (!PackageIntegrityVerifier.HasVerifiableIntegrity(package))
                    {
                        return await RefreshMutablePackageAsync(
                            package,
                            finalPath,
                            partialPath,
                            metadataPath,
                            cachedMetadata,
                            progress,
                            cancellationToken);
                    }
                    progress?.Invoke(new PackageDownloadProgress(package.Id, cachedLength, cachedLength));
                    return new DownloadedPackage(package, finalPath, cachedMetadata?.ETag, cachedLength);
                }
                catch (InvalidDataException)
                {
                    File.Delete(finalPath);
                }
            }

            var metadata = await ReadMetadataAsync(metadataPath, cancellationToken);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
                using var request = new HttpRequestMessage(HttpMethod.Get, package.Url);
                request.Headers.UserAgent.ParseAdd("Unity-CLIUI/1.0.1");
                if (existingLength > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(existingLength, null);
                    if (EntityTagHeaderValue.TryParse(metadata?.ETag, out var entityTag))
                    {
                        request.Headers.IfRange = new RangeConditionHeaderValue(entityTag);
                    }
                }

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    File.Delete(partialPath);
                    metadata = null;
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                if (!append)
                {
                    existingLength = 0;
                }

                var responseLength = response.Content.Headers.ContentLength;
                var totalLength = response.Content.Headers.ContentRange?.Length ??
                                  (responseLength.HasValue ? existingLength + responseLength.Value : null);
                var etag = response.Headers.ETag?.ToString();
                metadata = new PackageCacheMetadata
                {
                    Url = package.Url.AbsoluteUri,
                    ETag = etag,
                    ContentLength = totalLength,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                await WriteMetadataAsync(metadataPath, metadata, cancellationToken);

                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var destination = new FileStream(
                    partialPath,
                    append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    1024 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[1024 * 1024];
                    var received = existingLength;
                    int bytesRead;
                    while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        received += bytesRead;
                        progress?.Invoke(new PackageDownloadProgress(package.Id, received, totalLength));
                    }
                    await destination.FlushAsync(cancellationToken);
                }

                var downloadedLength = new FileInfo(partialPath).Length;
                if (totalLength.HasValue && downloadedLength != totalLength.Value)
                {
                    throw new EndOfStreamException($"Package {package.Id} ended at {downloadedLength} of {totalLength.Value} bytes.");
                }
                if (downloadedLength == 0)
                {
                    throw new InvalidDataException($"Package {package.Id} is empty.");
                }

                await PackageIntegrityVerifier.VerifyAsync(partialPath, package, cancellationToken);
                File.Move(partialPath, finalPath, overwrite: true);
                return new DownloadedPackage(package, finalPath, etag, downloadedLength);
            }

            throw new HttpRequestException($"Unable to resume package {package.Id}; the server rejected the saved range.");
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    private string GetCacheFilePath(DirectPackagePlan package)
    {
        var urlHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(package.Url.AbsoluteUri)))[..16];
        var extension = package.Type switch
        {
            "EXE" => ".exe",
            "ZIP" => ".zip",
            "PO" => ".po",
            _ => ".package"
        };
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safeSlug = new string(package.Slug
            .Take(100)
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeSlug))
        {
            safeSlug = package.Id;
        }
        PackageSafetyPolicy.RejectExcludedFile(safeSlug);
        return Path.Combine(_cacheDirectory, $"{safeSlug}-{urlHash}{extension}");
    }

    private async Task<DownloadedPackage> RefreshMutablePackageAsync(
        DirectPackagePlan package,
        string finalPath,
        string partialPath,
        string metadataPath,
        PackageCacheMetadata? cachedMetadata,
        Action<PackageDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, package.Url);
        request.Headers.UserAgent.ParseAdd("Unity-CLIUI/1.0.1");
        if (EntityTagHeaderValue.TryParse(cachedMetadata?.ETag, out var entityTag))
        {
            request.Headers.IfNoneMatch.Add(entityTag);
        }
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            var cachedLength = new FileInfo(finalPath).Length;
            progress?.Invoke(new PackageDownloadProgress(package.Id, cachedLength, cachedLength));
            return new DownloadedPackage(package, finalPath, cachedMetadata?.ETag, cachedLength);
        }

        response.EnsureSuccessStatusCode();
        var totalLength = response.Content.Headers.ContentLength;
        var etag = response.Headers.ETag?.ToString();
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = new FileStream(
            partialPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[1024 * 1024];
            long received = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                received += bytesRead;
                progress?.Invoke(new PackageDownloadProgress(package.Id, received, totalLength));
            }
            await destination.FlushAsync(cancellationToken);
        }

        var length = new FileInfo(partialPath).Length;
        if (length == 0 || (totalLength.HasValue && length != totalLength.Value))
        {
            throw new EndOfStreamException($"Mutable package {package.Id} was incomplete.");
        }
        var metadata = new PackageCacheMetadata
        {
            Url = package.Url.AbsoluteUri,
            ETag = etag,
            ContentLength = length,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await WriteMetadataAsync(metadataPath, metadata, cancellationToken);
        File.Move(partialPath, finalPath, overwrite: true);
        return new DownloadedPackage(package, finalPath, etag, length);
    }

    private async Task<PackageCacheMetadata?> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<PackageCacheMetadata>(stream, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    private async Task WriteMetadataAsync(
        string path,
        PackageCacheMetadata metadata,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, metadata, _jsonOptions, cancellationToken);
        }
        File.Move(temporaryPath, path, overwrite: true);
    }
}
