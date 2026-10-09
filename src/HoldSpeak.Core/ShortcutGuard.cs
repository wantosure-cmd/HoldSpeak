namespace HoldSpeak.Core;

public static class ShortcutGuard
{
    // Synthetic key-up must never take ownership of a key that was already down.
    public static void EnsureAvailable(Key[] keys, bool modifiersDown, Func<Key, bool> isDown, bool ending)
    {
        if (modifiersDown || keys.Any(isDown))
            throw new InvalidOperationException(ending
                ? "结束快捷键与实体按键冲突，请在原软件中结束录音。"
                : "快捷键或键盘修饰键已按下，请先松开。");
    }
}
