# Unity CLIUI 项目规范

## 目录职责

- `.agents/readme.md`：协作入口、项目结构和强制约束。
- `.agents/log.md`：按日期记录最近完成的工作和验证结果。
- `.agents/mistakes.md`：记录踩坑、禁止事项及预防方式。
- `.agents/knowledge/`：按功能拆分设计规范；行为变化时同步更新对应文档。

开始任务前必须阅读本文件、`log.md`、`mistakes.md`，再读取与任务相关的 knowledge 文档。完成行为变更后更新日志和知识文档；发现新坑时补充 `mistakes.md`。

## 项目概览

Unity CLIUI 是 Windows x64 上的第三方 WPF Unity 编辑器与工程管理器。应用基于 `.NET 10`，支持英文和简体中文，并提供自动检测、直接管理（无 CLI）和 Unity CLI 三种管理模式。

## 代码结构

- `App.*`：WPF 应用入口和全局资源。
- `MainWindow.*`：主界面与当前主要协调逻辑。
- `*Window.xaml(.cs)`：独立对话框及少量交互逻辑。
- `Services/`：CLI、官方 Release API、下载、校验、安全解压、状态存储和工程创建。
- `Models/`：Unity Release API 与安装计划数据模型。
- `Interop/`：Windows 原生窗口和环境变量通知。
- `Resources/`：`en-US`、`zh-CN` 本地化资源。
- `Properties/PublishProfiles/`：两种 Windows x64 发布配置。
- `Tests/UnityCliUi.DirectTests/`：无外部测试框架的回归测试程序。
- `Assets/`：应用图标等静态资源。

## 强制行为约束

1. 只有设置中明确选择 `Direct`（无 CLI）模式时，才默认允许手动下载并安装 Unity 官方包。
2. `Auto` 只检测已有 Unity CLI，不得隐式切换为直接下载；`UnityCli` 默认只走 CLI 后端。
3. `UnityCli` 模式仅允许用户在安装编辑器提示框中明确选择“一次手动安装”时，临时调用 Direct 安装；不得改变管理模式。
4. Direct 模式的编辑器版本详情只显示本机已安装模块；远程模块目录不可达时，其他模式也回退到已安装模块，不使用全量缓存冒充实时结果。
5. 当前版本通过集中策略跟随 Windows 动画设置；页面、亚克力和窗口状态动画不得阻塞最终状态切换。
6. 编辑器与模块数据只能来自 Unity 官方 Release API 和官方分发地址，并执行完整性、路径与包类型校验。
7. 永久拒绝 `System.Security.Cryptography.Xml.dll`；它是测试文件，不属于官方分发内容。
8. 发布 ZIP 必须保持单 `Unity-CLIUI.exe` 布局，不包含 Unity CLI，也不附带额外 DLL。
9. 创建工程不使用官方模板，只生成最小必要目录和文件；包版本必须来自所选编辑器官方元数据。
10. 创建成功后立即加入工程列表，并使用用户选择的准确编辑器启动。
11. 工程测试目录使用 `E:\`，不得在 `D:\` 创建 Unity 测试工程。
12. 不得终止用户正在运行的 `E:\Unity-CLIUI.exe` 或 Unity 编辑器进程。

## 模块化与文件规模

- 新文件以单一职责为原则，目标少于 600 行；超过 600 行必须说明原因并优先拆分。
- 不继续向 `MainWindow.xaml`（约 1954 行）和 `MainWindow.xaml.cs`（约 4776 行）堆积大段功能。
- 修改主窗口时，优先把业务逻辑、持久化、模型或策略抽到小型 `Services`/独立类型；不要为无关任务做一次性大重构。
- 测试入口当前约 690 行，新增测试时优先拆分测试分组，避免继续增长。
- 可并行且文件边界独立的任务才使用子代理；同一文件或小范围改动不并行，避免合并冲突。

## 开发与验证

```powershell
dotnet build unity-cli-ui.csproj -c Release
dotnet run --project Tests\UnityCliUi.DirectTests\UnityCliUi.DirectTests.csproj -c Release
dotnet publish unity-cli-ui.csproj -p:PublishProfile=win-x64-framework-dependent
dotnet publish unity-cli-ui.csproj -p:PublishProfile=win-x64-self-contained
.\scripts\Publish-Packages.ps1
```

业务服务至少运行回归测试；UI 变更还要启动应用检查布局、下拉框、语言切换和交互。正式打包只运行 `Publish-Packages.ps1`，并始终生成普通版和自包含版。

## Knowledge 索引

- [架构与状态](knowledge/architecture.md)
- [无 CLI 安装](knowledge/direct-install.md)
- [最小工程创建](knowledge/project-creation.md)
- [UI 与本地化](knowledge/ui-conventions.md)
- [动画与窗口过渡](knowledge/animation.md)
- [构建与发布](knowledge/release.md)
