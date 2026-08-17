# Unity CLIUI

[English](README.md) | [开发计划](TODO.md)

Unity CLIUI 是一个用于 Windows 的第三方 WPF Unity 管理器。无需安装 Unity CLI，即可通过 Unity 官方 Release API 和 CDN 管理编辑器版本与模块，并集中管理本地 Unity 工程。[Unity 官方 CLI](https://docs.unity.com/zh-cn/unity-cli/unity-cli) 仍可作为可选后端使用。

界面参考 Windows 10 设置，支持简体中文和英文。

## 下载

从 [Releases](https://github.com/RSFNpresent/Unity-CLIUI-WPF/releases) 下载 Windows x64 压缩包，解压后运行 `Unity-CLIUI.exe`。

- `framework-dependent` 是普通版，体积较小。它需要 [.NET 10 Desktop Runtime x64](https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe)。
- `self-contained` 是自包含版，无需单独安装 .NET Runtime。

发布包不包含 Unity CLI。在“设置”中明确选择“直连（无 CLI）”后，才会启用官方软件包的下载与安装。“自动”模式只检测已有 CLI，不会隐式切换到直连下载。

## 1.0.6 版本

- 最大化窗口使用当前显示器工作区，并兼容第三方任务栏与自动隐藏任务栏。
- 将 Unity 工程文件夹拖放到工程列表即可添加工程。
- 打开工程时匹配完整 Unity 版本，包括 `f1c1` 等区域发行后缀。
- 使用稳定版 .NET 10 SDK 构建普通版，并跳转到正确的 Desktop Runtime 下载页。
- 在有无 Unity CLI 的环境中安装、扫描、更新、卸载和启动 Unity 编辑器。
- 从 Unity 官方 Release API 获取编辑器与模块清单。
- 最多并行断点下载三个包，校验官方完整性信息，并在独立 staging 中安全解压后提交。
- 按 Unity 清单应用 `destination` 和 `extractedPathRename`，同时防止路径穿越。
- 查看并安装编辑器模块及其必需的嵌套依赖。
- Direct 模式在本地读取已安装模块；Unity 服务不可用时回退为仅显示已安装模块。
- 为导航、亚克力状态和窗口生命周期提供跟随 Windows 设置的 Win8/Win10 风格动画，并在“关于”区域显示版本和仓库链接。
- 为所选已安装编辑器创建最小 Unity 工程，并从编辑器官方元数据写入兼容的 Visual Studio 与 VS Code 包版本。
- 添加、扫描、排序和启动 Unity 工程，并保存各工程的启动参数。
- 管理 Unity Pipeline 和 Unity AI Assistant 包。
- 为支持的 AI 客户端配置 Unity MCP。
- 在本地缓存编辑器、可用模块和工程信息。

## 构建

从源码构建需要 Windows 和 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```powershell
git clone https://github.com/RSFNpresent/Unity-CLIUI-WPF.git
cd Unity-CLIUI-WPF
dotnet build unity-cli-ui.csproj
```

## 发布

一个命令固定生成两种 Windows x64 发布包：

```powershell
.\scripts\Publish-Packages.ps1
```

脚本同时生成普通版和自包含版。每个 ZIP 只包含 `Unity-CLIUI.exe`。

无需外部测试包即可运行直连后端回归测试：

```powershell
dotnet run --project Tests\UnityCliUi.DirectTests\UnityCliUi.DirectTests.csproj -c Release
```

## 贡献者

- [RSFNpresent](https://github.com/RSFNpresent) - 项目设计与开发
- [OpenAI](https://openai.com/) - GPT 与 Codex 协助开发

感谢 GPT 在开发过程中帮助纠错、提供指导并参与实现。

## 第三方声明

Unity CLIUI 是独立的第三方项目，与 Unity Technologies 没有隶属或背书关系。Unity 和 Unity Logo 是 Unity Technologies 或其关联公司的商标。
