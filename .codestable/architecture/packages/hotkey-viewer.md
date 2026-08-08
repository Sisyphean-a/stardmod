---
scope: package:hotkey-viewer
---

# 快捷键查看器包

`packages/HotkeyViewer` 是独立的 SMAPI mod 包，运行时身份为 `xixifu.HotkeyViewer`，入口程序集为 `HotkeyViewer.dll`。

## 职责

- 通过可配置快捷键打开一个独立面板，查看当前游戏本体和已加载模组的快捷键。
- 读取原版 `Game1.options` 中的本体按键配置。
- 尝试读取 GMCM 已注册的按键选项，以取得较准确的功能名称和模组来源。
- 扫描已加载模组目录下的 `config.json`，在过滤敏感字段后推测快捷键配置。
- 按按键分组标出潜在冲突，并提供来源筛选、冲突筛选、搜索与刷新。

## 边界与锚点

- SMAPI 入口和打开快捷键：`packages/HotkeyViewer/ModEntry.cs`。
- 配置契约：`packages/HotkeyViewer/ModConfig.cs`、`packages/HotkeyViewer/config.json`。
- 快捷键收集与冲突计算：`packages/HotkeyViewer/HotkeyCatalog.cs`、`packages/HotkeyViewer/HotkeyModels.cs`。
- 游戏内面板：`packages/HotkeyViewer/HotkeyViewerMenu.cs`。
- 包身份：`packages/HotkeyViewer/manifest.json`。
- 构建和游戏程序集引用：`packages/HotkeyViewer/HotkeyViewer.csproj`。

## 运行约束

- 默认打开键为 `OemQuestion`，即 `?` 所在按键；可通过 GMCM 或 `config.json` 修改。
- 面板只在没有其他活动菜单、事件或对话时由快捷键打开；再次按打开键会关闭已经打开的面板。
- 冲突判断基于当前收集到的键鼠按键配置；同键被多个功能使用时标为潜在冲突，不判断具体场景是否一定冲突。
- 手柄按键不进入默认面板和冲突统计，避免键鼠排查场景出现噪音。
- GMCM 注册项优先作为模组快捷键依据；缺少 GMCM 信息时，`config.json` 只作为配置推测来源。
- `config.json` 扫描不会显示路径疑似 API key、token、密码、凭证、私钥或认证密钥的字段。
