# Unity CLIUI

[简体中文](README.zh-CN.md) | [Roadmap](TODO.md)

Unity CLIUI is a small third-party WPF manager for Unity on Windows. It can manage editor versions and modules directly through Unity's official Release API and CDN without installing Unity CLI, and keeps local Unity projects in one place. The official [Unity CLI](https://docs.unity.com/en-us/unity-cli/unity-cli) remains available as an optional backend.

The interface follows the Windows 10 Settings style and is available in English and Simplified Chinese.

## Download

Download the Windows x64 package from [Releases](https://github.com/RSFNpresent/Unity-CLIUI-WPF/releases), extract it, and run `Unity-CLIUI.exe`.

Unity CLI is not included. Select **Direct (no CLI)** in **Settings** to enable official package downloads and installation without the CLI. **Auto** only detects an existing CLI and never switches to direct downloads implicitly.

## Version 1.0.1

- Install, scan, update, uninstall, and launch Unity editors with or without Unity CLI.
- Discover editor packages and modules from Unity's official Release API.
- Resume up to three package downloads in parallel, verify official integrity metadata, and stage ZIP extraction before committing files.
- Apply Unity package `destination` and `extractedPathRename` rules with path traversal protection.
- View and install editor modules and their required nested dependencies.
- Create minimal Unity projects for an installed editor with compatible Visual Studio and VS Code package versions read from its official metadata.
- Add, scan, sort, and launch Unity projects with per-project arguments.
- Manage the Unity Pipeline and Unity AI Assistant packages.
- Configure Unity MCP for supported AI clients.
- Cache editor, available module, and project information locally.

## Build

Building requires Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/RSFNpresent/Unity-CLIUI-WPF.git
cd Unity-CLIUI-WPF
dotnet build unity-cli-ui.csproj
```

## Publish

Use the checked-in publish profiles to create the Windows x64 packages:

```powershell
dotnet publish unity-cli-ui.csproj -p:PublishProfile=win-x64-self-contained
dotnet publish unity-cli-ui.csproj -p:PublishProfile=win-x64-framework-dependent
```

Each profile produces only `Unity-CLIUI.exe`. Publishing fails if any additional file is present. The Unity CLI executable is ignored by Git and is never included.

Run the direct-backend regression suite without external test packages:

```powershell
dotnet run --project Tests\UnityCliUi.DirectTests\UnityCliUi.DirectTests.csproj -c Release
```

## Contributors

- [RSFNpresent](https://github.com/RSFNpresent) - project design and development
- [OpenAI](https://openai.com/) - GPT and Codex assistance

Thanks to GPT for catching mistakes, offering guidance, and helping with development.

## Disclaimer

Unity CLIUI is an independent third-party project. It is not affiliated with or endorsed by Unity Technologies. Unity and the Unity logo are trademarks of Unity Technologies or its affiliates.
