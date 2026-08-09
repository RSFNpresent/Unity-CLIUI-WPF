# 架构与状态规范

## 技术基线

- UI：WPF，目标框架 `net10.0-windows`。
- 运行平台：Windows x64。
- 根命名空间：`unity_cli_ui`。
- 产品名：`Unity CLIUI`；公司名：`RSFNpresent`。
- 当前版本以 `unity-cli-ui.csproj` 的四个版本字段为准。

## 运行结构

`MainWindow` 当前承担页面导航、编辑器列表、工程列表、设置、缓存与后端协调。新增可复用逻辑优先进入以下边界：

- Unity CLI 进程：`UnityCliService`。
- 无 CLI 编辑器管理：`DirectUnityService`。
- 官方版本目录：`UnityReleaseCatalogClient`。
- 下载与恢复：`PackageDownloadService`。
- 完整性与包安全：`PackageIntegrityVerifier`、`PackageSafetyPolicy`。
- 安全解压：`SafePackageExtractor`。
- 安装计划与状态：`DirectPackagePlanner`、`DirectInstallStateStore`。
- 本地编辑器发现：`InstalledEditorScanner`。
- 模块显示策略与本地目录：`EditorModuleDisplayPolicy`、`InstalledModuleCatalog`。
- 工程创建：`UnityProjectCreator`。
- Unity 版本解析与比较：`UnityVersionPolicy`。
- 本地化：`LocalizationService`。

## 本地状态

应用状态根目录为 `%LOCALAPPDATA%\unityCLI-UI`：

- `settings.json`：语言、管理模式、CLI 目录和编辑器安装根目录。
- `recent-projects.json`：受管工程列表。
- `editor-installations.json`：编辑器发现缓存。
- `available-modules.json`：按编辑器版本缓存的模块状态。
- `packages\`：无 CLI 下载缓存及元数据。
- `staging\`：安全解压临时区。
- `install-state\`：直接安装的编辑器状态和事务。

默认无 CLI 编辑器根目录为 `%LOCALAPPDATA%\Unity\Editors`，但用户设置可覆盖。

## 依赖原则

- 现有下载、JSON、压缩和并发功能使用 .NET BCL，不需要额外运行时文件。
- 只有外部包能显著降低复杂度且不破坏单 EXE 发布时才引入；必须锁定版本、确认许可证并验证两种发布配置。
- 数据模型与持久化类型应离开 `MainWindow.xaml.cs`，保持可独立测试。

## 拆分优先级

1. 将设置和缓存读写抽为独立 Store。
2. 将工程注册、扫描、排序和启动抽为工程服务。
3. 将编辑器列表协调逻辑抽为服务或 ViewModel。
4. 将大页面 XAML 拆为 UserControl，并共享现有 Win10 样式资源。

拆分随功能增量进行，避免一次性改写整个主窗口造成大范围回归。
