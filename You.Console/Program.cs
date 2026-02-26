using System.Runtime.InteropServices;
using System.Diagnostics;

internal static class Program
{
    private const int MovePixels = 40;
    private const int PauseMilliseconds = 250;

    private static void Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("This app only supports Windows.");
            return;
        }

        OpenChromeWindow();

        if (!GetCursorPos(out var start))
        {
            Console.WriteLine("Could not read cursor position.");
            return;
        }

        Console.WriteLine($"Starting cursor position: X={start.X}, Y={start.Y}");

        Console.WriteLine(
            $"Movement disabled. Would move to X={start.X + MovePixels}, Y={start.Y}, " +
            $"wait {PauseMilliseconds}ms, then return to X={start.X}, Y={start.Y}.");
    }

    private static void OpenChromeWindow()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c start \"\" chrome --new-window about:blank",
                UseShellExecute = true,
                CreateNoWindow = true
            };

            Process.Start(startInfo);
            Console.WriteLine("Opened a new Chrome window.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not open Chrome window: {ex.Message}");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
