# Default Methodology

Treat facts as the constraint on judgment. Do not guess when the current state can be checked directly.

For every new conversation, use `qiushi-methodology` as the default top-level working methodology.
The user may refer to this as `qiushi-skill`, but the installed local skill name is `qiushi-methodology`.

If the host fails to surface `qiushi-methodology` in the current session skill list, still apply its methodology as the effective top-level guide and use `arming-thought` as the opening execution layer for that methodology.

Default stance:

- investigate first when facts are incomplete
- identify the main contradiction before proposing tradeoffs
- validate plans through practice and iteration
- perform criticism and self-criticism after implementation or review

For coding, design, research, and game-development tasks, keep this methodology active and combine it with the appropriate domain workflow instead of replacing the workflow.

--- project-doc ---

# 项目说明

## 项目目标

Rotom Mindmap 是一个受“黑曜石 + 幕布”启发的本地桌面写作与知识整理工具。当前目标是做稳定、易维护、易扩展的 MVP，核心围绕幕布式大纲编辑，并提供搜索、脑图、CSV 配置导出和数据安全机制。

## 运行方式

1. 使用 Godot 4.6 Mono 打开项目目录 `D:\Godot\rotom-mindmap`
2. 确认 C# 支持可用，等待 Godot 刷新 .NET 项目缓存
3. 运行主场景 `res://Scenes/Main.tscn`

界面基准尺寸是 `1920x1080`，窗口缩放策略为 `canvas_items + expand`。

## 构建方式

```powershell
dotnet build "D:\Godot\rotom-mindmap\RotomMindmap.csproj"
```

日常开发以 Godot Mono 编辑器为主，用于场景编辑、2D 可视化调整、脚本热重载与导出配置。

## 导出 exe 的方式

1. 在 Godot Mono 中打开 `Project > Export`
2. 添加 `Windows Desktop`
3. 选择 Windows Mono/.NET 导出模板
4. 项目已附带 `export_presets.cfg`
5. 默认输出路径为 `build\RotomMindmap.exe`

## 目录结构

- `Scenes/`
  - `Main.tscn`：主界面场景
  - `Components/`：文库行、大纲行等可视化组件
- `Scripts/`
  - `Main.cs`：主界面控制与交互
  - `Domain/`：数据模型
  - `Services/`：文件系统、标题规则、本地化、Markdown 解析等服务
  - `UI/`：脑图画布与交互控件
- `Data/Localization/`：中英文界面文案表
- `project.godot`：Godot 项目配置
- `RotomMindmap.csproj`：C# 项目文件
- `RotomMindmap.sln`：解决方案文件

## 数据存储规则

运行时数据保存在 `user://` 下，并区分编辑器与导出版本：

- Godot 编辑器运行：`user://workspace-editor/`
- 导出 exe 运行：`user://workspace-app/`
- 兼容旧版：编辑器工作区为空时，会自动从 `user://workspace/` 迁移一次

工作区目录：

- `vault/`：实际 Markdown 文档库
- `.trash/`：回收站目录
- `exports/`：导出文件目录
- `.mindmap/`：脑图布局状态
- `ui-settings.json`：语言等界面配置

## 文档命名同步规则

- 显示名不是单独输入字段，而是从文档内容实时推导
- 推导顺序：
  1. 第一条 `# H1`
  2. 第一条非空列表项
  3. 第一条非空普通文本
  4. 文件名友好化
  5. `Untitled`
- 文件名在创建时由标题 slug 生成
- 后续修改标题只改显示名，不自动改物理文件名
- 重名允许显示重复；物理文件名冲突时自动追加 `-2`、`-3`
- 非法字符只在 slug 化时处理，不影响显示标题

## 回收站规则

- 删除文档或文件夹时，不直接永久删除
- 目标会移动到 `user://.../.trash/<trash_id>/payload/`
- 同时写入 `meta.json` 保存原路径、删除时间、类型
- 恢复时优先回到原路径
- 原路径冲突时自动恢复到 `*-restored-2` 之类的新路径
- 彻底删除时直接删除回收站条目目录

## 大纲规则

- 标题单独编辑，不需要手打 Markdown `#`
- 条目直接输入
- `Enter` 新建同级条目
- `Tab` 缩进
- `Shift+Tab` 反缩进
- 保存时自动序列化为 Markdown
- 脑图和 CSV 都基于这份 Markdown 链路继续工作

## 自动保存规则

- 文本变化后启动 `0.6s` 防抖
- `0.6s` 内没有继续输入则自动保存
- 切换文档、删除、导出、关闭窗口前强制保存一次

## 基础验证方法

- `dotnet build` 必须通过
- 运行主场景验证：
  - 中文文案正常显示
  - 全屏不再出现错误留黑边
  - 文档创建、编辑、自动保存正常
  - 回收站恢复正常
  - 搜索、脑图、CSV 正常
  - 导出 exe 后使用独立的 `workspace-app`
