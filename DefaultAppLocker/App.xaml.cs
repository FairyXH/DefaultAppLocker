using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using DefaultAppLocker.Core;

namespace DefaultAppLocker;

public partial class App : Application
{
    private const int AttachParentProcess = -1;
    private const int StandardOutputHandle = -11;
    private const int StandardErrorHandle = -12;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WriteConsoleW(IntPtr hConsoleOutput, string lpBuffer, int nNumberOfCharsToWrite, out int lpNumberOfCharsWritten, IntPtr lpReserved);

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
        var text = message + Environment.NewLine;
        var handle = GetStdHandle(success ? StandardOutputHandle : StandardErrorHandle);
        if (handle != IntPtr.Zero && handle != new IntPtr(-1) && WriteConsoleW(handle, text, text.Length, out _, IntPtr.Zero))
        {
            return;
        }

        var stream = success ? Console.OpenStandardOutput() : Console.OpenStandardError();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        writer.Write(text);
    }
}
