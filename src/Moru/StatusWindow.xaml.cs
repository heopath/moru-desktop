using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Moru;

public partial class StatusWindow : Window
{
    private readonly App host;
    private readonly SystemMetrics metrics = new();
    private readonly DispatcherTimer timer;
    private bool ready;
    private bool paused;
    private bool suppressSelection;

    internal StatusWindow(App app)
    {
        host = app;
        InitializeComponent();
        timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        timer.Tick += (_, _) => RefreshMetrics();
        SkinList.ItemsSource = host.Skins;
        TopmostToggle.IsChecked = host.Preferences.AlwaysOnTop;
        RefreshPicker.SelectedIndex = host.Preferences.RefreshSeconds switch { 5 => 1, 10 => 2, _ => 0 };
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 24);
        Height = Math.Min(MaxHeight, host.Skins.Count > 3 ? 790 : 700);
        ready = true;
        IsVisibleChanged += (_, _) => UpdateSampling();
        StateChanged += (_, _) => UpdateSampling();
        Loaded += (_, _) => SelectCurrentSkin();
        Closing += OnClosing;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => WindowPlacement.Clamp(this));
    }

    internal void SetPaused(bool value)
    {
        if (paused == value) return;
        paused = value;
        UpdateSampling();
    }

    private void UpdateSampling()
    {
        timer.Stop();
        metrics.Reset();
        DiagnosticLog.Write($"Sampling visible={IsVisible} paused={paused} interval={host.Preferences.RefreshSeconds}");
        if (!IsVisible || WindowState == WindowState.Minimized || paused) return;
        CpuValue.Text = "—";
        CpuBar.Value = 0;
        timer.Interval = TimeSpan.FromSeconds(host.Preferences.RefreshSeconds);
        RefreshMetrics();
        timer.Start();
    }

    private void RefreshMetrics()
    {
        Metrics sample = metrics.Read();
        if (sample.CpuPercent is double cpu)
        {
            CpuValue.Text = $"{cpu:0}%";
            CpuBar.Value = cpu;
            CpuStatus.Text = cpu >= 85 ? "바쁘게 일하는 중" : cpu >= 50 ? "일하는 중" : "여유로워요";
            CpuDetail.Text = $"최근 {host.Preferences.RefreshSeconds}초 동안의 전체 CPU 사용률";
        }
        else
        {
            CpuValue.Text = "—";
            CpuBar.Value = 0;
            CpuStatus.Text = metrics.SupportsCpu ? "측정 준비 중" : "지원 범위 확인";
            CpuDetail.Text = metrics.SupportsCpu ? "첫 측정값을 기다리고 있어요" : "여러 CPU 그룹을 가진 PC는 아직 지원하지 않아요";
        }
        MemoryValue.Text = sample.TotalMemory == 0 ? "—" : $"{sample.MemoryPercent:0}%";
        MemoryBar.Value = sample.MemoryPercent;
        MemoryDetail.Text = sample.TotalMemory == 0 ? "메모리 정보를 읽을 수 없어요" :
            $"{sample.UsedMemory / 1073741824d:0.0} / {sample.TotalMemory / 1073741824d:0.0} GB 사용 중";
    }

    internal void SelectCurrentSkin()
    {
        suppressSelection = true;
        foreach (var radio in Descendants<RadioButton>(SkinList))
            radio.IsChecked = radio.Tag is Skin skin && skin.Id == host.CurrentSkin.Id;
        suppressSelection = false;
    }

    internal void ReloadSkins()
    {
        SkinList.ItemsSource = host.Skins;
        Height = Math.Min(MaxHeight, host.Skins.Count > 3 ? 790 : 700);
        Dispatcher.BeginInvoke(() =>
        {
            host.PositionPanel();
            SelectCurrentSkin();
            SkinScroll.ScrollToEnd();
        });
    }

    private void OnImportSkin(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "PNG 친구 추가", Filter = "PNG 이미지 (*.png)|*.png", Multiselect = false, CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        try { host.ImportSkin(picker.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or FormatException or System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(this, ex is InvalidDataException ? ex.Message : "이미지를 읽거나 저장하지 못했어요. 다른 PNG 파일로 다시 시도해 주세요.",
                "친구를 추가하지 못했어요", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnExportPreview(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.SaveFileDialog
        {
            Title = "소개 화면 저장", Filter = "PNG 이미지 (*.png)|*.png", FileName = "moru-preview.png", AddExtension = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            host.ExportPreview(picker.FileName);
            FooterStatus.Text = "소개 화면을 PNG로 저장했어요.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(this, "이미지를 저장하지 못했어요. 다른 폴더를 선택해 주세요.", "화면 저장", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private void OnSkinSelected(object sender, RoutedEventArgs e)
    {
        if (ready && !suppressSelection && sender is RadioButton { Tag: Skin skin }) host.ChangeSkin(skin);
    }
    private void OnTopmostChanged(object sender, RoutedEventArgs e)
    {
        if (ready) host.ChangeTopmost(TopmostToggle.IsChecked == true);
    }
    private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || RefreshPicker.SelectedItem is not ComboBoxItem item) return;
        host.ChangeInterval(int.Parse((string)item.Tag));
        UpdateSampling();
    }
    internal void ShowSaveResult(bool saved) => FooterStatus.Text = saved
        ? "패널을 닫으면 측정도 쉬어요." : "설정을 저장하지 못했어요. 저장 폴더를 확인해 주세요.";
    private void OnHide(object sender, RoutedEventArgs e) => Hide();
    private void OnExit(object sender, RoutedEventArgs e) => host.ExitApp();
    private void OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Hide(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        timer.Stop();
        if (!host.Exiting) { e.Cancel = true; Hide(); }
    }
}
