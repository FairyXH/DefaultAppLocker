using System.Windows;
using DefaultAppLocker.Core;

namespace DefaultAppLocker;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var store = new ConfigurationStore();
        var commandLine = new DefaultAppLockerCommandLine(store, new AssociationService(), new RegistryDefaultAppScanner());
        if (commandLine.HasCommandLineMode(e.Args))
        {
            var result = await commandLine.RunAsync(e.Args).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(result.Message)) store.AppendLog("CLI: " + result.Message);
            Current.Shutdown(result.ExitCode);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
