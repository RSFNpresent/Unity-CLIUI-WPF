using unity_cli_ui.Services;

namespace UnityCliUi.DirectTests;

internal static class UnityVersionPolicyTests
{
    public static Task RunAsync()
    {
        Equal("6000.0.28f1c1", UnityVersionPolicy.Normalize("6000.0.28f1c1 (7d3b9a4c8f20)"));
        True(UnityVersionPolicy.Matches("6000.0.28f1c1", " 6000.0.28F1C1 "));
        False(UnityVersionPolicy.Matches("6000.0.28f1", "6000.0.28f1c1"));

        var root = Path.Combine(Path.GetTempPath(), "UnityCliUi.VersionTests", Guid.NewGuid().ToString("N"));
        var editorRoot = Path.Combine(root, "6000.0.28f1c1");
        var editorDirectory = Path.Combine(editorRoot, "Editor");
        try
        {
            Directory.CreateDirectory(editorDirectory);
            File.Copy(Environment.ProcessPath!, Path.Combine(editorDirectory, "Unity.exe"));
            var editor = InstalledEditorScanner.Scan(root, CancellationToken.None).Single();
            Equal("6000.0.28f1c1", editor.Version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
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

    private static void False(bool condition) => True(!condition);
}
