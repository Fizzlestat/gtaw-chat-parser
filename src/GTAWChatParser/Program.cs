namespace GTAWChatParser;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(false, @"Local\GTAWChatParser", out _);
        var acquired = false;
        try
        {
            acquired = instance.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            MessageBox.Show("GTA: World Chat Parser is already open.", "GTA: World Chat Parser", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, args) => AppLog.Error("UI exception.", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                AppLog.Error("Unhandled exception.", exception);
        };

        Application.Run(new HudForm());
    }
}
