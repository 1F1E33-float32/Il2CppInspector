using Avalonia;

namespace Il2CppInspector.GUI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) => AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}
