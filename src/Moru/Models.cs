using System;

namespace Moru;

internal sealed record Preferences
{
    public string SkinId { get; init; } = "sprout";
    public int RefreshSeconds { get; init; } = 2;
    public bool AlwaysOnTop { get; init; } = true;
    public bool AnimationEnabled { get; init; } = true;
    public bool CodexEnabled { get; init; }
    public bool ClaudeEnabled { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }

    public Preferences Normalize() => this with
    {
        SkinId = string.IsNullOrWhiteSpace(SkinId) ? "sprout" : SkinId,
        RefreshSeconds = RefreshSeconds is 2 or 5 or 10 ? RefreshSeconds : 2,
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
