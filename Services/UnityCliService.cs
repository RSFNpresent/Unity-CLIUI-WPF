using System.Diagnostics;
using System.IO;
using System.Text;

namespace unity_cli_ui.Services;

public sealed class UnityCliService
{
    private readonly object _processLock = new();
    private Process? _activeProcess;

    public string ExecutablePath { get; set; } = string.Empty;

    public async Task<CliResult> RunAsync(IReadOnlyList<string> arguments, Action<string> onOutput)
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath) || !File.Exists(ExecutablePath))
        {
            throw new FileNotFoundException("Unity CLI executable was not found.", ExecutablePath);
        }

        return await RunProcessAsync(ExecutablePath, arguments, onOutput);
    }

    public async Task<CliResult> RunProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string> onOutput,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path cannot be empty.", nameof(executablePath));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (environmentVariables is not null)
        {
            foreach (var (name, value) in environmentVariables)
            {
                startInfo.Environment[name] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
            lock (_processLock)
            {
                if (_activeProcess is not null)
                {
                    process.Kill(entireProcessTree: true);
                    throw new InvalidOperationException("A Unity CLI process is already running.");
                }
                _activeProcess = process;
            }

            var stdoutTask = ReadStreamAsync(process.StandardOutput, onOutput, prefix: string.Empty);
            var stderrTask = ReadStreamAsync(process.StandardError, onOutput, prefix: "[stderr] ");

            await Task.WhenAll(process.WaitForExitAsync(), stdoutTask, stderrTask);

            return new CliResult(
                process.ExitCode,
                await stdoutTask,
                await stderrTask);
        }
        finally
        {
            lock (_processLock)
            {
                _activeProcess = null;
            }
        }
    }

    public void Cancel()
    {
        lock (_processLock)
        {
            if (_activeProcess is { HasExited: false })
            {
                _activeProcess.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task<string> ReadStreamAsync(StreamReader reader, Action<string> onOutput, string prefix)
    {
        var buffer = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            buffer.AppendLine(line);
            onOutput(prefix + line);
        }
        return buffer.ToString();
    }
}

public sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);
