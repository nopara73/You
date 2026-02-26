using System.Runtime.InteropServices;
using System.Diagnostics;
using You.Library;

internal static class Program
{
    private const int ChromeLaunchDelayMilliseconds = 2500;
    private const int MouseClickDelayMilliseconds = 900;
    private const int PostClickKeyboardDelayMilliseconds = 120;
    private const int MinKeyDownMilliseconds = 35;
    private const int MaxKeyDownMilliseconds = 90;
    private const int MinInterKeyDelayMilliseconds = 60;
    private const int MaxInterKeyDelayMilliseconds = 190;
    private const string TargetUrl = "twitter.com";
    private const int ClipboardReadRetries = 4;
    private const int ClipboardReadRetryDelayMilliseconds = 80;
    private const int CursorMoveSteps = 150;
    private const int MinCursorMoveStepDelayMilliseconds = 10;
    private const int MaxCursorMoveStepDelayMilliseconds = 28;
    private const double MaxArcHeightPixels = 70.0;
    private const double EndJitterPixels = 1.5;
    private const int AddressBarOffsetFromTop = 50;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;
    private const byte VirtualKeyControl = 0x11;
    private const byte VirtualKeyA = 0x41;
    private const byte VirtualKeyC = 0x43;
    private const byte VirtualKeyDelete = 0x2E;
    private const byte VirtualKeyEnter = 0x0D;
    private const byte VirtualKeyEscape = 0x1B;
    private const byte VirtualKeyL = 0x4C;
    private const uint ClipboardUnicodeText = 13;

    private static void Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("This app only supports Windows.");
            return;
        }

        OpenChromeWindow();
        Thread.Sleep(ChromeLaunchDelayMilliseconds);
        MoveToAddressBarAndNavigate();
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

    private static void MoveToAddressBarAndNavigate()
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

        var windowWidth = windowRect.Right - windowRect.Left;
        var addressX = windowRect.Left + Math.Clamp((int)(windowWidth * 0.42), 220, Math.Max(220, windowWidth - 120));
        var addressY = windowRect.Top + AddressBarOffsetFromTop;

        if (!MoveCursorSmoothly(start.X, start.Y, addressX, addressY))
        {
            Console.WriteLine("Could not move cursor to the address bar.");
            return;
        }

        Thread.Sleep(MouseClickDelayMilliseconds);
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(PostClickKeyboardDelayMilliseconds);
        SelectAllWithKeyboard();
        Thread.Sleep(Random.Shared.Next(90, 220));

        PressKeyHumanLike(VirtualKeyDelete);
        Thread.Sleep(Random.Shared.Next(140, 280));
        TypeTextHumanLike(TargetUrl);
        Thread.Sleep(Random.Shared.Next(100, 220));
        PressKeyHumanLike(VirtualKeyEscape);
        Thread.Sleep(Random.Shared.Next(90, 180));
        EnsureAddressBarContainsTargetUrl();
        Thread.Sleep(Random.Shared.Next(120, 260));
        PressKeyHumanLike(VirtualKeyEnter);

        SetCursorPos(start.X, start.Y);
        Console.WriteLine($"Clicked address bar at X={addressX}, Y={addressY}, typed {TargetUrl}, and pressed Enter.");
    }

    private static void SelectAllWithKeyboard()
    {
        PressCtrlComboHumanLike(VirtualKeyA);
    }

    private static void PressKeyHumanLike(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        Thread.Sleep(Random.Shared.Next(MinKeyDownMilliseconds, MaxKeyDownMilliseconds + 1));
        keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static void PressCtrlComboHumanLike(byte virtualKey)
    {
        keybd_event(VirtualKeyControl, 0, 0, UIntPtr.Zero);
        Thread.Sleep(Random.Shared.Next(30, 75));
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        Thread.Sleep(Random.Shared.Next(MinKeyDownMilliseconds, MaxKeyDownMilliseconds + 1));
        keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
        Thread.Sleep(Random.Shared.Next(20, 60));
        keybd_event(VirtualKeyControl, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static void EnsureAddressBarContainsTargetUrl()
    {
        PressCtrlComboHumanLike(VirtualKeyL);
        Thread.Sleep(Random.Shared.Next(70, 170));
        PressCtrlComboHumanLike(VirtualKeyC);
        Thread.Sleep(Random.Shared.Next(100, 200));

        var copiedText = ReadClipboardTextWithRetries();
        if (BrowserInputLogic.UrlTextMatchesTarget(copiedText, TargetUrl))
        {
            return;
        }

        SelectAllWithKeyboard();
        Thread.Sleep(Random.Shared.Next(80, 180));
        PressKeyHumanLike(VirtualKeyDelete);
        Thread.Sleep(Random.Shared.Next(120, 240));
        TypeTextHumanLike(TargetUrl);
        Thread.Sleep(Random.Shared.Next(70, 160));
        PressKeyHumanLike(VirtualKeyEscape);
    }

    private static string? ReadClipboardTextWithRetries()
    {
        for (var i = 0; i < ClipboardReadRetries; i++)
        {
            var text = TryReadClipboardUnicodeText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            Thread.Sleep(ClipboardReadRetryDelayMilliseconds);
        }

        return null;
    }

    private static string? TryReadClipboardUnicodeText()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            var clipboardData = GetClipboardData(ClipboardUnicodeText);
            if (clipboardData == IntPtr.Zero)
            {
                return null;
            }

            var pointer = GlobalLock(clipboardData);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(clipboardData);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static void TypeTextHumanLike(string text)
    {
        foreach (var character in text)
        {
            if (!BrowserInputLogic.TryGetVirtualKeyForCharacter(character, out var virtualKey))
            {
                continue;
            }

            PressKeyHumanLike(virtualKey);
            Thread.Sleep(Random.Shared.Next(MinInterKeyDelayMilliseconds, MaxInterKeyDelayMilliseconds + 1));
        }
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
            var easedT = BrowserInputLogic.EaseInOutCubic(t);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

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
