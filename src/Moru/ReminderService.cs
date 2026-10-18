using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;

namespace Moru;

internal sealed record Reminder(Guid Id, string Title, DateTimeOffset DueAt)
{
    public int RepeatMinutes { get; init; }
    public bool IsRepeating => RepeatMinutes is >= 1 and <= 1440;
    public string RepeatLabel => IsRepeating ? $"{RepeatMinutes}분마다 반복" : "한 번 알림";
    public string When => DueAt.ToLocalTime().ToString("M월 d일 (ddd) HH:mm", CultureInfo.GetCultureInfo("ko-KR")) + (IsRepeating ? $" · {RepeatLabel}" : "");
}

internal static class ReminderRules
{
    internal static bool TryCreateAfter(string minutes, DateTimeOffset now, out Reminder? reminder, bool repeat = false)
    {
        reminder = null;
        if (!int.TryParse(minutes.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value is < 1 or > 1440) return false;
        reminder = new(Guid.NewGuid(), $"{value}분 타이머", now.AddMinutes(value)) { RepeatMinutes = repeat ? value : 0 };
        return true;
    }

    internal static Reminder[] Due(IEnumerable<Reminder> reminders, DateTimeOffset now) =>
        reminders.Where(r => r.DueAt <= now).OrderBy(r => r.DueAt).ToArray();

    // Keep the original cadence and skip missed occurrences after sleep/restart.
    // Each repeating alarm keeps its identity and occupies one slot.
    internal static Reminder[] AfterDelivery(IEnumerable<Reminder> reminders, DateTimeOffset now) =>
        reminders.Where(r => r.DueAt > now || r.IsRepeating).Select(r =>
        {
            if (r.DueAt > now) return r;
            long interval = TimeSpan.FromMinutes(r.RepeatMinutes).Ticks;
            long periods = (now - r.DueAt).Ticks / interval + 1;
            return r with { DueAt = r.DueAt.AddTicks(periods * interval) };
        }).ToArray();

    internal static string Remaining(Reminder reminder, DateTimeOffset now)
    {
        long seconds = (long)Math.Ceiling((reminder.DueAt - now).TotalSeconds);
        if (seconds <= 0) return "곧 울려요";
        long hours = seconds / 3600, minutes = seconds % 3600 / 60;
        return hours > 0 ? $"{hours}시간 {minutes:00}분 {seconds % 60:00}초 남음"
            : $"{minutes:00}분 {seconds % 60:00}초 남음";
    }

    internal static bool TrySnooze(IReadOnlyList<Reminder> scheduled, IEnumerable<Reminder> displayed,
        DateTimeOffset now, out Reminder[] result)
    {
        var updated = scheduled.ToList();
        foreach (var reminder in displayed.DistinctBy(r => r.Id))
        {
            if (reminder.IsRepeating)
            {
                int index = updated.FindIndex(r => r.Id == reminder.Id);
                // An already deleted repeat must never be resurrected by an old popup.
                if (index >= 0) updated[index] = updated[index] with { DueAt = now.AddMinutes(5) };
            }
            else updated.Add(reminder with { Id = Guid.NewGuid(), DueAt = now.AddMinutes(5) });
        }
        result = updated.Count <= 8 ? updated.ToArray() : scheduled.ToArray();
        return updated.Count <= 8;
    }

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
    private readonly Action<Reminder[], DateTimeOffset> deliver;
    internal ReminderService(Dispatcher dispatcher, Func<IReadOnlyList<Reminder>> read, Action<Reminder[], DateTimeOffset> deliver)
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
        var now = DateTimeOffset.Now;
        var due = ReminderRules.Due(read(), now);
        if (due.Length > 0) deliver(due, now);
        Schedule();
    }
    internal void Pause() => timer.Stop();
    public void Dispose() => timer.Stop();
}
