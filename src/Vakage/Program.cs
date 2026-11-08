using Avalonia;
using Avalonia.Media;

namespace Vakage;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppLog.Info("Vakage starting");

        // `--import`, `--list` and `--files` work and stop: no window and no
        // single-instance lock, so they run from a script while the app is open.
        if (CommandLine.Requested(args))
        {
            ParentConsole.Attach();
            return CommandLine.Run(args);
        }

        if (!SingleInstance.TryAcquire())
            return 0;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            AppLog.Info("Vakage closed normally");
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Error("Vakage could not start", ex);
            throw;
        }
        finally
        {
            SingleInstance.Release();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions { DefaultFamilyName = "avares://Vakage/Assets/Fonts#Roboto" })
            .LogToTrace();
}
