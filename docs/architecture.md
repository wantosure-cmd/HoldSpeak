# 架构与约束

## 默认全局模式（0.1.14）

`triggerAnywhere` 默认 true。鼠标按下记录手势并选择当前应用的语音配置；到长按阈值直接调用 Begin，松开调用 End。不发起 HoverRequested／ProbeRequested／ActiveRequested，启动时跳过 UIA／AX 预热，不依据控件、密码、只读、焦点、应用切换或窗口切换终止。手势移动取消、双击保留、实体键取消、最长保持、暂停与故障释放仍保留。应用规则继续决定配置及明确禁用；回填位置由原语音输入法和当前焦点决定。以下检测细节仅适用于高级 JSON 中显式关闭全局模式的兼容路径。

## 模块

- Core：配置校验、应用规则、单线程手势状态机、引擎队列、探测与恢复协议。
- App：Avalonia 设置、托盘／菜单栏入口、诊断、单实例锁。
- ListenerLease：当前用户共享的输入监听独占文件句柄，独立于便携配置。平台启动监听前获取，监听线程结束后释放；进程异常结束由系统关闭句柄。残留文件不是存活证据，不能通过删除文件解除争用。
- Windows：专用 Hook 线程、SendInput、UIA 探测。
- Mac：Event Tap RunLoop、CGEvent、AX 探测。
- `--probe-stdio`：可替换的探测子进程。调用者软超时后忽略迟到结果，保留健康进程并读取完整响应，避免协议串线；每个非预热请求有 1 秒硬超时，硬超时后结束并重建。忙时不排队新的悬停请求。结果必须携带当前手势代次。
- 启用时先预热辅助功能接口，再安装输入监听，首次手势不承担子进程冷启动耗时。
- `--recovery-stdio`：恢复子进程。接收当前拥有的按键清单并确认，心跳为 300ms；2 秒失联或输入流结束后尝试 key-up。

## 手势

正常启动自动启用；烟雾测试例外保持暂停。空闲时轮询鼠标位置（80 ms），经独立悬停代次预识别当前前台应用目标，允许尚未聚焦。同位置每 600 ms 刷新，按下时复用位置一致、未超过 1 秒且应用／窗口一致的已知结果；宽松模式也可复用未知结果，明确不可输入的缓存拒绝触发。设置／暂停／停止清除缓存，迟到结果不能启动会话。

候选手势在长按阈值前一个探测预算（至少 80ms）开始焦点复核。严格模式要求明确可编辑；默认宽松模式允许未知和软超时，明确不可输入仍拒绝。按下时记录前台应用与窗口，未知和缓存不得跨窗口复用。后台应用仍需要点击成为前台。

Idle → Candidate → 第二次目标复核 → Activating → Holding → AwaitRelease / Idle。

原始 down 立即传给应用。静止达到阈值并确认编辑目标后，默认补发一次鼠标 up，终止原应用拖选，再保持快捷键。最后一个真实 up 被消费，防止重复配对。取消状态等到物理松开才允许下一轮。

Mac 在提前结束鼠标按下后，把后续 leftMouseDragged 变为 mouseMoved。两端都用自有标记排除自身注入的事件。Windows 忽略所有注入鼠标事件作为手势来源；Mac 当前只排除自身标记，需要继续评估其他自动化工具的事件来源。

Candidate 内任何一次位移越界都会取消，即使回到起点。系统多击不启动手势。触发前的第二次复核要求应用、窗口及控件身份一致。

Holding 内检查物理松开、最长保持和前台应用／窗口；已配置的输入法浮窗可以例外。每 500ms 检查原窗口的焦点编辑器，避免初始鼠标坐标随布局变化失效；明确不可输入或两次已知编辑器的身份变化时结束。严格模式查询失败／超时结束，宽松模式允许未知／超时继续，因此无法保证未知目标在同一窗口内的控件身份。

## 并发

Hook / Event Tap 不访问 UIA、不写磁盘、不等待 IPC，只标记和投递事件。状态机由单读取队列串行调用。探测完成带 Generation；旧结果丢弃。设置变更先释放活动会话。

鼠标生产端仅投递第一次位移越界事件，保留越界后移回的取消语义；Tick 最多等待一个，执行时读取最新单调时钟。事件队列仍无界，按钮及取消事件不会丢弃。循环异常会尝试紧急释放并暂停。队列压力的运行指标仍需验证。

## 释放

输入发送前向恢复子进程登记可能按下的键，收到确认后发送。正常释放成功后清除登记。输入注入部分成功时仍按全登记列表尝试释放。

开始快捷键与切换／独立结束快捷键发送前，检查实体修饰键及本次快捷键的每个按键；已经按下则拒绝注入。结束冲突会明确提示在原语音软件中结束录音。保持模式正常释放自身已登记按键，不使用此检查阻止 key-up。检查与实际注入之间仍存在系统事件竞争窗口，不能视为实体按键所有权隔离。

恢复进程只处理键释放，不负责语音状态；hold 最适合，toggle 的主程序崩溃可能留下录音状态。用户同时按相同实体键会产生所有权竞争，仍需真实设备覆盖。Windows 的 UIPI 及 macOS 权限撤销会限制事件发送。

Mac 发送前先构造并校验完整事件序列。key-up 逆序释放，修饰键标记由尚未释放的键重新计算，左右 Command 共用标记时不会在第一个松开事件上提前清除。依据 [Apple CGEventFlags](https://developer.apple.com/documentation/coregraphics/cgeventflags) 对事件发生时修饰键状态的定义。纯托管测试可在 Windows 运行；Mac 原生测试还构造同一序列的 CGEvent 并检查字段，但不向系统输入流发送。

恢复进程失联会通过平台故障事件暂停引擎并尝试释放活动会话。故障后的旧引擎不接受重新启用配置；界面提供“重新启用长按”，操作时销毁旧监听和恢复进程并创建新实例。旧实例迟到的界面状态被忽略。此路径已有模型和界面生命周期测试，真实输入法故障恢复仍需实机验证。

## 隐私

识别只获取控件角色、可编辑属性、进程／窗口及控件短期标识。不调用 ValuePattern.Value 或 TextPattern.GetText。日志不写正文、窗口标题、网页地址或输入事件内容。

配置是声明式数据，不加载任意脚本／DLL。导入采用未知字段拒绝策略和固定按键集合。

## 系统转换

Windows 订阅 SystemEvents 的 SessionSwitch、SessionEnding 和 PowerModeChanged；锁定、断开、结束及 Suspend 产生平台中断，Unlock／Connect／Resume 发出恢复请求。Mac 观察 WillSleep、SessionDidResignActive、DidWake 和 SessionDidBecomeActive；Event Tap 停用也请求重建。中断暂停并尝试结束活动会话；界面仅恢复中断前启用的实例，主动暂停、录入按键、诊断暂停和退出会取消自动恢复意图。恢复通知合并，延后重建，失败最多尝试 3 次；旧实例回调按来源过滤。

Windows HookWatchdog 每 2 秒发起一项鼠标标记检查：指向当前像素的绝对移动标记，不点击、不发送键；正常由自身 LL 回调消费。相对零位移在本机没有可靠产生回调，因此不用它作为健康凭据。连续两次 500ms 内未收到确认才请求重建；安全桌面跳过，SendInput 返回失败不据此判定钩子被移除。健康检测不能证明所有游戏／权限环境支持注入。重建销毁旧钩子和恢复进程并创建新实例，不修改注册表钩子超时、不提升进程权限。

Windows 通知测试只向自身隐藏窗口投递模拟消息；钩子测试安装和移除自己的鼠标钩子，并消费专用健康标记，不切换前台或发送语音开始键。Mac 原生测试只在自身 workspace 通知中心发布模拟通知。测试不触发 OS 锁屏／休眠、不操作游戏。真实锁屏、休眠、Mac 权限和游戏边界仍待用户实测。

参考 [微软 SystemEvents 源码](https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Win32.SystemEvents/src/Microsoft/Win32/SystemEvents.cs)和 [Apple 会话切出通知](https://developer.apple.com/documentation/appkit/nsworkspace/sessiondidresignactivenotification)。

## 编辑控件兼容依据

应用选择器的枚举与输入框识别独立。Windows 只检查当前会话进程是否有主窗口并取得进程名；Mac 使用 [NSWorkspace.runningApplications](https://developer.apple.com/documentation/appkit/nsworkspace/runningapplications) 的原子快照，取得应用名与 Bundle ID，排除无界面的禁止激活应用。列表按需刷新，不持续监听应用启停，不读取窗口标题或命令行。选择应用不能跳过可编辑性检测。

Windows 的 Edit／Document 可通过可写 ValuePattern 或 TextPattern 的 IsReadOnly=false 确认。只读、混合属性和不支持属性不作为可编辑依据；整页 Document 的存在本身不能证明可输入。见 [微软 IsReadOnlyAttribute 文档](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.textpattern.isreadonlyattribute?view=netframework-4.8.1)。

Mac 在命中测试前询问前台应用是否声明 AXManualAccessibility 可写；只对声明此接口的应用设置 true，以请求其暴露辅助功能树。不会修改未声明该属性的原生应用，也不在退出时关闭其他辅助技术正在使用的树。第一次请求后若树尚未出现，返回 accessibility_enabled_retry；不跳过编辑目标复核。见 [Electron 官方辅助功能文档](https://github.com/electron/electron/blob/main/docs/tutorial/accessibility.md)。此适配仍须 Mac 实机验证。
