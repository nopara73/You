using System.Runtime.InteropServices;
using System.Diagnostics;

internal static class Program
{
    private const int ChromeLaunchDelayMilliseconds = 2500;
    private const int MouseClickDelayMilliseconds = 900;
    private const int CursorMoveSteps = 150;
    private const int MinCursorMoveStepDelayMilliseconds = 10;
    private const int MaxCursorMoveStepDelayMilliseconds = 28;
    private const double MaxArcHeightPixels = 70.0;
    private const double EndJitterPixels = 1.5;
    private const int CloseButtonOffsetFromRight = 16;
    private const int CloseButtonOffsetFromTop = 16;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    private static void Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("This app only supports Windows.");
            return;
        }

        OpenChromeWindow();
        Thread.Sleep(ChromeLaunchDelayMilliseconds);
        MoveToWindowCloseAndClick();
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

    private static void MoveToWindowCloseAndClick()
    {
        if (!GetCursorPos(out var start))
        {
            Console.WriteLine("Could not read cursor position.");
            return;
        }

        var activeWindow = GetForegroundWindow();
        if (activeWindow == IntPtr.Zero || !GetWindowRect(activeWindow, out var windowRect))
        {
            Console.WriteLine("Could not determine active window bounds.");
            return;
        }

        var closeX = windowRect.Right - CloseButtonOffsetFromRight;
        var closeY = windowRect.Top + CloseButtonOffsetFromTop;

        if (!MoveCursorSmoothly(start.X, start.Y, closeX, closeY))
        {
            Console.WriteLine("Could not move cursor to the close button.");
            return;
        }

        Thread.Sleep(MouseClickDelayMilliseconds);
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);

        SetCursorPos(start.X, start.Y);
        Console.WriteLine($"Clicked close button at X={closeX}, Y={closeY}.");
    }

    private static bool MoveCursorSmoothly(int fromX, int fromY, int toX, int toY)
    {
        var deltaX = toX - fromX;
        var deltaY = toY - fromY;
        var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (distance < 1)
        {
            return SetCursorPos(toX, toY);
        }

        var unitPerpX = -(deltaY / distance);
        var unitPerpY = deltaX / distance;
        var direction = Random.Shared.Next(0, 2) == 0 ? -1.0 : 1.0;
        var arcHeight = Math.Min(MaxArcHeightPixels, distance * 0.18) * direction;

        for (var step = 1; step <= CursorMoveSteps; step++)
        {
            var t = (double)step / CursorMoveSteps;
            var easedT = EaseInOutCubic(t);

            var baseX = fromX + (deltaX * easedT);
            var baseY = fromY + (deltaY * easedT);

            // Arc peaks around mid-path and fades near start/end.
            var arcFactor = Math.Sin(Math.PI * easedT);
            var arcOffset = arcHeight * arcFactor;

            // Tiny jitter that fades as the cursor reaches the target.
            var jitterScale = (1.0 - easedT) * EndJitterPixels;
            var jitterX = (Random.Shared.NextDouble() - 0.5) * 2.0 * jitterScale;
            var jitterY = (Random.Shared.NextDouble() - 0.5) * 2.0 * jitterScale;

            var nextX = (int)Math.Round(baseX + (unitPerpX * arcOffset) + jitterX);
            var nextY = (int)Math.Round(baseY + (unitPerpY * arcOffset) + jitterY);

            if (!SetCursorPos(nextX, nextY))
            {
                return false;
            }

            var delay = Random.Shared.Next(MinCursorMoveStepDelayMilliseconds, MaxCursorMoveStepDelayMilliseconds + 1);
            Thread.Sleep(delay);
        }

        return SetCursorPos(toX, toY);
    }

    private static double EaseInOutCubic(double t)
    {
        return t < 0.5
            ? 4.0 * t * t * t
            : 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
