using System;

namespace Moru;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // No WPF window, tray, settings load or GPU work in the short-lived hook process.
        if (args.Length == 2 && args[0] is ClaudeBridge.HookFlag or ClaudeBridge.StatusFlag)
            return ClaudeBridge.Send(args[1], args[0] == ClaudeBridge.StatusFlag);
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
