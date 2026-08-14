namespace CodexModelSwitcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            return RunCommand(args);
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }

    private static int RunCommand(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], "token", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("無法辨識命令。");
            return 2;
        }

        try
        {
            Console.Out.Write(CredentialVault.Read(args[1]));
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
