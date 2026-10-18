# Auto Terra Factions

[English](README.md)

一个tModLoader模组：最多6个AI阵营按原版规则游玩泰拉瑞亚，你可以观战或一起玩。

## 特性

- 自动 NPC 们可以采集、合成、建造基地、升级装备，有不同的武器偏好。
- 以挑战到月亮领主为止的所有 Boss 为目标。
- 设置和 NPC 信息随世界保存。
- 仅支持单人模式。

> 开发中所有测试都按 2 个阵营、每个阵营 6 个角色配置。选择过多的角色数可能导致游戏卡顿，或因为地图内资源耗尽而永久无法推进进度。

## 开启模组

进入地图后，依次选择 **设置 → 模组配置 → Auto Terra Factions**，将 **开启 AI 角色** 设置为“开”。

## 从源码安装

需要Steam版Terraria和tModLoader。

1. 安装 [.NET 8 SDK](https://dotnet.microsoft.com/)。
2. 克隆本仓库，并链接到 tModLoader 的 ModSources 目录（以 macOS 为例）：
   ```bash
   ln -s "$(pwd)" "$HOME/Library/Application Support/Terraria/tModLoader/ModSources/AutoTerraMod"
   ```
3. 在游戏内用 **创意工坊 → 开发模组 → 构建并重新加载** 构建；或关闭游戏后用命令行构建（会同时打包 `.tmod`）：
   ```bash
   tools/build.sh
   ```
4. 在模组菜单中启用Auto Terra Factions。

## 操作

| 默认按键 | 功能 |
| --- | --- |
| `[` / `]` | 观战上一个 / 下一个角色 |
| `/` | 切换自由镜头 |
| `Esc` | 观战时打开 / 关闭被跟随角色的物品栏（只读） |
| `P` | 在观战与亲自游玩之间切换 |

按键可在 设置 → 控制 → 模组控制 中修改。观战面板会显示当前实际绑定的按键、各阵营状态和世界的 Boss 进度。

## 代码结构

```
Common/
  Bosses/       Boss 目录、战力评估、召唤仪式、击杀记功
  Bots/         角色的身体动作、移动、生命体征、决策、看门狗
  Bots/Tasks/   效用 AI 任务：战斗、逃跑、采集、合成、建造、存取仓库……
  Building/     基地布局、建筑蓝图、房屋检测、仓库
  Factions/     阵营与阵营共享情报
  Navigation/   2 格宽 A* 寻路与路径执行
  Net/          角色向客户端的同步
  Planning/     进度目标、递归配方规划、资源来源、装备评估
  Spectating/   观战镜头与界面镜像
  Systems/      世界生命周期、原版钩子、性能统计、状态报告、无头测试模式
  UI/           观战面板与只读背包
  World/        世界扫描、地标与图格查询
Content/NPCs/   每个角色的 NPC 身体
tools/headless/ 无头服务器测试脚本
```

## 开发与测试

在聊天框输入 `/autoterra logging on` 开启诊断日志，写入 `client.log` / `server.log`；输入 `/autoterra logging off` 关闭。日志中会出现：

- `perf over 10s`：各子系统的耗时
- `status (...)`：每 30 秒所有角色的位置、任务、计划和卡住时长
- `watchdog: trapped`：角色被困时的移动状态和周围地形的字符画

无头测试不需要打开游戏客户端：

```bash
tools/headless/run.sh 300           # 在 AutoTest 世界运行 300 秒
tools/headless/run.sh 300 --fresh   # 删除并重新生成 AutoTest 世界
AUTOTERRA_SCENARIO=boss tools/headless/run.sh 300
```

脚本会设置 `AUTOTERRA_HEADLESS=1`，让专用服务器在没有玩家连接时也持续更新世界；无头模式下诊断日志始终开启。日志位于 tModLoader 安装目录的 `tModLoader-Logs/server.log`。
