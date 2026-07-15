using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using DefaultAppLocker.Core;

namespace DefaultAppLocker;

public partial class App : Application
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var store = new ConfigurationStore();
        var commandLine = new DefaultAppLockerCommandLine(store, new AssociationService(), new RegistryDefaultAppScanner());
        if (commandLine.HasCommandLineMode(e.Args))
        {
            var result = await commandLine.RunAsync(e.Args);
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                WriteCommandLineMessage(result.Message, result.ExitCode == 0);
                store.AppendLog("CLI: " + result.Message);
            }
            Current.Shutdown(result.ExitCode);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void WriteCommandLineMessage(string message, bool success)
    {
        AttachConsole(AttachParentProcess);
        var stream = success ? Console.OpenStandardOutput() : Console.OpenStandardError();
        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        writer.WriteLine(message);
    }
}
