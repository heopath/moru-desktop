using Moru;
using System.IO;
using System.Windows.Media;

internal static class UtilityChecks
{
    internal static void Run(string scratch, Action<string, bool> check)
    {
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var past = new Reminder(Guid.NewGuid(), "지난 알람", now.AddMinutes(-1));
        var due = new Reminder(Guid.NewGuid(), "정시 알람", now);
        var future = new Reminder(Guid.NewGuid(), "다음 알람", now.AddMinutes(5));
        check("Alarms include exact boundary and overdue reminders", ReminderRules.Due(new[] { future, due, past }, now).SequenceEqual(new[] { past, due }));
        check("No future alarms fire early", ReminderRules.Due(new[] { future }, now).Length == 0);
        var remaining = new[] { past, due, future }.Except(ReminderRules.Due(new[] { past, due, future }, now)).ToArray();
        check("Delivered alarms do not repeat", ReminderRules.Due(remaining, now).Length == 0);
        check("Invalid clock rejected", !ReminderRules.TryCreate(DateTime.Today.AddDays(1), "25:90", "a", DateTimeOffset.Now, out _));
        check("Past clock rejected", !ReminderRules.TryCreate(DateTime.Today.AddDays(-1), "12:00", "a", DateTimeOffset.Now, out _));
        check("Missing date rejected", !ReminderRules.TryCreate(null, "12:00", "a", DateTimeOffset.Now, out _));
        check("Ten-minute timer keeps seconds", ReminderRules.TryCreateAfter("10", now.AddSeconds(23), out var ten) && ten!.DueAt == now.AddMinutes(10).AddSeconds(23) && ten.Title == "10분 타이머");
        check("Fifteen-minute timer crosses midnight", ReminderRules.TryCreateAfter("15", now.Date.AddDays(1).AddMinutes(-5), out var fifteen) && fifteen!.DueAt.Minute == 10 && fifteen.DueAt.Hour == 0);
        check("Custom minute timer accepts one and 1440", ReminderRules.TryCreateAfter("1", now, out _) && ReminderRules.TryCreateAfter("1440", now, out var day) && day!.DueAt == now.AddDays(1));
        check("Invalid duration input rejected", new[] { "", "0", "-1", "1.5", "1e2", "15분", "1441", "999999999999" }.All(s => !ReminderRules.TryCreateAfter(s, now, out _)));
        check("Timer never fires before its due instant", ReminderRules.Due(new[] { ten! }, ten!.DueAt.AddMilliseconds(-1)).Length == 0 && ReminderRules.Due(new[] { ten }, ten.DueAt).Length == 1);
        check("Repeat timer stores its chosen interval", ReminderRules.TryCreateAfter("15", now, out var repeating, repeat: true) && repeating!.RepeatMinutes == 15 && repeating.RepeatLabel == "15분마다 반복");
        var firstRepeat = repeating!;
        var nextRepeat = ReminderRules.AfterDelivery(new[] { firstRepeat }, firstRepeat.DueAt).Single();
        check("Repeating alarm retains identity and advances one period", nextRepeat.Id == firstRepeat.Id && nextRepeat.DueAt == now.AddMinutes(30) && nextRepeat.RepeatMinutes == 15);
        check("Repeat cannot fire twice at the same boundary", ReminderRules.Due(new[] { nextRepeat }, firstRepeat.DueAt).Length == 0);
        var resumed = ReminderRules.AfterDelivery(new[] { firstRepeat }, now.AddDays(2).AddMinutes(7)).Single();
        check("Missed repeats advance once to the next original boundary", resumed.DueAt == now.AddDays(2).AddMinutes(15) && resumed.Id == firstRepeat.Id);
        check("Early check leaves repeat unchanged", ReminderRules.AfterDelivery(new[] { firstRepeat }, now).Single() == firstRepeat);
        var mixed = ReminderRules.AfterDelivery(new[] { past, due, future, firstRepeat }, now);
        check("Delivery removes one-shot alarms and preserves future alarms", mixed.SequenceEqual(new[] { future, firstRepeat }));
        check("Countdown rounds upward instead of showing zero early", ReminderRules.Remaining(due, now.AddMilliseconds(-100)) == "00분 01초 남음");
        check("Countdown is nonnegative for due and overdue alarms", ReminderRules.Remaining(due, now) == "곧 울려요" && ReminderRules.Remaining(past, now) == "곧 울려요");
        check("Countdown supports hours and seconds", ReminderRules.Remaining(due with { DueAt = now.AddHours(1).AddSeconds(2) }, now) == "1시간 00분 02초 남음");
        check("Snoozing repeat updates one slot without creating a second chain", ReminderRules.TrySnooze(new[] { nextRepeat }, new[] { firstRepeat }, now, out var snoozed) && snoozed.Length == 1 && snoozed[0].Id == firstRepeat.Id && snoozed[0].DueAt == now.AddMinutes(5) && snoozed[0].RepeatMinutes == 15);
        check("Snoozed repeat resumes from its postponed occurrence", ReminderRules.AfterDelivery(snoozed, now.AddMinutes(5)).Single().DueAt == now.AddMinutes(20));
        check("Deleted repeat is not recreated from a stale notification", ReminderRules.TrySnooze(Array.Empty<Reminder>(), new[] { firstRepeat }, now, out var removedRepeat) && removedRepeat.Length == 0);
        var fullQueue = Enumerable.Range(0, 7).Select(_ => future with { Id = Guid.NewGuid() }).Append(nextRepeat).ToArray();
        check("Repeat snooze works even with eight scheduled alarms", ReminderRules.TrySnooze(fullQueue, new[] { firstRepeat }, now, out var fullSnooze) && fullSnooze.Length == 8);
        check("Snooze overflow leaves all scheduled times untouched", !ReminderRules.TrySnooze(fullQueue, new[] { firstRepeat, due }, now, out var rejectedSnooze) && rejectedSnooze.SequenceEqual(fullQueue));
        check("One-shot snooze remains one-shot", ReminderRules.TrySnooze(Array.Empty<Reminder>(), new[] { due }, now, out var onceSnooze) && onceSnooze.Length == 1 && !onceSnooze[0].IsRepeating && onceSnooze[0].Id != due.Id && onceSnooze[0].DueAt == now.AddMinutes(5));
        var tone = AlarmTone.CreateWave();
        check("Alarm chime is bounded 2-second mono PCM", System.Text.Encoding.ASCII.GetString(tone, 0, 4) == "RIFF" && BitConverter.ToInt32(tone, 24) == 22050 && BitConverter.ToInt16(tone, 22) == 1 && BitConverter.ToInt16(tone, 34) == 16 && tone.Length == 88244 && BitConverter.ToInt32(tone, 40) == 88200);
        check("Alarm chime contains signal and ends silently", tone.Skip(44).Any(b => b != 0) && tone.TakeLast(1000).All(b => b == 0));
        check("Future clock is parsed and empty titles get a default", ReminderRules.TryCreate(DateTime.Today.AddDays(1), "12:30", " ", DateTimeOffset.Now, out var created) && created!.Title == "잠깐 쉬어 가요");
        var prefs = new Preferences { DarkMode = true, TextSize = 20, HoverEnabled = false, AlarmSound = false, RepeatTimer = true, Reminders = new[] { future, nextRepeat } };
        var store = new SettingsStore(Path.Combine(scratch, "utilities"));
        check("Utility preferences save", store.Save(prefs));
        var loaded = store.Load();
        check("Theme, text size and reminders survive restart", loaded.DarkMode && loaded.TextSize == 20 && !loaded.HoverEnabled && !loaded.AlarmSound && loaded.Reminders.SequenceEqual(prefs.Reminders));
        check("Repeat option, identity, interval and next occurrence survive restart", loaded.RepeatTimer && loaded.Reminders.Single(r => r.Id == nextRepeat.Id) == nextRepeat);
        check("Invalid repeat intervals normalize to one-shot", new[] { -1, 1441, int.MaxValue }.All(v => !(prefs with { Reminders = new[] { future with { RepeatMinutes = v } } }).Normalize().Reminders[0].IsRepeating));
        var legacy = System.Text.Json.JsonSerializer.Deserialize<Preferences>("{\"Reminders\":[{\"Id\":\"cced4dd4-e470-49fe-b27f-461599067999\",\"Title\":\"legacy\",\"DueAt\":\"2026-10-18T13:00:00Z\"}]}")!;
        check("Old settings retain one-shot behavior", !legacy.RepeatTimer && !legacy.Normalize().Reminders.Single().IsRepeating);
        check("Invalid font size normalizes", (prefs with { TextSize = int.MaxValue }).Normalize().TextSize == 16);
        check("Alarm queue bounded to eight", (prefs with { Reminders = Enumerable.Range(0, 20).Select(i => future with { Id = Guid.NewGuid() }).ToArray() }).Normalize().Reminders.Count == 8);
        check("Duplicate alarm IDs removed", (prefs with { Reminders = new[] { future, future } }).Normalize().Reminders.Count == 1);
        check("Null alarm arrays recover safely", (prefs with { Reminders = null! }).Normalize().Reminders.Count == 0);
        check("Long alarm titles bounded", (prefs with { Reminders = new[] { future with { Title = new string('a', 100) } } }).Normalize().Reminders[0].Title.Length == 40);
        var app = new App(); app.InitializeComponent();
        var skin = SkinCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Skins"))[0];
        Theme.Apply(app, prefs, skin);
        check("Dark theme applies high contrast text and enlarged type", ((SolidColorBrush)app.Resources["Ink"]).Color == Color.FromRgb(243, 246, 250) && (double)app.Resources["BodySize"] == 20);
        Theme.Apply(app, prefs with { DarkMode = false }, skin);
        check("Theme toggle restores light surfaces", ((SolidColorBrush)app.Resources["Surface"]).Color == Colors.White);
    }
}
