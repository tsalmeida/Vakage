using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Vakage.Views;

namespace Vakage;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // A fault in the window is logged and shown, not allowed to close the app.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            AppLog.Error("Unhandled error in the window", e.Exception);
            e.Handled = true;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: Views.MainWindow window })
                window.ShowError(e.Exception);
        };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
