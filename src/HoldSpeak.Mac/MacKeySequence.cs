using HoldSpeak.Core;

namespace HoldSpeak.Mac;

internal readonly record struct MacKeyStroke(Key Key, ushort Code, bool Down, ulong Flags, bool IsModifier);

internal static class MacKeySequence
{
    public static MacKeyStroke[] Build(Key[] keys, bool down)
    {
        if (keys.Length > 5 || keys.Distinct().Count() != keys.Length)
            throw new InvalidOperationException("Mac 快捷键过长或包含重复按键。");
        // Validate the full sequence before any native event can be posted.
        var mapped = keys.Select(key =>
        {
            if (!KeyCatalog.TryMac(key, out ushort code, out ulong flag))
                throw new InvalidOperationException("不支持的 macOS 按键。");
            return (Key: key, Code: code, Flag: flag);
        }).ToArray();
        var events = new MacKeyStroke[keys.Length];
        for (int step = 0; step < events.Length; step++)
        {
            int index = down ? step : events.Length - 1 - step;
            var current = mapped[index];
            // Both Command keys share one modifier mask. Recompute from the
            // keys still held rather than clearing the current key's mask.
            ulong flags = mapped.Take(down ? index + 1 : index).Aggregate(0UL, (value, key) => value | key.Flag);
            events[step] = new(current.Key, current.Code, down, flags, current.Flag != 0);
        }
        return events;
    }
}
