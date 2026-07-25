# Unity CLIUI

[English](README.md) | [开发计划](TODO.md)

Unity CLIUI 是一个用于 Windows 的第三方 WPF Unity 管理器。它使用 [Unity 官方 CLI](https://docs.unity.com/zh-cn/unity-cli/unity-cli) 管理编辑器版本和模块，并集中管理本地 Unity 工程。

界面参考 Windows 10 设置，支持简体中文和英文。

## 下载

从 [Releases](https://github.com/RSFNpresent/Unity-CLIUI-WPF/releases) 下载 Windows x64 压缩包，解压后运行 `Unity-CLIUI.exe`。

发布包不包含 Unity CLI。可以在“设置”中选择已有 CLI、打开 Unity 官方下载页面，或运行官方安装脚本。

## 1.0 版本

- 安装、扫描、更新和启动 Unity 编辑器。
- 查看和管理编辑器模块。
- 添加、扫描、排序和启动 Unity 工程，并保存各工程的启动参数。
- 管理 Unity Pipeline 和 Unity AI Assistant 包。
- 为支持的 AI 客户端配置 Unity MCP。
- 在本地缓存编辑器和工程信息。

## 构建

从源码构建需要 Windows 和 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```powershell
git clone https://github.com/RSFNpresent/Unity-CLIUI-WPF.git
cd Unity-CLIUI-WPF
dotnet build unity-cli-ui.csproj
```

Unity CLI 可执行文件已被 Git 忽略，也不会包含在发布包中。

## 贡献者

- [RSFNpresent](https://github.com/RSFNpresent) - 项目设计与开发
- [OpenAI](https://openai.com/) - GPT 与 Codex 协助开发

感谢 GPT 在开发过程中帮助纠错、提供指导并参与实现。

## 第三方声明

Unity CLIUI 是独立的第三方项目，与 Unity Technologies 没有隶属或背书关系。Unity 和 Unity Logo 是 Unity Technologies 或其关联公司的商标。
