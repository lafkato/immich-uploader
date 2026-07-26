using ImmichUploaderApp.Services;

namespace ImmichUploaderApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\ImmichUploaderApp_SingleInstance_9F3D2C11", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Immich Uploader on jo kaynnissa (katso ilmoitusalue).", "Immich Uploader",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => AppLogger.LogFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLogger.LogFatal(e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
