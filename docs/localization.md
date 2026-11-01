# 界面翻译 / Interface translations

在“常规设置 → 界面语言”选择语言，完全退出并重新打开后生效。语言保存在配置目录的 `language.json`，与 `settings.json` 独立；不改变语音识别语言。

Choose a language in General → Interface language, then quit and reopen HoldSpeak. The preference is saved in `language.json` next to the settings file. Recognition languages are controlled by the voice software.

## 贡献翻译

翻译资源在 `src/HoldSpeak.App/Translations.json`，键是简体中文原文，值依次为英文、日文、韩文。`{0}` 等占位符必须与原文一致；保留应用名、快捷键标识和用户输入。

新增语言时，在 `Localization.cs` 的 `Languages` 添加语言代码和本族语名称，为所有资源添加对应译文，并扩展 `Translate` 中的语言索引。测试会检查资源完整性、占位符与各语言界面。

Translation keys are Simplified Chinese source text; values are English, Japanese and Korean in that order. Preserve numbered placeholders such as `{0}`, app identifiers and key names. To add a language, register its code and native name in `Localization.cs`, extend the language index, and supply every translation. Run the UI tests to verify resources and layouts.

引擎／平台状态只在显示时翻译；配置名称、原始诊断和导入导出的 JSON 不翻译。未知系统异常保留原文，以免丢失排查信息。
