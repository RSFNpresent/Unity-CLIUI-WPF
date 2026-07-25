using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using unity_cli_ui.Models;

namespace unity_cli_ui.Services;

public static partial class PackageIntegrityVerifier
{
    public static bool HasVerifiableIntegrity(DirectPackagePlan package) =>
        !string.IsNullOrWhiteSpace(package.Integrity) ||
        Sha256FileNamePattern().IsMatch(package.Url.AbsolutePath);

    public static async Task VerifyAsync(
        string filePath,
        DirectPackagePlan package,
        CancellationToken cancellationToken)
    {
        PackageSafetyPolicy.RejectExcludedFile(Path.GetFileName(filePath));
        var integrity = package.Integrity?.Trim();
        if (string.IsNullOrWhiteSpace(integrity))
        {
            var match = Sha256FileNamePattern().Match(package.Url.AbsolutePath);
            if (!match.Success)
            {
                return;
            }

            var expectedSha256 = Convert.FromHexString(match.Groups["hash"].Value);
            var actualSha256 = await ComputeHashAsync(filePath, HashAlgorithmName.SHA256, cancellationToken);
            EnsureMatches(package.Id, "SHA-256 filename", expectedSha256, actualSha256);
            return;
        }

        var separator = integrity.IndexOf('-');
        if (separator <= 0 || separator == integrity.Length - 1)
        {
            throw new InvalidDataException($"Package {package.Id} has an invalid integrity value.");
        }

        var algorithmLabel = integrity[..separator].ToLowerInvariant();
        var encodedDigest = integrity[(separator + 1)..];
        byte[] expectedDigest;
        try
        {
            expectedDigest = Convert.FromBase64String(encodedDigest);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"Package {package.Id} has invalid base64 integrity data.", exception);
        }

        var algorithm = algorithmLabel switch
        {
            "md5" => HashAlgorithmName.MD5,
            "sha256" => HashAlgorithmName.SHA256,
            "sha384" => HashAlgorithmName.SHA384,
            "sha512" => HashAlgorithmName.SHA512,
            _ => throw new InvalidDataException($"Package {package.Id} uses unsupported integrity algorithm {algorithmLabel}.")
        };

        if (algorithm == HashAlgorithmName.MD5 && expectedDigest.Length == 32)
        {
            var text = Encoding.ASCII.GetString(expectedDigest);
            if (text.All(Uri.IsHexDigit))
            {
                expectedDigest = Convert.FromHexString(text);
            }
        }

        var actualDigest = await ComputeHashAsync(filePath, algorithm, cancellationToken);
        EnsureMatches(package.Id, algorithmLabel, expectedDigest, actualDigest);
    }

    private static async Task<byte[]> ComputeHashAsync(
        string filePath,
        HashAlgorithmName algorithm,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(algorithm);
        var buffer = new byte[1024 * 1024];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
        }
        return hash.GetHashAndReset();
    }

    private static void EnsureMatches(string packageId, string algorithm, byte[] expected, byte[] actual)
    {
        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            throw new InvalidDataException($"Integrity verification failed for {packageId} ({algorithm}).");
        }
    }

    [GeneratedRegex(@"_(?<hash>[0-9a-fA-F]{64})\.zip$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256FileNamePattern();
}
