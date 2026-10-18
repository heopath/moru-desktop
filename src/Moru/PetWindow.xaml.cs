using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Moru;

public partial class PetWindow : Window
{
    private readonly App host;
    private Point? pointerDown;
    private bool dragged;

    internal PetWindow(App app)
    {
        host = app;
        InitializeComponent();
        var menu = new ContextMenu();
        AddMenu(menu, "내 PC 상태 / 스킨", () => host.ShowPanel());
        AddMenu(menu, "캐릭터 숨기기", () => host.TogglePet());
        AddMenu(menu, "위치 초기화", () => host.ResetPosition());
        menu.Items.Add(new Separator());
        AddMenu(menu, "모루 종료", () => host.ExitApp());
        PetButton.ContextMenu = menu;
        SourceInitialized += (_, _) => WindowPlacement.Restore(this, host.Preferences.X, host.Preferences.Y);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => WindowPlacement.Clamp(this));
        Closing += (_, args) =>
        {
            if (host.Exiting) return;
            args.Cancel = true;
            Dispatcher.BeginInvoke(host.ExitApp);
        };
    }

    internal void ApplySkin(Skin skin)
    {
        Character.Skin = skin;
        NameLabel.Text = $"{skin.Name} · 클릭해 봐요";
    }

    private static void AddMenu(ContextMenu menu, string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void OnPetClick(object sender, RoutedEventArgs e)
    {
        pointerDown = null;
        if (!dragged) host.TogglePanel();
        dragged = false;
    }
    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        pointerDown = e.GetPosition(this);
        dragged = false;
    }
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (pointerDown is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        pointerDown = null;
        dragged = true;
        Mouse.Capture(null);
        host.HidePanel();
        try { DragMove(); } catch (InvalidOperationException) { }
        WindowPlacement.Clamp(this);
        host.SavePosition();
        e.Handled = true;
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) host.HidePanel();
    }
}
