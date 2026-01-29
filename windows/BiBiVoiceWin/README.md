# BiBiVoiceWin（Windows 桌面最小语音转文字）

目标：把「说点啥」Android 端的**语音 → 文本 → 自动插入当前焦点输入框**这条链路在 Windows 桌面端先跑通（最小可运行形态）。

## 快速开始

1. 进入项目目录：

   - `cd windows/BiBiVoiceWin`

2. 准备配置文件并填写密钥（两种方式二选一）：

   - 方式 A（推荐）：将 `config.example.json` 复制为 `config.json`
   - 方式 B：直接运行一次程序，程序会在 `%APPDATA%\\BiBiVoiceWin\\config.json` 自动生成配置文件

   然后填写 `Volc.AppKey`（App ID）与 `Volc.AccessKey`（Access Token）。
   如已开通「豆包流式语音识别 2.0（小时版）」服务，`ResourceId` 通常为 `volc.seedasr.sauc.duration`，`Endpoint` 默认已指向 `bigmodel_async`。

3. 运行：

   - `dotnet run`

4. 使用：

   - 程序以托盘图标常驻（双击托盘图标也可触发）
   - 默认热键 `Alt+Space`（仅在关闭“按住说话”时生效）
   - 第一次按下：开始录音
   - 再按一次：停止录音 → 调用 ASR → 把文本插入到录音开始时的前台窗口
   - 退出：右键托盘图标 → `退出`

## 说明

- 默认使用火山引擎 WebSocket 流式 `bigmodel_async`（与 Android 端 `VolcStreamAsrEngine` 相同协议）。
- 识别过程中会实时把内容插入到当前输入框，停止热键用于“收尾并等待最终结果”。
- 默认启用“按住说话”：长按 `Space` 开始识别并流式输入，松开停止并收尾；短按空格仍会输入空格。
- 按住说话时会忽略静音判停，避免中途停顿导致自动停止。
- 文本插入默认使用 `SendInput`（纯键盘输入，不走剪贴板）；如需兼容性更好可改为 `Clipboard`。
- 托盘图标为动态提示：上半表示麦克风录音状态，下半表示流式传输状态。
- `AutoStopEnabled=true` 时，检测到“说过话”后，持续静音达到 `AutoStopSilenceMs` 会自动停止并识别。
- 日志输出默认写入 `%APPDATA%\\BiBiVoiceWin\\logs\\app.log`；从 PowerShell/CMD 启动时会自动附加到父控制台并输出同样内容。
- 默认启用火山“语义顺滑”（`EnableDdc`）与“二遍识别”（`EnableNonstream`）以提升最终准确率；如需更原始结果可在配置中关闭。
- 如需启用 VAD 判停分句，可设置 `EnableVad=true`，并使用推荐参数 `VadEndWindowSizeMs=800`、`VadForceToSpeechTimeMs=1000`。
