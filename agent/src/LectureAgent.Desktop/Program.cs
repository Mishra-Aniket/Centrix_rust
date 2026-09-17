namespace LectureAgent.Desktop;

internal static class Program
{
    /// <summary>
    /// Broadcast by a second launch of the app so the already-running instance
    /// pops its window back up instead of starting a duplicate.
    /// </summary>
    internal static readonly uint ShowWindowMessage =
        NativeMethods.RegisterWindowMessage("Centrix.ShowMainWindow");

    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, @"Local\CentrixApp", out var isFirstInstance);
        if (!isFirstInstance)
        {
            NativeMethods.PostMessage(NativeMethods.HwndBroadcast, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
