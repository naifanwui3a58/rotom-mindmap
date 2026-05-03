# Rotom Mindmap

Rotom Mindmap 是一个基于 Godot 4.6 Mono + C# 的本地桌面写作与知识整理工具。当前版本围绕幕布式大纲编辑展开，支持文档管理、实时保存、搜索、脑图生成与导出、CSV 导出、回收站恢复。

## 当前能力

- 本地文档与文件夹管理
- 幕布式大纲编辑
- 标题与左侧文档名实时同步
- 自动保存
- 标题搜索与正文搜索
- 回收站恢复与彻底删除
- 大纲生成脑图
- 导出 Markdown / CSV / 脑图 PNG / JPG / SVG
- 支持拖入外部 `.md` / `.markdown` / `.txt`

## 运行方式

1. 用 Godot 4.6 Mono 打开目录 `D:\Godot\rotom-mindmap`
2. 等待 Godot 刷新 .NET 项目
3. 运行主场景 [Main.tscn](/D:/Godot/rotom-mindmap/Scenes/Main.tscn)

命令行也可以直接编译：

```powershell
dotnet build "D:\Godot\rotom-mindmap\RotomMindmap.csproj"
```

## 界面与缩放

- 设计基准尺寸是 `1920x1080`
- 项目使用 `canvas_items + expand`
- 全屏或更大窗口时按比例扩展，不再强制保留黑边

## 数据目录

为了避免“编辑器里测试过的数据”直接混到导出的 exe 里，项目现在分开使用工作区：

- Godot 编辑器运行：`user://workspace-editor/`
- 导出 exe 运行：`user://workspace-app/`
- 旧版兼容迁移：如果编辑器工作区还是空的，会从旧的 `user://workspace/` 自动迁移一次

工作区内目录：

- `vault/`：文档库
- `.trash/`：回收站
- `exports/`：导出文件
- `.mindmap/`：脑图节点布局状态
- `ui-settings.json`：语言等界面设置

## 大纲规则

- 标题单独编辑，不需要手打 `#`
- 中间直接输入条目
- `Enter` 新建同级
- `Tab` 缩进
- `Shift+Tab` 反缩进
- 自动序列化为 Markdown 文件

## 自动保存

- 文本变化后启动 `0.6s` 防抖
- `0.6s` 内没有继续输入就自动保存
- 切换文档、删除、导出、关闭窗口前会强制保存一次

## 标题同步规则

显示名按以下顺序推导：

1. 第一条 H1
2. 第一条非空列表项
3. 第一条非空普通文本
4. 文件名友好化
5. `Untitled`

说明：

- 左侧显示名实时同步
- 创建时根据标题生成文件名 slug
- 后续改标题只改显示名，不自动改物理文件名
- 文件名冲突时自动追加 `-2`、`-3`

## CSV 映射

当前 Markdown / 大纲会映射为以下列：

- `id`
- `parent_id`
- `level`
- `type`
- `title`
- `body`
- `order`
- `path`

适合作为层级配置表使用。

## 导出 exe

1. 在 Godot Mono 打开 `Project > Export`
2. 添加 `Windows Desktop`
3. 使用带 Mono/.NET 的 Windows 导出模板
4. 项目已带 [export_presets.cfg](/D:/Godot/rotom-mindmap/export_presets.cfg)
5. 默认输出是 `build\RotomMindmap.exe`

## 验证建议

- `dotnet build` 通过
- 运行主场景，确认中文正常显示
- 切换语言后位置不乱跳
- 全屏后界面不再出现黑边式留空
- 编辑器数据与导出 exe 数据目录分离
- 导出 Markdown / CSV / 脑图文件后检查内容
