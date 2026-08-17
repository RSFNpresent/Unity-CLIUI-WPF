using unity_cli_ui;

namespace UnityCliUi.DirectTests;

internal static class ProjectFolderDropTests
{
    public static Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnityCliUi.ProjectDropTests", Guid.NewGuid().ToString("N"));
        var invalidFolder = Path.Combine(root, "Invalid");
        var projectFolder = Path.Combine(root, "Project");
        var projectSettings = Path.Combine(projectFolder, "ProjectSettings");
        var filePath = Path.Combine(root, "file.txt");

        try
        {
            Directory.CreateDirectory(invalidFolder);
            Directory.CreateDirectory(projectSettings);
            File.WriteAllText(filePath, string.Empty);
            File.WriteAllText(Path.Combine(projectSettings, "ProjectVersion.txt"), "m_EditorVersion: 6000.0.28f1");

            Equal(projectFolder, MainWindow.GetDroppedUnityProjectPath([filePath, invalidFolder, projectFolder]));
            Equal(null, MainWindow.GetDroppedUnityProjectPath([filePath, invalidFolder]));
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Equal(string? expected, string? actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }
}
