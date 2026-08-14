# Unity CLIUI

[简体中文](README.zh-CN.md) | [Roadmap](TODO.md)

Unity CLIUI is a small third-party WPF manager for Unity on Windows. It can manage editor versions and modules directly through Unity's official Release API and CDN without installing Unity CLI, and keeps local Unity projects in one place. The official [Unity CLI](https://docs.unity.com/en-us/unity-cli/unity-cli) remains available as an optional backend.

The interface follows the Windows 10 Settings style and is available in English and Simplified Chinese.

## Download

Download the Windows x64 package from [Releases](https://github.com/RSFNpresent/Unity-CLIUI-WPF/releases), extract it, and run `Unity-CLIUI.exe`.

- `framework-dependent` is the smaller package. It requires the [.NET 10 Desktop Runtime x64](https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe).
- `self-contained` includes the runtime and needs no separate .NET installation.

Unity CLI is not included. Select **Direct (no CLI)** in **Settings** to enable official package downloads and installation without the CLI. **Auto** only detects an existing CLI and never switches to direct downloads implicitly.

## Version 1.0.5

- Resolve the installed x64 Desktop Runtime from the Windows global installation and use the official x64 installer link.
- Match full Unity versions, including regional suffixes such as `f1c1`, when opening projects.
- Build the runtime-dependent launcher with a stable .NET 10 SDK and a working Desktop Runtime redirect.
- Install, scan, update, uninstall, and launch Unity editors with or without Unity CLI.
- Discover editor packages and modules from Unity's official Release API.
- Resume up to three package downloads in parallel, verify official integrity metadata, and stage ZIP extraction before committing files.
- Apply Unity package `destination` and `extractedPathRename` rules with path traversal protection.
- View and install editor modules and their required nested dependencies.
- Read installed modules locally in Direct mode and fall back to the installed-only view when Unity services are unavailable.
- Use Win8/Win10-inspired transitions that follow Windows animation settings, with an About panel showing the version and repository link.
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

One command always creates both Windows x64 packages:

```powershell
.\scripts\Publish-Packages.ps1
```

The script publishes `framework-dependent` and `self-contained` together. Each ZIP contains only `Unity-CLIUI.exe`.

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
