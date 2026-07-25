# Unity CLIUI

[简体中文](README.zh-CN.md) | [Roadmap](TODO.md)

Unity CLIUI is a small third-party WPF manager for Unity on Windows. It uses the official [Unity CLI](https://docs.unity.com/en-us/unity-cli/unity-cli) to manage editor versions and modules, and keeps local Unity projects in one place.

The interface follows the Windows 10 Settings style and is available in English and Simplified Chinese.

## Download

Download the Windows x64 package from [Releases](https://github.com/RSFNpresent/Unity-CLIUI-WPF/releases), extract it, and run `Unity-CLIUI.exe`.

Unity CLI is not included. Select an existing CLI executable from **Settings**, open Unity's official download page, or run the official installer from the app.

## Version 1.0

- Install, scan, update, and launch Unity editors.
- View and manage editor modules.
- Add, scan, sort, and launch Unity projects with per-project arguments.
- Manage the Unity Pipeline and Unity AI Assistant packages.
- Configure Unity MCP for supported AI clients.
- Cache editor and project information locally.

## Build

Building requires Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/RSFNpresent/Unity-CLIUI-WPF.git
cd Unity-CLIUI-WPF
dotnet build unity-cli-ui.csproj
```

The Unity CLI executable is ignored by Git and is not included in release packages.

## Contributors

- [RSFNpresent](https://github.com/RSFNpresent) - project design and development
- [OpenAI](https://openai.com/) - GPT and Codex assistance

Thanks to GPT for catching mistakes, offering guidance, and helping with development.

## Disclaimer

Unity CLIUI is an independent third-party project. It is not affiliated with or endorsed by Unity Technologies. Unity and the Unity logo are trademarks of Unity Technologies or its affiliates.
