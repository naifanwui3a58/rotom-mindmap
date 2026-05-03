# Rotom Mindmap

Rotom Mindmap 是一个基于 Godot 4.6 Mono + C# 的本地桌面写作与知识整理工具。当前版本围绕幕布式大纲编辑展开，使用 Markdown 文件进行大纲写作输入，一键生成思维导图和 CSV 表格，基于 Codex 辅助完成，支持文档管理、实时保存、搜索、脑图生成与导出、CSV 导出。

Rotom Mindmap is a local desktop writing and knowledge organization tool built with Godot 4.6 Mono and C#. The current version centers on Mubu-style outline editing, using Markdown files for structured writing input and offering one-click generation of mind maps and CSV tables, Assisted by Codex. it supports document management, real-time saving, search, mind map generation and export, and CSV export.

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
