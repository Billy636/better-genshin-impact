# Lycia-dev 个人开发分支

本地目录：`D:\cWorking\BGICode`。

- 个人开发分支：`Lycia-dev`，跟踪 `origin/Lycia-dev`。
- `origin`：https://github.com/Billy636/better-genshin-impact.git
- `upstream`：https://github.com/babalae/better-genshin-impact.git
- `main` 用于跟进公版，个人修改放在独立提交中。向公版贡献功能时，可以从最新公版另建功能分支，只挑选对应提交，避免把所有个人定制带入同一个 PR。

## 2026-10-15：一条龙启动时恢复遗留的 F1 委托页

起点：fork 的 `main` 提交 `7a48461d7a55ede6453a32b29f388597df42ee03`。此版本已使用 `GameRuntimeService` 和 `InputHub`；补丁按这些接口适配，没有回退到旧公版实现。

问题：联机切换下一条龙时，若游戏遗留在冒险之证委托页，启动条件无法识别通过，后续异常处理和锄地任务不能开始。用户已确认手动关闭书本后能够继续执行。

采用茶包 B175“在启动等待阶段恢复界面”的思路，但没有直接照搬 `CancellationToken.None` 配合 `.Wait(5000)` 的实现，也没有注释公版的结束奖励检查。

行为边界：

- 仅在一条龙启动时启用；保留原有公开方法签名和其他调用方的默认行为。
- 等待 30 秒后，每隔约 30 秒检查一次，只有同时识别到本地化的“委托”和“每日委托奖励”时才关闭一次。
- 游戏必须已初始化、捕获运行且在前台，没有停止或暂停，并能取得正式任务共用的输入锁。
- 每次仅发送一个 ESC，异步等待 900ms 后重新检测启动条件，不把按键发送当作恢复成功。
- 没有脱离调用链的后台恢复任务；识别后再次检查停止和前台状态。
- 识别失败保留等待，不在登录、加载或其他未知界面无条件反复按 ESC/Enter。
- 不修改 JS 脚本、路线、队伍、账号、User 数据或公版奖励检查逻辑。

验证：

- 独立恢复检查项目覆盖 10 个行为：未知画面、非前台、旧任务占锁、预先停止、识别期间停止、识别异常释放锁、等待期间持锁、并发恢复互斥、取消后结束、单次关闭动作完成。
- 在此前 0.66.0 适配中，使用同一张已保存的实际书本截图，Paddle OCR V4/V5 在 1280、1920、2560、3840 宽度的 16:9 缩放图上均匹配两项文本，空白图均未匹配。这是截图离线验证，不是四种显示设置的实际游戏测试。
- 当前 fork 的编译和检查结果以本次提交说明为准。

限制：没有部署或操作真实游戏。其他 F1 页签、其他语言和真实联机交接仍需使用中确认；识别漏判会保留原有等待。该补丁不宣称修复书本被上一个任务打开的完整时序原因。

## 编译和检查

在源码根目录执行：

```powershell
dotnet run --project Test/StartupHandbookRecovery.Checks/StartupHandbookRecovery.Checks.csproj -c Release
dotnet build BetterGenshinImpact/BetterGenshinImpact.csproj -c Release -p:Platform=x64
```

构建输出在 `BetterGenshinImpact/bin/x64/Release/net8.0-windows10.0.22621.0`。构建不等同部署，不自动覆盖正在使用的 BGI。

## 2026-10-29：同步官方 main

- 同步基线：官方 `main` 的 `94c1de65fa5c525496a776ad63087162626dfbe7`（2026-10-28，扩大自动地脉花交互选项识别范围）。相对此前基线 `14537eae`，补齐 9 个提交，项目版本更新至 `0.66.3-alpha.1`。
- 官方默认分支已切换为 `refactor/20260930`，包含 .NET 10 和任务系统等大规模重构。本个人版继续沿用既有的官方 `main` 基线；“已同步”仅指上述 main 提交，不代表已迁移到默认重构分支。
- `main` 快进同步；`Lycia-dev` 使用合并保留已发布的个人提交，不改写远端历史。同步前状态保存在 `backup/Lycia-dev-before-sync-20261029`（`ef6ebac0`）。
- 冒险之证委托页恢复修复完整保留；同步后，`ScriptService.cs`、`StartupHandbookRecovery.cs` 和个人修改的 `OneDragonFlowViewModel.cs` 与同步前一致。
- 同步操作限于源码仓库与其中的编译产物，未部署到真实安装目录，也未运行主程序或操作游戏。

本次验证：

- Release/x64 主项目构建通过，0 错误、583 个警告（未在此次同步中清理公版警告）。
- `StartupHandbookRecovery.Checks` 的 10 项行为检查全部通过。
- 按 `FolderProfile` 成功发布 win-x64 单文件版本，保留 .NET 8 桌面运行时依赖。发布命令覆盖 `Version=0.66.3-alpha.1+Lycia.20261029.94c1de65`，源码项目版本继续保持公版值。
- 新发布包位于 `BetterGenshinImpact/bin/publish/Lycia-20261029/`，入口为其中的 `BetterGI.exe`；旧目录中的历史发布包不代表本次构建。
- 构建及发布日志归档在 `BetterGenshinImpact/bin/sync-20261029/`。编译与离线检查通过不等同于游戏内实测通过。

## 2026-10-29：锄地四阶段基线计时

目的：先测量现有行为，暂不缩短等待、改动路线、战斗或拾取策略。计时不新增识图和游戏输入，每条地图追踪结束或取消后向独立 JSONL 写入一条记录，输出错误不会传播到游戏任务。

- 开关：在运行目录创建空文件 `User/pathing-timing.enabled`；默认关闭。删除该文件后，从下一条路线起停止记录。
- 产物：`log/pathing-timing/timing-yyyyMMdd-进程号.jsonl`。记录配置组、路线、点位、分段、尝试次数、版本、完成状态和阶段明细。
- `Teleport`：传送点处理，含开图、选择、加载和传送后等待；`teleport_loading_wait` 是其中的等待子项。
- `Move`：移动前操作、转向、粗略移动及精确靠近；`move_recovery` 是其中的脱困子项。
- `Fight`：战斗 action，扣除其内部明确的战后拾取阶段。`fight_end_check` 记录已有结束检查的占用时间，不推断最后一只怪死亡时刻。
- `Pickup`：战后拾取判断、聚物及周边扫描，或显式拾取 action；包括已有冷却与等待。伴随跑图/战斗的 JS 模板拾取和 AutoPick 不额外叠加计时。
- `Other`：初始化、换队、恢复及动作完成后的固定等待等。五类主阶段互斥，合计等于路线墙钟总时长；子项已包含在主阶段内，不能重复相加。
- `party_end_checks`、`pickup_empty_sweeps`、`move_recoveries`、路线重试等只计数原有事件。没有把按 F 次数或路线声明怪物数当作实际拾取/击杀数量。
- 墙钟时间包含手动暂停；取消/失败路线以独立状态保留，基线分析需分开。路线外 JS 的准备、传送与配置组间等待不在这条路线的计时范围内。

验证：`dotnet run --project Test/PathingTiming.Checks/PathingTiming.Checks.csproj -c Release` 覆盖默认关闭、嵌套阶段互斥、配置组和异步上下文隔离、取消留档及磁盘/日志故障隔离；原书本恢复检查另行回归。未以启动游戏作为代码检查方式。

分析次日数据：

```powershell
./Build/Scripts/Summarize-PathingTiming.ps1 -LogDirectory '<运行目录>/log/pathing-timing' -Day 20261030
```

用户已明确授权本轮通过 SSH 向笔记本的独立目录部署计时版、复制配置并切换启动入口。保留旧版目录及原启动配置作为回退；不将真实 `User` 数据提交到 Git。

本次交付记录：

- Release/x64 编译及单文件发布通过（0 错误）；计时检查 11 项、书本恢复检查 10 项全部通过。汇总脚本已用检查生成的 JSONL 验证。
- 发布版本 `0.66.3-alpha.1+Lycia.Timing.20261029`，核心代码提交 `0f431f8c`。笔记本 EXE 的 SHA256 为 `AB95900EEC3C80D0E754344C5DA2C523D4C32E1A371FB4816BC7A92811231A49`，与本地发布产物一致。
- 笔记本独立目录：`D:\Game\BetterGI-Lycia-Timing-20261029`。复制原茶包版的完整 User，20 个配置组文件逐一哈希一致；依赖脚本、路线和战斗策略存在，计时开关已启用。
- ABGI 的 `BetterGIAddress`、莫酱的 `bgiFolder` 与脚本文件监控路径均切换到新目录；收益自动汇总任务也已切换，任务最近结果为 0。历史收益事件保留原证据路径。
- 沿用莫酱原有每日 `04:05` 定时与联机调度规则，不另建重复的一条龙定时。重启后确认莫酱登录成功，控制配置仍指向新目录。
- 已用交互会话的 `BGI-Lycia-Timing-Manual` 任务启动该 EXE，进程实际路径指向上述目录，启动日志无错误。仅启动空闲 BGI 主程序，未开始游戏内路线测试。
- 原程序目录保留；启动配置及收益任务动作备份在笔记本 `D:\Game\Lycia-Timing-backup-20261029`，其中 `Rollback.ps1` 可在退出游戏后恢复原启动路径。新目录内的测试数据会保留，不会自动回写覆盖旧 User。
- 笔记本桌面新增 `BGI-Lycia-计时版.lnk`。次日测试日志位于新目录的 `log/pathing-timing`；首条路线结束或取消时才会生成 JSONL。
