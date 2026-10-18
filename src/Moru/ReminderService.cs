using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;

namespace Moru;

internal sealed record Reminder(Guid Id, string Title, DateTimeOffset DueAt)
{
    public string When => DueAt.ToLocalTime().ToString("M월 d일 (ddd) HH:mm", CultureInfo.GetCultureInfo("ko-KR"));
}

internal static class ReminderRules
{
    internal static bool TryCreateAfter(string minutes, DateTimeOffset now, out Reminder? reminder)
    {
        reminder = null;
        if (!int.TryParse(minutes.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value is < 1 or > 1440) return false;
        reminder = new(Guid.NewGuid(), $"{value}분 타이머", now.AddMinutes(value));
        return true;
    }

    internal static Reminder[] Due(IEnumerable<Reminder> reminders, DateTimeOffset now) =>
        reminders.Where(r => r.DueAt <= now).OrderBy(r => r.DueAt).ToArray();

    internal static bool TryCreate(DateTime? date, string time, string title, DateTimeOffset now, out Reminder? reminder)
    {
        reminder = null;
        if (date is null || !TimeOnly.TryParseExact(time.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock)) return false;
        var local = DateTime.SpecifyKind(date.Value.Date.Add(clock.ToTimeSpan()), DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local)) return false;
        var due = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        if (due <= now || due > now.AddYears(1)) return false;
        reminder = new(Guid.NewGuid(), string.IsNullOrWhiteSpace(title) ? "잠깐 쉬어 가요" : title.Trim()[..Math.Min(40, title.Trim().Length)], due);
        return true;
    }
}

// A single timer sleeps until the next alarm (max 1 minute to handle clock changes).
// No alarms means no background clock polling. The app never wakes a sleeping PC.
internal sealed class ReminderService : IDisposable
{
    private readonly DispatcherTimer timer;
    private readonly Func<IReadOnlyList<Reminder>> read;
    private readonly Action<Reminder[]> deliver;
    internal ReminderService(Dispatcher dispatcher, Func<IReadOnlyList<Reminder>> read, Action<Reminder[]> deliver)
    {
        this.read = read; this.deliver = deliver;
        timer = new(DispatcherPriority.Background, dispatcher);
        timer.Tick += (_, _) => Check();
    }
    internal void Schedule()
    {
        timer.Stop();
        if (read().Count == 0) return;
        timer.Interval = TimeSpan.FromSeconds(Math.Clamp((read().Min(r => r.DueAt) - DateTimeOffset.Now).TotalSeconds, 0.1, 60));
        timer.Start();
    }
    internal void Check()
    {
        timer.Stop();
        var due = ReminderRules.Due(read(), DateTimeOffset.Now);
        if (due.Length > 0) deliver(due);
        Schedule();
    }
    internal void Pause() => timer.Stop();
    public void Dispose() => timer.Stop();
}
