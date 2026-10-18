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
        check("Future clock is parsed and empty titles get a default", ReminderRules.TryCreate(DateTime.Today.AddDays(1), "12:30", " ", DateTimeOffset.Now, out var created) && created!.Title == "잠깐 쉬어 가요");
        var prefs = new Preferences { DarkMode = true, TextSize = 20, HoverEnabled = false, AlarmSound = false, Reminders = new[] { future } };
        var store = new SettingsStore(Path.Combine(scratch, "utilities"));
        check("Utility preferences save", store.Save(prefs));
        var loaded = store.Load();
        check("Theme, text size and reminders survive restart", loaded.DarkMode && loaded.TextSize == 20 && !loaded.HoverEnabled && !loaded.AlarmSound && loaded.Reminders.SequenceEqual(prefs.Reminders));
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
