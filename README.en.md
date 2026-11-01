# HoldSpeak

[简体中文](README.md) | English

## What it does

Holding the left mouse button to speak can be more convenient than repeatedly reaching for combinations such as Ctrl+Alt. HoldSpeak maps a left-button hold to the shortcut of your existing voice-input software: hold to speak and release to finish. You can configure shortcuts for different input methods or always use one fixed shortcut.

HoldSpeak sends the shortcut; your chosen voice software handles recording, recognition and text insertion. Configure HoldSpeak to match the shortcut and activation mode supported by that software.

## Install and use

1. Download the Windows portable ZIP from [Releases](https://github.com/wantosure-cmd/HoldSpeak/releases), extract it and run `HoldSpeak.App.exe`.
2. Under **语音快捷键** (Voice shortcuts), select a fixed shortcut or follow the current input method. Set the shortcut to match your voice software and save.
3. Click where you want to type, hold the left mouse button while speaking, and release to finish.
4. To prevent accidental activation in a game, select it under **应用规则** (App rules) and add it to the blocklist. Entries can be removed directly from the list.
5. Closing the window hides it in the system tray. Use the tray menu to quit; enable login startup in **常规设置** (General settings) if desired.

The screenshots use test settings. The current application interface is in Chinese.

![General settings](docs/images/general-zh.png)
![Voice shortcuts](docs/images/shortcuts-zh.png)
![App blocklist](docs/images/blacklist-zh.png)

[Contributing](CONTRIBUTING.md) · [Compatibility and validation](docs/compatibility.md) · [Changelog](CHANGELOG.md) · [MIT License](LICENSE)