using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using unity_cli_ui.Models;

namespace unity_cli_ui.Services;

public sealed record StagedPackage(DirectPackagePlan Package, string StagingRoot, string DestinationPath);

public sealed class SafePackageExtractor
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DestinationLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<StagedPackage> StageAsync(
        DownloadedPackage downloadedPackage,
        string editorRoot,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var package = downloadedPackage.Package;
        if (string.IsNullOrWhiteSpace(package.Destination))
        {
            throw new InvalidDataException($"Package {package.Id} does not declare a destination.");
        }

        editorRoot = Path.GetFullPath(editorRoot);
        stagingRoot = Path.GetFullPath(stagingRoot);
        Directory.CreateDirectory(stagingRoot);
        var destination = ResolveManifestPath(package.Destination, editorRoot);
        var relativeDestination = GetSafeRelativePath(editorRoot, destination);
        var stagedDestination = ResolveUnderRoot(stagingRoot, relativeDestination);
        Directory.CreateDirectory(stagedDestination);

        if (package.Type == "ZIP")
        {
            await ExtractZipAsync(downloadedPackage.FilePath, stagedDestination, cancellationToken);
        }
        else if (package.Type == "PO")
        {
            var language = package.Id.StartsWith("language-", StringComparison.OrdinalIgnoreCase)
                ? package.Id["language-".Length..]
                : package.Id;
            var fileName = SanitizeLeafName(language) + ".po";
            PackageSafetyPolicy.RejectExcludedFile(fileName);
            var target = ResolveUnderRoot(stagedDestination, fileName);
            File.Copy(downloadedPackage.FilePath, target, overwrite: true);
        }
        else
        {
            throw new InvalidDataException($"Package {package.Id} cannot be staged as {package.Type}.");
        }

        if (package.ExtractedPathRename is { } rename)
        {
            ApplyRename(rename, editorRoot, stagingRoot);
        }

        return new StagedPackage(package, stagingRoot, destination);
    }

    public async Task CommitAsync(
        StagedPackage stagedPackage,
        string editorRoot,
        CancellationToken cancellationToken)
    {
        editorRoot = Path.GetFullPath(editorRoot);
        var lockKey = Path.GetFullPath(stagedPackage.DestinationPath);
        var destinationLock = DestinationLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await destinationLock.WaitAsync(cancellationToken);
        try
        {
            var files = Directory.EnumerateFiles(stagedPackage.StagingRoot, "*", SearchOption.AllDirectories).ToArray();
            foreach (var source in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PackageSafetyPolicy.RejectExcludedFile(Path.GetFileName(source));
                var relative = GetSafeRelativePath(stagedPackage.StagingRoot, source);
                var target = ResolveUnderRoot(editorRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(source, target, overwrite: true);
            }
        }
        finally
        {
            destinationLock.Release();
        }
    }

    public static string ResolveManifestPath(string manifestPath, string editorRoot)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new InvalidDataException("Manifest path cannot be empty.");
        }

        editorRoot = Path.GetFullPath(editorRoot);
        var normalized = manifestPath.Replace('/', Path.DirectorySeparatorChar);
        string candidate;
        if (normalized.Equals("{UNITY_PATH}", StringComparison.OrdinalIgnoreCase))
        {
            candidate = editorRoot;
        }
        else if (normalized.StartsWith("{UNITY_PATH}" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            candidate = Path.Combine(editorRoot, normalized[("{UNITY_PATH}".Length + 1)..]);
        }
        else
        {
            throw new InvalidDataException($"Manifest path is not rooted at {{UNITY_PATH}}: {manifestPath}");
        }

        var resolved = Path.GetFullPath(candidate);
        EnsureUnderRoot(editorRoot, resolved);
        return resolved;
    }

    private static async Task ExtractZipAsync(
        string archivePath,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(entryName))
            {
                continue;
            }
            if (entryName.Split(Path.DirectorySeparatorChar).Any(segment => segment.Contains(':')))
            {
                throw new InvalidDataException($"ZIP entry uses an unsafe path: {entry.FullName}");
            }
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            {
                throw new InvalidDataException($"ZIP symbolic links are not allowed: {entry.FullName}");
            }

            PackageSafetyPolicy.RejectExcludedFile(Path.GetFileName(entryName));
            var target = ResolveUnderRoot(destination, entryName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var output = new FileStream(
                target,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(output, cancellationToken);
        }
    }

    private static void ApplyRename(
        UnityExtractedPathRename rename,
        string editorRoot,
        string stagingRoot)
    {
        var fromResolved = ResolveManifestPath(rename.From, editorRoot);
        var toResolved = ResolveManifestPath(rename.To, editorRoot);
        var from = ResolveUnderRoot(stagingRoot, GetSafeRelativePath(editorRoot, fromResolved));
        var to = ResolveUnderRoot(stagingRoot, GetSafeRelativePath(editorRoot, toResolved));
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!Directory.Exists(from) && !File.Exists(from))
        {
            throw new InvalidDataException($"Package rename source was not extracted: {rename.From}");
        }

        if (Directory.Exists(from))
        {
            if (IsUnderRoot(to, from))
            {
                MoveDirectoryContents(from, to);
                if (Directory.Exists(from))
                {
                    Directory.Delete(from, recursive: true);
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (Directory.Exists(to))
                {
                    MoveDirectoryContents(from, to);
                    Directory.Delete(from, recursive: true);
                }
                else
                {
                    Directory.Move(from, to);
                }
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(from, to, overwrite: true);
        }
    }

    private static void MoveDirectoryContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source).ToArray())
        {
            var target = ResolveUnderRoot(destination, Path.GetFileName(directory));
            if (Directory.Exists(target))
            {
                MoveDirectoryContents(directory, target);
                Directory.Delete(directory, recursive: true);
            }
            else
            {
                Directory.Move(directory, target);
            }
        }
        foreach (var file in Directory.EnumerateFiles(source).ToArray())
        {
            PackageSafetyPolicy.RejectExcludedFile(Path.GetFileName(file));
            File.Move(file, ResolveUnderRoot(destination, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static string SanitizeLeafName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var value = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        if (value is "" or "." or "..")
        {
            throw new InvalidDataException("Package produced an invalid filename.");
        }
        return value;
    }

    private static string GetSafeRelativePath(string root, string path)
    {
        root = Path.GetFullPath(root);
        path = Path.GetFullPath(path);
        EnsureUnderRoot(root, path);
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".")
        {
            return string.Empty;
        }
        if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Path escapes its allowed root: {path}");
        }
        return relative;
    }

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        root = Path.GetFullPath(root);
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
        EnsureUnderRoot(root, resolved);
        return resolved;
    }

    private static void EnsureUnderRoot(string root, string path)
    {
        if (!IsUnderRoot(root, path))
        {
            throw new InvalidDataException($"Path escapes its allowed root: {path}");
        }
    }

    private static bool IsUnderRoot(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
