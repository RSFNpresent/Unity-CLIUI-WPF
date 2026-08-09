# 构建与发布规范

## 版本与元数据

发布前同步更新 `unity-cli-ui.csproj`：

- `Version`、`InformationalVersion`：三段产品版本，例如 `1.0.2`。
- `AssemblyVersion`、`FileVersion`：四段版本，例如 `1.0.2.0`。
- `Company` 保持 GitHub 用户名 `RSFNpresent`。
- README 的版本标题和功能说明同步。

先查询现有 GitHub Releases 和 tags。版本已存在时递增版本，不得移动、覆盖或删除既有发布标签。

## 发布配置

- `win-x64-framework-dependent`：依赖 .NET 10 Desktop Runtime。
- `win-x64-self-contained`：包含运行时，启用原生库单文件打包和压缩。
- `global.json` 只允许稳定 .NET SDK，避免普通版使用预览 apphost 的失效 Runtime 跳转。
- 项目级 `PublishSingleFile=true`。
- `ValidatePublishedLayout` 会拒绝除 `Unity-CLIUI.exe` 外的任何发布文件。

正式打包必须运行 `scripts\Publish-Packages.ps1`。脚本不可只生成一种包，必须顺序生成普通版和自包含版。

两种发布目录位于 `bin\Release\net10.0-windows\publish\<profile>\`，正式 ZIP 放在 `artifacts\v<version>\`。

## 发布前检查

1. Release 构建要求 0 错误；警告需评估并原则上保持 0。
2. 运行直接后端回归测试并确保全部通过。
3. 分别执行两个 Publish Profile。
4. 检查两个发布目录都只有 `Unity-CLIUI.exe`。
5. 检查 ZIP 也只有该 EXE，不含 Unity CLI 和 `System.Security.Cryptography.Xml.dll`。
6. 读取两个 EXE 的 Company、Product、FileVersion、ProductVersion。
7. 不终止用户已有进程，分别启动并确认进入输入空闲、正常响应，再关闭本次测试进程。
8. 只有明确文件错误时才计算文件 hash。

## GitHub 发布

- 先提交并推送 `main`，再创建指向该提交的 annotated tag 并推送。
- Release 标题使用 `Unity CLIUI v<version>`，说明简短列出主要变化及两种运行时要求。
- 上传后通过 GitHub API 回查：非 draft/prerelease、tag 正确、两个资产为 `uploaded`、远端大小和 digest 与本地一致。
- 用户明确要求“只推送不发布”时不得创建 Release；只有收到发布授权后才执行。
