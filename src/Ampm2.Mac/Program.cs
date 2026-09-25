using System;
using Avalonia;

namespace Ampm2.Mac;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless protocol self-test (no window): ampm2 --selftest <report.txt>
        int st = Array.IndexOf(args, "--selftest");
        if (st >= 0 && st + 1 < args.Length)
            return Ampm2.Sys.SelfTest.RunAsync(args[st + 1]).GetAwaiter().GetResult();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = true })
            .LogToTrace();
}
