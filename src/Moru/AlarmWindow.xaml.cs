using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Moru;

public partial class AlarmWindow : Window
{
    private readonly App host;
    private readonly List<Reminder> reminders = new();
    internal AlarmWindow(App app)
    {
        host = app; InitializeComponent();
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 24);
        Closing += (_, e) => { if (!host.Exiting) { e.Cancel = true; host.StopAlarmSound(); reminders.Clear(); Hide(); } };
    }
    internal void Deliver(IEnumerable<Reminder> due)
    {
        foreach (var reminder in due) { reminders.RemoveAll(r => r.Id == reminder.Id); reminders.Add(reminder); }
        if (reminders.Count > 8) reminders.RemoveRange(0, reminders.Count - 8);
        Items.ItemsSource = reminders.ToArray();
        Show();
    }
    private void OnDismiss(object sender, RoutedEventArgs e) { host.StopAlarmSound(); reminders.Clear(); Hide(); }
    internal void Remove(Guid id)
    {
        reminders.RemoveAll(r => r.Id == id);
        Items.ItemsSource = reminders.ToArray();
        if (reminders.Count == 0) { host.StopAlarmSound(); Hide(); }
    }
    private void OnSnooze(object sender, RoutedEventArgs e)
    {
        if (!host.SnoozeReminders(reminders))
        { MessageBox.Show(this, "예약 알람을 정리한 후 다시 시도해 주세요.", "알람이 가득 찼어요"); return; }
        host.StopAlarmSound(); reminders.Clear(); Hide();
    }
}
