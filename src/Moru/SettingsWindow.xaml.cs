using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Moru;

public partial class SettingsWindow : Window
{
    private readonly App host;
    private bool ready, selecting;
    internal SettingsWindow(App app)
    {
        host = app; InitializeComponent();
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 24);
        Height = Math.Min(740, MaxHeight);
        Closing += (_, e) => { if (!host.Exiting) { e.Cancel = true; Hide(); host.ShowPanel(); } };
        Loaded += (_, _) => SelectCurrentSkin();
        Tabs.SelectionChanged += (_, e) => { if (e.Source == Tabs) Dispatcher.BeginInvoke(SelectCurrentSkin); };
        var suggested = DateTime.Now.AddMinutes(5);
        AlarmDate.SelectedDate = suggested.Date;
        AlarmTime.Text = suggested.ToString("HH:mm");
        Sync(); ready = true;
    }
    internal void SelectTab(int index) { Tabs.SelectedIndex = Math.Clamp(index, 0, 3); Sync(); }
    private void Sync()
    {
        ready = false;
        var p = host.Preferences;
        DarkToggle.IsChecked = p.DarkMode; HoverToggle.IsChecked = p.HoverEnabled;
        TopmostToggle.IsChecked = p.AlwaysOnTop; AnimationToggle.IsChecked = p.AnimationEnabled;
        CodexToggle.IsChecked = p.CodexEnabled; ClaudeToggle.IsChecked = p.ClaudeEnabled;
        AlarmSoundToggle.IsChecked = p.AlarmSound;
        TextSizePicker.SelectedIndex = p.TextSize switch { 18 => 1, 20 => 2, _ => 0 };
        RefreshPicker.SelectedIndex = p.RefreshSeconds switch { 5 => 1, 10 => 2, _ => 0 };
        SkinList.ItemsSource = host.Skins; RefreshConnections(); RefreshAlarms(); ready = true;
    }
    internal void RefreshConnections()
    {
        CodexNotice.Text = host.Preferences.CodexEnabled ? host.Ai.CodexStatus : "연결을 켜면 로컬 작업 상태와 수신된 사용량을 표시해요.";
        ClaudeNotice.Text = host.Preferences.ClaudeEnabled ? host.ClaudeNotice : "작업 상태와 사용량 연결 · Claude Code 새 세션에 적용";
    }
    internal void RefreshAlarms()
    {
        AlarmList.ItemsSource = host.Preferences.Reminders.OrderBy(r => r.DueAt).ToArray();
        AlarmNotice.Text = host.Preferences.Reminders.Count == 0 ? "등록된 알람이 없어요. 최대 8개까지 추가할 수 있어요." : $"예약된 알람 {host.Preferences.Reminders.Count}개";
    }
    internal void ReloadSkins() { SkinList.ItemsSource = host.Skins; Dispatcher.BeginInvoke(SelectCurrentSkin); }
    private void SelectCurrentSkin()
    {
        selecting = true;
        foreach (var radio in Descendants<RadioButton>(SkinList)) radio.IsChecked = radio.Tag is Skin s && s.Id == host.CurrentSkin.Id;
        selecting = false;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var item2 in Descendants<T>(child)) yield return item2;
        }
    }
    private void OnAppearance(object sender, RoutedEventArgs e) { if (ready) host.ChangeAppearance(DarkToggle.IsChecked == true, host.Preferences.TextSize); }
    private void OnTextSize(object sender, SelectionChangedEventArgs e)
    {
        if (ready && TextSizePicker.SelectedItem is ComboBoxItem item) host.ChangeAppearance(host.Preferences.DarkMode, int.Parse((string)item.Tag));
    }
    private void OnHover(object sender, RoutedEventArgs e) { if (ready) host.ChangeHover(HoverToggle.IsChecked == true); }
    private void OnTopmost(object sender, RoutedEventArgs e) { if (ready) host.ChangeTopmost(TopmostToggle.IsChecked == true); }
    private void OnAnimation(object sender, RoutedEventArgs e) { if (ready) host.ChangeAnimation(AnimationToggle.IsChecked == true); }
    private void OnInterval(object sender, SelectionChangedEventArgs e) { if (ready && RefreshPicker.SelectedItem is ComboBoxItem item) host.ChangeInterval(int.Parse((string)item.Tag)); }
    private void OnSkinSelected(object sender, RoutedEventArgs e) { if (ready && !selecting && sender is RadioButton { Tag: Skin skin }) host.ChangeSkin(skin); }
    private void OnCodex(object sender, RoutedEventArgs e) { if (ready) { host.ChangeCodex(CodexToggle.IsChecked == true); RefreshConnections(); } }
    private void OnClaude(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        if (!host.ChangeClaude(ClaudeToggle.IsChecked == true))
        {
            ready = false; ClaudeToggle.IsChecked = host.Preferences.ClaudeEnabled; ready = true;
            MessageBox.Show(this, host.ClaudeNotice, "Claude Code 연결");
        }
        RefreshConnections();
    }
    private void OnAlarmSound(object sender, RoutedEventArgs e) { if (ready) host.ChangeAlarmSound(AlarmSoundToggle.IsChecked == true); }
    private void OnQuickAlarm(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string minutes }) StartTimer(minutes);
    }
    private void OnStartTimer(object sender, RoutedEventArgs e) => StartTimer(TimerMinutes.Text);
    private void StartTimer(string minutes)
    {
        if (!ReminderRules.TryCreateAfter(minutes, DateTimeOffset.Now, out var reminder))
        { AlarmNotice.Text = "1~1440 사이의 정수(분)를 입력해 주세요."; return; }
        if (host.AddReminder(reminder!))
            AlarmNotice.Text = $"{reminder!.Title} 시작 · {reminder.DueAt.ToLocalTime():HH:mm}에 알림" + (host.Preferences.AlarmSound ? "" : " · 소리 꺼짐");
        else AlarmNotice.Text = "알람은 최대 8개예요. 기존 알람을 정리해 주세요.";
    }
    private void OnPreviewSound(object sender, RoutedEventArgs e) => AlarmNotice.Text =
        host.PlayAlarmSound() ? "미리듣기 재생 · 소리가 작으면 Windows 음량을 확인해 주세요." : "소리를 재생하지 못했어요. 출력 장치를 확인해 주세요.";
    private void OnAddAlarm(object sender, RoutedEventArgs e)
    {
        if (!ReminderRules.TryCreate(AlarmDate.SelectedDate, AlarmTime.Text, AlarmTitle.Text, DateTimeOffset.Now, out var reminder))
        { AlarmNotice.Text = "앞으로 1년 이내 날짜와 시간(HH:mm)을 입력해 주세요."; return; }
        Add(reminder!);
    }
    private void Add(Reminder reminder)
    {
        if (!host.AddReminder(reminder)) AlarmNotice.Text = "알람은 최대 8개예요. 기존 알람을 정리해 주세요.";
        else RefreshAlarms();
    }
    private void OnDeleteAlarm(object sender, RoutedEventArgs e) { if (sender is Button { Tag: Guid id }) host.RemoveReminder(id); }
    private void OnPreview(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string name } && Enum.TryParse<PetActivity>(name, out var state)) host.PreviewAnimation(state); }
    private void OnReset(object sender, RoutedEventArgs e) => host.ResetPosition();
    private void OnAi(object sender, RoutedEventArgs e) => host.ShowAi();
    private void OnImportSkin(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "PNG 친구 추가", Filter = "PNG 이미지 (*.png)|*.png", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        try { host.ImportSkin(picker.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FormatException or System.Runtime.InteropServices.COMException)
        { MessageBox.Show(this, ex is InvalidDataException ? ex.Message : "이미지를 읽거나 저장하지 못했어요.", "친구 추가"); }
    }
    private void OnExportPreview(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.SaveFileDialog { Title = "소개 화면 저장", Filter = "PNG 이미지 (*.png)|*.png", FileName = "moru-preview.png", AddExtension = true };
        if (picker.ShowDialog(this) != true) return;
        try { host.ShowPanel(); host.ExportPreview(picker.FileName); SaveNotice.Text = "소개 화면을 저장했어요."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException)
        { MessageBox.Show(this, "다른 폴더에 다시 저장해 주세요.", "화면 저장"); }
    }
    internal void ShowSaveResult(bool saved) => SaveNotice.Text = saved ? "변경하면 자동 저장돼요." : "설정을 저장하지 못했어요.";
    private void OnHide(object sender, RoutedEventArgs e) { Hide(); host.ShowPanel(); }
    private void OnExit(object sender, RoutedEventArgs e) => host.ExitApp();
    private void OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Hide(); host.ShowPanel(); } }
    private void OnDrag(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is TextBlock) DragMove(); }
}
