# Floating HP Bar 1.2

在战斗中显示敌人名字、浮动血条与 HP 数字。


[English](README.md) | 简体中文

## 环境与兼容性

- Windows Steam 版 FINAL FANTASY IX，已安装 [Memoria](https://github.com/Albeoris/Memoria/releases/tag/v2025.07.04) 的 2025-07-13 热修版本。
- 2025-07-13 是本次发布准备使用的编译与兼容基准；更早版本和 Canary 未验证。
- 本次完成源码编译和压缩包检查，尚未完成发布候选包的游戏内回归。详见 [发布检查清单](RELEASE-CHECKLIST.md)。
- 不包含游戏、Memoria、汉化或 Moguri 的 DLL、素材和存档。游戏引用需从自己的安装目录读取。


## 安装

1. 从 Releases 下载 `FloatingHPBar-v1.2.zip` 并退出游戏。
2. 解压到游戏根目录（有 `FF9_Launcher.exe` 的目录），确认形成 `FloatingHPBar/StreamingAssets/Scripts/Memoria.Scripts.FloatingHPBar.dll`，不要套两层文件夹。
3. 在 Memoria 启动器的 Mods 页面启用本 mod，然后重启游戏。
4. 如果启动器未列出它，在 `Memoria.ini` 的 `[Mod]` 中，将 `"FloatingHPBar"` 追加到现有 `FolderNames` 和 `Priorities` 列表。保留其他 mod 的名称与顺序，不要用示例覆盖整行。


## 卸载

退出游戏，在启动器中停用本 mod，或从 `FolderNames` / `Priorities` 移除其名称，再删除 `FloatingHPBar` 文件夹。

## 关于 Boss 血量（1.2 修正）

1.1 错把原版 `btl_para.GetLogicalHP` 的返回值当作已经扣除脚本预留的血量；实际上该函数在原版模式下直接返回内部 HP，导致部分 Boss 和特殊敌人多显示 10000。

1.2 默认显示可消耗的血量：原版使用引擎 `IsNonDyingVanillaBoss` 的战斗列表和例外规则，对最大 HP > 10000 的对应单位扣除 10000 脚本预留值；当前值最低为 0。普通敌人即使 HP 超过 10000 也不因此扣减。Alternate Fantasy 继续使用引擎 `GetLogicalHP`，不会重复扣除。

| HpMode | 行为 |
|---|---|
| `0`（默认） | 自动：原版扣除引擎识别的 Boss 脚本预留值；其他模式使用引擎逻辑 HP |
| `1` | 原始数据 HP，包含脚本预留值，供排查用 |
| `2` | 保留旧版兼容行为，只处理原版不死 Boss |

只修改显示，不修改敌人实际 HP、伤害或战斗脚本。此规则依据当前 Memoria 的原版战斗列表；大幅改写原版战斗脚本的 Mod 可使用 `HpMode = 1` 查看原始值。特殊剧情战斗仍可能按回合或其他条件结束，血条不承诺代表胜利进度。

验证：已做编译与 HP 计算分支回归，尚未完成游戏内 Boss 战斗回归。

## 配置

编辑 `FloatingHPBar/FloatingHPBar.ini`，下一场战斗生效：

| 键 | 默认 | 说明 |
|---|---|---|
| `Enabled` | 1 | 总开关 |
| `ShowMaxHp` | 0 | 1 = 显示 `当前 / 最大` |
| `HpMode` | 0 | 血量显示方式，见上表 |
| `ShowName` | 1 | 1 = 血条上方显示敌人名字 |
| `NameFontSize` | 15 | 名字字号（1080p 基准，自动缩放） |
| `BarWidth` | 156 | 血条宽度（1080p 基准，自动缩放） |
| `BarHeight` | 22 | 血条高度（1080p 基准，自动缩放） |
| `OffsetY` | 8 | 血条相对头顶的额外上移像素；觉得太高/太低就调这个 |
| `ShowOnlyTargetable` | 0 | 1 = 只显示可选中敌人 |


## 已知限制

- 血条基于屏幕坐标绘制（IMGUI），不参与 3D 遮挡；敌人跑到屏幕外会隐藏
- 只对 `bi.slave == 0` 的敌人显示；多部位敌人的隐藏 slave 部位不会单独显示
- 名字直接使用战斗场景里的敌人名字（`ENEMY.et.name` / 双语模式下的 `FF9TextTool.BattleText`）。如果名字特别长会自动缩小字号，最小 8px
- 尚未在实机战斗中校准过血条高度；如果位置不理想，`OffsetY` 可以直接微调



## 组合使用与语言

- FloatingHPBar 与 BattleInfoOverlay 有协作初始化逻辑。单独使用 BattleInfoOverlay 时，浮窗可能要等第一次战斗指令结算后才出现。
- FloatingHPBar 和 ChocoboHotCold 均实现了 `IOverloadOnBattleInitScript`。在当前引擎只保留一个实现的机制下，存在启动钩子覆盖风险；四个 mod 同开尚需游戏内验证。遇到血条不出现时，先只启用 FloatingHPBar 排查。
- BattleInfoOverlay 的界面含固定中文；NeroNames 的名字固定为中文。其他文本取决于游戏及汉化环境。
- 不承诺与所有大型改版兼容。反馈时请附 Memoria 版本、启用顺序、复现步骤与相关日志，并先移除个人路径信息。


## 从源码构建

安装稳定版 .NET SDK 8 或更新版本。构建会读取你提供的游戏目录，只将 DLL 写入本仓库的 `FloatingHPBar` 子目录，不安装到游戏。

Windows PowerShell（在仓库根目录）：

```powershell
./build.ps1 -GamePath 'D:/Games/FINAL FANTASY IX'
./package.ps1 -GamePath 'D:/Games/FINAL FANTASY IX'
```

macOS / Linux 的 Bash 构建入口（仅提供源码构建，运行兼容性未验证）：

```bash
bash build.sh '/path/to/FINAL FANTASY IX'
```

源码位于 `Source/FloatingHPBar.cs`，编译产物位于 `FloatingHPBar/StreamingAssets/Scripts/`，压缩包输出到 `dist/`。DLL 和 dist 默认不纳入 Git，成品单独上传 Releases。直接下载 GitHub 的 Source code ZIP 需要自行编译。


## 作者与许可

作者：**JustPeace**。本 mod 的源码与原创内容按 [MIT License](LICENSE) 发布，允许修改、使用和再分发，须保留版权与许可声明。
FINAL FANTASY IX、Memoria 及第三方 mod 的权利归各自权利人所有。