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
