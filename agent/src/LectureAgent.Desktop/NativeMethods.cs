using System.Runtime.InteropServices;

namespace LectureAgent.Desktop;

internal static class NativeMethods
{
    internal static readonly IntPtr HwndBroadcast = new(0xffff);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
