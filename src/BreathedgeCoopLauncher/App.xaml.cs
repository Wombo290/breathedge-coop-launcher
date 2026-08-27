using System.Windows;
using System.Windows.Threading;

namespace BreathedgeCoopLauncher;

public partial class App : Application
{
    public App() => DispatcherUnhandledException += OnUnhandledException;

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"The launcher encountered an unexpected error:\n\n{e.Exception.Message}",
            "Breathedge Co-op Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(1);
    }
}
