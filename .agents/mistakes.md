# 踩坑与禁止事项

## 产品行为

- 禁止让 `Auto` 模式触发手动下载。`UnityCli` 模式只允许在编辑器安装提示中由用户明确选择一次手动安装。
- 禁止让 Direct 版本详情从全量可用模块缓存恢复数据；应只显示本机已安装模块。
- 远程目录失败时禁止用旧全量缓存伪装成功；改为本地扫描与已登记模块，并显示离线回退状态。
- Unity Release API 的 `installedSize.value` 和 `downloadSize.value` 不保证始终是 JSON Int64；模型解析必须接受字符串数字和 `.0` 数字。
- Unity 版本正则不得停在 `f1`。必须保留 `f1c1` 等后缀，否则工程版本与列表版本无法匹配。
- 禁止把 Unity CLI 放入安装包；本地 `unitycli-*.exe` 仅供开发检测，已由项目文件和 `.gitignore` 排除。
- 禁止接收或打包 `System.Security.Cryptography.Xml.dll`。下载计划、清单字段和 ZIP 条目都要检查。
- 禁止硬编码不同 Unity 编辑器的 Visual Studio 包版本；必须读取目标编辑器官方 Package Manager 清单。
- 禁止使用官方工程模板或生成大量默认资源；当前需求是可由 Unity 首次打开时补全的最小工程。
- 禁止创建后使用相近版本的任意编辑器启动；必须使用创建窗口实际选择的安装路径。

## 文件与安全

- ZIP、`destination`、`extractedPathRename` 都可能包含路径穿越；解析后必须验证目标仍位于允许根目录内。
- 不得直接解压覆盖正式目录。先进入独立 staging，全部验证成功后再提交。
- 不得把下载中断后的部分文件当成完整包。恢复下载要结合 Range、ETag/If-Range、长度和完整性元数据。
- 不得在已有工程目标目录内补文件；创建目标已存在时必须失败，防止覆盖用户内容。
- 不得在 `D:\` 放置 Unity 工程测试数据；统一使用 `E:\`，单元测试临时数据可使用系统临时目录。
- 不得清理或终止用户的 Unity/Unity CLIUI 进程，尤其是从 `E:\` 启动的进程。

## UI 与维护

- 不要只修改一个下拉框模板。语言、管理模式/CLI 检测、工程编辑器选择需要保持同一 Win10 风格。
- 表头分隔线变化后同时检查列表列宽、版本列和最后打开时间中心线；只改视觉线宽会造成错位。
- 新增显示文本必须同时加入两个语言 JSON，不得在代码中直接写单一语言 UI 文本。
- WPF `Run.Text` 绑定只读 CLR 属性时必须显式使用 `Mode=OneWay`；其默认绑定模式可能尝试回写并在启动阶段触发 `XamlParseException`。UI 构建通过后仍必须实际启动验证。
- `MainWindow.xaml(.cs)` 已严重超出 600 行目标。禁止继续加入可独立测试的业务逻辑，优先拆到服务或控件。
- PowerShell 控制台可能错误显示中文；不能仅凭终端乱码重写文件，先按 UTF-8 读取或检查原始字节。
- 禁止动画布局宽高、Margin 或大型模糊效果；使用 `Opacity` 与 `RenderTransform`，避免持续布局和模糊重绘。
- 禁止在原生亚克力切换时直接闪变；先用不拦截输入的遮罩覆盖，在中点切材质后再揭示。
- 关闭动画必须拦截外部关闭并防重入，动画完成后只调用一次真实 `Close()`。
- 不要在 Loaded 内直接启动依赖布局的 Storyboard；首次显示预设姿态后等 `ContentRendered`，页面切换排到下一次 Render。
- 亚克力过渡遮罩不得带覆盖工作区的 Background；只绘制实际透明的标题栏和导航区域。
- 仅平移 `WindowAnimationRoot` 会让原生 DWM 亚克力底板停在原位，形成静止不透明矩形；整窗位移动画必须操作 HWND 位置且保持窗口尺寸不变。
- 关闭时仅淡出 `_content` 或 WPF 遮罩不会影响 DWM 亚克力底板。不要给当前非透明 WPF `HwndSource` 运行时追加 `WS_EX_LAYERED`：样式会被拒绝，`SetLayeredWindowAttributes` 返回错误 87。动画策略启用时应捕获完整合成窗口并淡出透明覆盖窗，关闭被否决或动画取消时重新显示原窗口。

## 构建与发布

- 不要并行运行主 WPF 项目的 `dotnet build` 与引用它的 DirectTests；两者会争用 `obj` 下的 WPF 临时项目，偶发产生临时项目 `CS5001`。应顺序执行构建和回归测试。
- 如果 `dotnet build` 卡在还原阶段，先单独执行 `dotnet restore`。还原成功后用 `dotnet build --no-restore` 验证编译，避免重复卡在 NuGet 网络重试。
- self-contained 不等于必须散布数百个文件；当前配置启用单文件和压缩，发布目标会拒绝额外文件。
- 发布前检查两个 ZIP 都只有 `Unity-CLIUI.exe`，并再次排除 Unity CLI 与测试 DLL。
- 禁止单独打一个发布包。正式打包统一运行 `scripts\Publish-Packages.ps1`，同时生成普通版和自包含版。
- 禁止使用预览 .NET SDK 打包普通版。旧 apphost 可能跳转到错误的 Runtime 下载页。
- apphost 默认先读取 `DOTNET_ROOT_X64`，无效值会遮蔽已安装 Runtime。普通版必须只搜索 Windows 全局位置。
- 不要显式设置 `RuntimeFrameworkVersion`。该属性会把 apphost 包固定到旧补丁，运行时声明默认已是 `10.0.0`。
- 已存在的 GitHub 标签不得改写。发布前先检查 Releases 和 tags，再递增版本号。
- PowerShell 拼接上传 URL 时，`$uploadBase?name=...` 会误解析；使用 `${uploadBase}?name=...`。
- 大型资产用 `Invoke-RestMethod -InFile` 可能连接中断；失败后先查询远端状态，再用 Git 自带 curl 重试。
- 不得仅以构建成功代替启动验证；两种 EXE 都要实际启动、进入输入空闲并正常响应。
