# 最小 Unity 工程创建规范

## 目标

不复制官方模板，不预生成 Library 或示例资源，仅创建可被指定 Unity 编辑器识别并在首次打开时自行补全的最小工程。

## 输入与编辑器检查

- 用户选择父目录、工程名和一个已安装编辑器。
- 编辑器路径必须解析到现存的 `Unity.exe`，支持 `<root>\Editor\Unity.exe` 和直接编辑器目录布局。
- 从 `Unity.exe` ProductVersion 读取精确 Unity 版本和 revision。
- 所选列表版本与可执行文件版本不一致时必须失败。
- 支持正式、alpha/beta 和中国发行后缀，例如 `2022.3.62f3`、`6000.5.2f1`、`...f1c1`。

## 包版本来源

读取 `<Unity.exe 目录>\Data\Resources\PackageManager\Editor\manifest.json`：

- `com.unity.ide.visualstudio` 必须从官方清单取得合法版本，缺失时创建失败。
- `com.unity.ide.vscode` 优先使用官方清单；旧编辑器没有记录时回退为 `1.2.5`。
- 不得根据 Unity 大版本猜测、使用“最新”包版本或抓取不对应该安装的版本。

## 最小输出

```text
<Project>/
  Assets/
  Packages/
    manifest.json
  ProjectSettings/
    ProjectVersion.txt
```

`manifest.json` 仅声明 Visual Studio 与 VS Code 两个 IDE 包。`ProjectVersion.txt` 写入 `m_EditorVersion`，有 revision 时同时写入 `m_EditorVersionWithRevision`。

## 路径与失败语义

- 父目录必须已存在。
- 工程名不得为空、为 `.`/`..`、包含非法字符、路径分隔或以空格/句点结尾。
- 目标目录已存在时拒绝创建，不合并、不覆盖。
- 中途失败时只清理本次新建的目标目录，不触碰父目录其他内容。

## 创建后流程

1. 将新路径加入受管工程列表并持久化。
2. 刷新排序与状态显示。
3. 用创建对话框中选定的准确编辑器启动工程。
4. 启动成功后更新最后打开时间。

手工创建/启动测试工程统一放在 `E:\`，不得使用 `D:\`。
