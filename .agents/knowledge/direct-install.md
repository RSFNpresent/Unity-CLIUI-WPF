# 无 CLI 编辑器管理规范

## 模式策略

| 模式 | 行为 |
| --- | --- |
| `Auto` | 只检测并使用现有 Unity CLI；不得直接下载。 |
| `Direct` | 使用官方 Release API/CDN 手动下载、安装和管理。 |
| `UnityCli` | 默认使用 Unity CLI 后端；用户可在编辑器安装提示中选择一次手动安装。 |

`BackendModePolicy.UsesDirectDownloads` 只有对 `Direct` 返回 `true`。Direct 模式的版本详情通过本地扫描只显示已安装模块；显式模块管理仍可请求官方完整目录。任何模式请求远程目录失败后，都只显示本地已安装模块，不回退到全量可用模块缓存。
`UnityCli` 模式的一次手动安装不得改变管理模式；它只在用户点击明确按钮后调用 Direct 编辑器安装。

## 官方数据源

- Release API：`https://services.api.unity.com/unity/editor/release/v1/releases`。
- 查询固定使用 `platform=WINDOWS` 与 `architecture=X86_64`。
- 下载 URL 必须为 HTTPS；EXE 必须来自 `download.unity3d.com` 的已知编辑器或 Target Support 路径。
- 编辑器、模块、子模块、目标目录、重命名和 integrity 均以所选精确版本的官方响应为准。
- `downloadSize.value` 与 `installedSize.value` 可能不是严格 JSON 整数；解析必须接受整数、字符串数字和 `.0` 数字。

## 安装流程

1. 按精确 Unity 版本查询官方元数据。
2. 构建编辑器与所选模块的依赖计划，并校验模块 slug 属于该版本。
3. 最多并行下载 3 个包；服务允许并发边界为 1 到 8。
4. 编辑器和所选模块在同一安装计划中下载；当前是包级并发，不做单包分片下载。
5. 支持 Range 断点续传和 ETag/If-Range；可变包通过 ETag 重新验证。
6. 校验长度与官方 integrity；无可验证完整性的可变包不得盲目信任缓存。
7. Direct 设置允许选择编辑器安装根目录、下载缓存目录和是否保留下载包。
8. 不保留下载包时，只在安装成功后删除本次下载的包和对应 metadata。
9. 安装编辑器前必须显示确认框；确认框显示版本、模块和安装目录。
10. CLI 模式点击修改安装目录时，必须提示 CLI 不能选择安装目录，再由用户选择是否一次手动安装。
11. 所有 ZIP 先解压到独立 staging，并验证路径、排除项、`destination` 和 `extractedPathRename`。
12. 全部 staging 成功后，按目标目录锁串行提交，避免并发覆盖。
13. 验证 `Unity.exe` 版本/修订并保存状态；失败或取消要记录事务并清理 staging。

## 安全边界

- 永久拒绝 `System.Security.Cryptography.Xml.dll`。
- 支持的包类型仅为 `EXE`、`ZIP`、`PO`。
- 任何归档条目和清单路径都不得逃逸编辑器根目录。
- 卸载只能作用于已确认的目标编辑器目录，不得扩大到安装根目录或其他 Unity 版本。

## 回归覆盖

测试至少覆盖：模式与模块回退策略、Release API 解析与分页、size 字段宽容解析、integrity 编码、依赖版本保护、清单/ZIP 路径穿越、排除文件、重命名规则、取消与续传、ETag 重新验证。
