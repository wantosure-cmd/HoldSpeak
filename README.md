# 左键说 · HoldSpeak

在任意位置长按鼠标左键，用已有语音软件的快捷键开始说话；松开鼠标结束。文字落在哪里，由原语音输入法和当前键盘焦点决定。

Windows / macOS，C# + .NET 10 + Avalonia，MIT 开源。

## 当前状态

这是开发中的 0.1.18 alpha，尚未完成跨平台发布验收。

许可采用 [MIT](LICENSE)：允许使用、修改、商用和分发，分发软件副本或实质性部分时必须保留原版权声明和许可声明。项目来源为“左键说 / HoldSpeak”，推荐在文档或关于页面注明来源。MIT 本身不强制界面显示项目名或链接；详见 [来源标注说明](ATTRIBUTION.md)。

长按时间滑块调整后立即保存并生效，暂停／启用和重启均保留。300ms 是本工具发送快捷键前的等待时间，不是语音输入法窗口显示的总时间。全局模式的诊断报告记录 `triggerLatencyMs`（按下到快捷键发送完成）和 `dispatchLatencyMs`（快捷键发送过程，包含恢复进程确认），用于定位额外等待；不记录语音内容。

新版采用 Ant Design 设计语言的浅色桌面界面与原创鼠标左键 Logo，设计说明见 [docs/design.md](docs/design.md)。现有 Avalonia 原生架构保持兼容。

- 已实现长按、移动取消、双击保护、松开结束和最长保持时间；目标确认与焦点切换结束仅用于可选的旧检测模式。
- 已实现保持、切换、分别开始／结束三种快捷键协议，以及多个语音配置、应用禁用／配置覆盖。
- Windows：Win32 Hook、SendInput、UI Automation；macOS：Event Tap、CGEvent、AX Accessibility。
- 输入框探测隔离到子进程；恢复进程在主程序失去心跳或退出后尝试释放登记的键。
- 恢复进程失联会暂停长按；状态区圆钮开关可重新启用并重建平台监听和恢复进程。绿色为开启，灰色为暂停。
- 锁定、断开、休眠时暂停并尝试释放；解锁／唤醒／会话返回时重建原来启用的监听。主动暂停不会自动开启。Windows 鼠标钩子健康检查失效也会请求重建；Mac 接入会话返回和 Event Tap 停用恢复请求。
- 正常启动自动启用全局长按，不判断当前位置是否为输入框，也不检查密码／只读／按钮；启动前与说话期间不做目标复核，不因切换焦点／应用／窗口中断。
- 默认 `triggerAnywhere: true`，无需手动调整。旧的输入框检测代码保留供调试兼容使用；仅在高级 JSON 明确设置为 false 时启用，宽松与严格规则仅作用于旧检测模式。
- 不录音、不调用识别 API、不读取输入正文、不发送遥测。语音识别和结果回填由原语音软件负责。

**Mac 代码可构建，不等于已在 Mac 上验证。** 用户已确认当前 Chrome 和 Codex 输入框能调用微信输入法语音；随后报告 Codex 持续说话时目标中断。0.1.12 修复持续复核依赖初始鼠标位置的问题，具体中断仍待新版实测。其他应用与输入位置需逐项验收，不能保证覆盖所有自绘、富文本或远程输入框。

## 开发运行

需要 .NET 10 SDK。

```sh
dotnet run --project src/HoldSpeak.App/HoldSpeak.App.csproj
```

Windows 默认配置为保持 `LCtrl + LWin`。首次运行检测到微信输入法时显示其配置名称。Mac 默认不设置快捷键，必须填写原语音软件实际支持的入口；不直接将 Win 键映射为 Command。

在“语音软件”页修改名称、协议和快捷键，或另存为多个配置。在“常规”选择当前配置并保存。“应用规则”直接设置应用启用和语音配置覆盖。“高级配置”支持 JSON 校验、导入和导出。

快捷键用逗号分隔，例如 `LCtrl,LWin`、`LCtrl,1` 或 `Command,A`。支持 A–Z、0–9、空格、回车、Tab、方向与导航键；Windows 支持 F1–F24，Mac 支持 F1–F20。Mac 字母按 US 物理键位发送；Fn 暂不支持。保存时保留上一份有效配置为 `settings.json.bak`，主配置损坏时加载备份并提示，原文件不会被自动覆盖。

“诊断”的输入框检查会暂停长按，给你 3 秒点击目标输入框，再显示可编辑判断、应用标识和拒绝原因。检查不发送语音快捷键；结束后需要主动重新启用。

出现提前结束或无法触发时，可在“诊断”点击“导出诊断报告”，保留最近 80 条状态、应用标识、控件类型和实际探测失败原因。报告不包含状态消息全文、配置名称、输入正文、网址、窗口标题或原始窗口／控件 ID。导出只保存到你选择的本地文件，不自动发送。

在“语音软件”页可点击当前平台的“录入”按钮。先松开所有键，再按一个组合键并全部松开，然后点击“保存语音配置”。录入会暂停长按，不自动恢复；Esc、切页、失去焦点或 10 秒超时会取消并保留原值。这里只接收设置窗口的键盘事件，不安装全局键盘记录器；原语音软件仍可能响应自己的全局快捷键。其他平台的键可手动填写；Mac 采用物理键位，当前不支持的右侧 Control／Option／Shift 不会自动替换成左侧键。

在“应用规则”点击“刷新应用列表”，选择当前运行的应用会填入规则标识，点击保存后生效。Windows 列出当前会话有主窗口的进程；Mac 列出具有 Bundle ID 的普通／附件应用。列表只是刷新时的快照，部分后台进程或自定义宿主不会列出，可通过诊断或手动填写补充。枚举只读取应用名、进程名或 Bundle ID，不读取窗口标题、命令行、网址或正文，不写入诊断报告。

Windows 应用标识使用进程名（例如 `chrome.exe`）；Mac 使用 Bundle ID。应用规则决定该应用中是否启用，以及使用哪个语音配置；默认全局模式不检查输入框类型。

配置默认写入用户应用数据目录下的 `HoldSpeak/settings.json`。开发或便携运行可设置 `HOLDSPEAK_CONFIG_DIR` 指向专用目录。该目录还保存单实例锁；启动第二实例返回退出码 2。

macOS 需要辅助功能及输入监控权限；设置后重新启动。Mac 发布包使用固定 Bundle ID `org.holdspeak.app`。未签名开发包的权限稳定性和正式签名包应分别测试。

Mac 适配会为明确声明 `AXManualAccessibility` 可写的应用启用辅助功能树（Electron 官方接口）。首次启用后控件树可能异步出现，诊断可提示 `accessibility_enabled_retry`，此时重新检查或再次长按。此路径尚未实机验收。

## 测试

```sh
dotnet run --project tests/HoldSpeak.Core.Tests/HoldSpeak.Core.Tests.csproj
dotnet run --project tests/HoldSpeak.UI.Tests/HoldSpeak.UI.Tests.csproj
```

UI 测试在无界面渲染环境检查设置界面，截图输出到 `artifacts/ui/`。它不能证明操作系统 Hook、权限或真实语音录音有效。

Windows 的真实桌面控件测试：

```powershell
dotnet run --project tests/HoldSpeak.Windows.Tests/HoldSpeak.Windows.Tests.csproj
```

需要交互式桌面；测试会短暂显示普通、只读和密码输入框，不发送语音开始组合键。无桌面沙箱中无法验证前台焦点。

浏览器识别测试使用独立配置和本地固定网页，覆盖地址栏、普通／多行／富文本、只读、密码、按钮以及鼠标位置与焦点不一致；不会发送语音快捷键，也不操作现有标签页。需要交互式 Windows 桌面：

```powershell
dotnet run --project tests/HoldSpeak.Browser.Tests/HoldSpeak.Browser.Tests.csproj
# 可传入 Edge 或另一安装位置的 Chromium 浏览器路径
dotnet run --project tests/HoldSpeak.Browser.Tests/HoldSpeak.Browser.Tests.csproj -- 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
```

结果与独立测试配置位于 `artifacts/browser-tests/`，不随软件包分发。识别通过不能证明语音录音和回填有效。

Mac 原生接口测试（必须在真实 macOS 运行）：

仅检查 Mac 按键事件规划时，可在 Windows 或 Mac 使用 `--managed-only`，此模式不加载 macOS 原生框架：

```sh
dotnet run --project tests/HoldSpeak.Mac.Tests/HoldSpeak.Mac.Tests.csproj -- --managed-only
```

完整原生测试：

```sh
dotnet run --project tests/HoldSpeak.Mac.Tests/HoldSpeak.Mac.Tests.csproj
```

该测试在本进程的 NSWorkspace 通知中心发送模拟休眠／会话切出通知，并构造键盘事件检查原生字段；不让系统休眠、不向系统输入流发送按键。它不能替代真实 Event Tap、权限、锁屏、录音与回填验收。已接入 Mac CI，但远程 CI 尚未实际执行，本机只有编译证据。

## 发布

```powershell
./scripts/publish-windows.ps1
```

```sh
bash scripts/publish-macos.sh osx-arm64
# Intel Mac: bash scripts/publish-macos.sh osx-x64
```

构建输出位于 `artifacts/`。macOS 脚本需要在 Mac 上运行，输出 `.app` 开发包；正式分发前需要签名、公证及真实设备验收。

Windows 包可双击 `Launch-HoldSpeak.cmd` 以便携模式运行，配置保存在同目录 `data/`；直接运行 `HoldSpeak.App.exe` 则使用用户应用数据目录。正常启动自动启用；权限不足、监听冲突或平台故障时显示原因，不会假装已经启用。

从不同目录启动的新版共用当前用户的输入监听锁，防止重复发送快捷键。已启用过监听的实例即使暂停也保留锁，需通过“完全退出”释放；其他实例启用时会提示冲突。锁文件保存在用户本地应用数据目录的 `HoldSpeak/runtime/`，不随便携配置迁移，不需要删除残留文件。升级前请完全退出旧版本：0.1.6 及更早版本尚未接入此锁。

关闭设置窗口会隐藏到托盘／菜单栏并继续运行；通过托盘“退出”或诊断页“完全退出”结束工具。

构建脚本关闭 .NET / Avalonia 的构建期遥测；应用运行没有遥测模块。

## 使用边界

- 多个语音软件监听同一快捷键时，工具不能定向只启动一个；请在原软件里配置不同入口。
- 严格模式只允许明确可编辑的辅助功能节点；默认宽松模式允许未知区域，但明确不可输入时仍拒绝。专用应用适配仍在开发。
- 密码框明确排除。控件提供者如果隐瞒安全属性，通用识别无法推断其真实内容。
- 保持模式的释放最明确；切换模式可能与用户在原软件中的手动录音操作失去同步。
- 界面显示“快捷键保持中”只证明发送动作，不证明原软件已开始录音。
- 恢复进程仅释放登记的键；不能可靠关闭已启动的切换式录音。进程同时被结束、安全桌面、权限撤销等情况下不能保证释放。
- 真实键与注入键共享系统状态。活动期间尽量避免操作键盘；真实按键会取消本次手势。
- 识别结果异步回填的位置由原软件决定；工具不会抢回焦点或重新粘贴文字。
- 管理员应用、远程桌面、虚拟机和安全桌面尚不作为已支持场景。
- 系统通知和恢复进程的释放是尽力处理；通知到达过晚、安全桌面或系统冻结时，不能保证及时释放。Windows 健康检查使用自身消费的鼠标标记，连续缺失才请求重建；输入被游戏／权限拦截仍不能保证有效。真实锁屏、具体游戏和 Mac 的通知覆盖需要实测。

## 项目文档

- [架构与事件约束](docs/architecture.md)
- [兼容性与验收](docs/compatibility.md)
- [后续开发路线](docs/roadmap.md)
- [贡献指南](CONTRIBUTING.md)

## English

HoldSpeak maps a stationary left-mouse hold in an editable field to an existing voice-input application's shortcut. Releasing the mouse ends the shortcut session. It does not record audio, transcribe speech, or insert the result itself.

The shared C# state machine runs behind platform-specific Windows UI Automation / Win32 and macOS Accessibility / Core Graphics adapters. This is an alpha: macOS runtime behavior and named third-party applications still require real-device verification. Unknown fields are rejected rather than guessed. See the documentation for build commands, configuration and limitations.
