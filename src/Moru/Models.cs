using System;
using System.Collections.Generic;
using System.Linq;

namespace Moru;

internal sealed record Preferences
{
    public string SkinId { get; init; } = "sprout";
    public int RefreshSeconds { get; init; } = 2;
    public bool AlwaysOnTop { get; init; } = true;
    public bool AnimationEnabled { get; init; } = true;
    public bool CodexEnabled { get; init; }
    public bool ClaudeEnabled { get; init; }
    public bool DarkMode { get; init; }
    public int TextSize { get; init; } = 16;
    public bool HoverEnabled { get; init; } = true;
    public bool AlarmSound { get; init; } = true;
    public bool RepeatTimer { get; init; }
    public bool DesktopShortcutCreated { get; init; }
    public IReadOnlyList<Reminder> Reminders { get; init; } = Array.Empty<Reminder>();
    public int? X { get; init; }
    public int? Y { get; init; }

    public Preferences Normalize() => this with
    {
        SkinId = string.IsNullOrWhiteSpace(SkinId) ? "sprout" : SkinId,
        RefreshSeconds = RefreshSeconds is 2 or 5 or 10 ? RefreshSeconds : 2,
        TextSize = TextSize is 16 or 18 or 20 ? TextSize : 16,
        Reminders = (Reminders ?? Array.Empty<Reminder>()).Where(r => r is not null && r.Id != Guid.Empty && r.DueAt.Year is >= 2020 and <= 2100)
            .DistinctBy(r => r.Id).Take(8).Select(r => r with {
                Title = string.IsNullOrWhiteSpace(r.Title) ? "잠깐 쉬어 가요" : r.Title.Trim()[..Math.Min(40, r.Title.Trim().Length)],
                RepeatMinutes = r.IsRepeating ? r.RepeatMinutes : 0
            }).ToArray(),
        X = X is > -100000 and < 100000 ? X : null,
        Y = Y is > -100000 and < 100000 ? Y : null
    };
}

internal readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);
internal sealed record Metrics(double? CpuPercent, ulong TotalMemory, ulong AvailableMemory)
{
    public ulong UsedMemory => TotalMemory - Math.Min(TotalMemory, AvailableMemory);
    public double MemoryPercent => TotalMemory == 0 ? 0 : UsedMemory * 100d / TotalMemory;
}

internal static class CpuMath
{
    // Windows kernel time includes idle time. Both counters must be sampled over an interval.
    public static double? Usage(CpuTimes previous, CpuTimes current)
    {
        if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User)
            return null;
        double total = (current.Kernel - previous.Kernel) + (double)(current.User - previous.User);
        double idle = current.Idle - previous.Idle;
        if (total <= 0 || idle > total) return null;
        return Math.Clamp((total - idle) * 100d / total, 0, 100);
    }
}
