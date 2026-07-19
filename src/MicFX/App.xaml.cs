using System.Windows;

namespace MicFX;

public partial class App : Application
{
    private Mutex? singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        singleInstanceMutex = new Mutex(true, @"Local\MicFX_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("MicFX is already running — look for its icon in the system tray.",
                "MicFX", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "MicFX — unexpected error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Don't let close-to-tray block Windows shutdown/sign-out.
        SessionEnding += (_, _) => (MainWindow as MainWindow)?.ForceExit();

        var window = new MainWindow();
        // The Run registry entry launches us with --minimized: start hidden in the tray.
        if (!e.Args.Contains("--minimized"))
            window.Show();
    }
}
