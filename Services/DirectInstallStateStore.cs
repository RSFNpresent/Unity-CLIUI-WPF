using System.IO;
using System.Text.Json;

namespace unity_cli_ui.Services;

public sealed class DirectInstallStateStore
{
    private readonly string _stateDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public DirectInstallStateStore(string stateDirectory)
    {
        _stateDirectory = stateDirectory;
    }

    public async Task<DirectEditorState?> LoadEditorAsync(string version, CancellationToken cancellationToken)
    {
        var path = GetEditorStatePath(version);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<DirectEditorState>(stream, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    public Task SaveEditorAsync(DirectEditorState state, CancellationToken cancellationToken) =>
        WriteAtomicallyAsync(GetEditorStatePath(state.Version), state, cancellationToken);

    public void RemoveEditor(string version)
    {
        var path = GetEditorStatePath(version);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public Task SaveTransactionAsync(DirectInstallTransaction transaction, CancellationToken cancellationToken) =>
        WriteAtomicallyAsync(
            Path.Combine(GetTransactionDirectory(), SanitizeName(transaction.Id) + ".json"),
            transaction,
            cancellationToken);

    private async Task WriteAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken);
        }
        File.Move(temporaryPath, path, overwrite: true);
    }

    private string GetEditorStatePath(string version) => Path.Combine(
        GetEditorDirectory(),
        SanitizeName(version) + ".json");

    private string GetEditorDirectory() => Path.Combine(_stateDirectory, "editors");
    private string GetTransactionDirectory() => Path.Combine(_stateDirectory, "transactions");

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? Guid.NewGuid().ToString("N") : sanitized;
    }
}
