using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Diagnostics;
using Microsoft.Win32;
using You.Library;

internal static class Program
{
    private const int BrowserWindowWidth = 1200;
    private const int BrowserWindowHeight = 800;
    private const int BrowserWindowResizeRetries = 30;
    private const int BrowserWindowResizeRetryDelayMilliseconds = 200;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const uint SpiGetWorkArea = 0x0030;
    private const int ChromeLaunchDelayMilliseconds = 2500;
    private const int MouseClickDelayMilliseconds = 900;
    private const int PostClickKeyboardDelayMilliseconds = 120;
    private const int MinKeyDownMilliseconds = 35;
    private const int MaxKeyDownMilliseconds = 90;
    private const int MinInterKeyDelayMilliseconds = 60;
    private const int MaxInterKeyDelayMilliseconds = 190;
    private const string TargetUrl = "https://twitter.com";
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
    private const byte VirtualKeySpace = 0x20;
    private const byte VirtualKeyControl = 0x11;
    private const byte VirtualKeyA = 0x41;
    private const byte VirtualKeyC = 0x43;
    private const byte VirtualKeyDelete = 0x2E;
    private const byte VirtualKeyEnter = 0x0D;
    private const byte VirtualKeyEscape = 0x1B;
    private const byte VirtualKeyL = 0x4C;
    private const uint ClipboardUnicodeText = 13;
    private const int AutomationLoopDelayMilliseconds = 1200;
    private const int EscapePollIntervalMilliseconds = 10;
    private const int TerminalWindowAttachTimeoutMilliseconds = 4000;
    private const int TerminalWindowAttachPollMilliseconds = 50;
    private const int SyntheticEscapeIgnoreMilliseconds = 75;
    private const int SwRestore = 9;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const uint LlkhfInjected = 0x00000010;
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const int StdOutputHandle = -11;

    private static volatile bool pauseRequested;
    private static volatile bool exitRequested;
    private static DateTime ignoreEscapeUntilUtc;
    private static IntPtr pauseTerminalWindow;
    private static string? pauseTerminalWindowTitle;
    private static int pauseTerminalProcessId;
    private static bool pauseTerminalLaunchedByApp;
    private static IntPtr keyboardHookHandle;
    private static Thread? keyboardHookThread;
    private static HookProc? keyboardHookProc;
    private static int escapePressCount;
    private static int consumedEscapePressCount;
    private static int shutdownMessagePrinted;
    private static int shutdownMessageWrittenToPauseTerminal;

    private static void Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("This app only supports Windows.");
            return;
        }

        RunAutomationUntilExplicitExit();
    }

    [SupportedOSPlatform("windows")]
    private static void RunAutomationUntilExplicitExit()
    {
        StartGlobalEscapeHook();
        OpenDefaultBrowser(TargetUrl);
        Console.WriteLine("Press Esc to pause and open terminal. Press Esc again in terminal to stop.");

        while (!exitRequested)
        {
            PollEscapeHotkey();
            if (exitRequested)
            {
                break;
            }

            if (pauseRequested)
            {
                BringTerminalToFront();
                WaitForTerminalEscapeToExit();
                continue;
            }

            MoveToAddressBarAndNavigate();
            InterruptibleSleep(AutomationLoopDelayMilliseconds);
        }

        EnsureShutdownMessageVisibleInPauseTerminal();
        ClosePauseTerminalIfOwned();
        Console.WriteLine("Stopped.");
        StopGlobalEscapeHook();
    }

    private static void PollEscapeHotkey()
    {
        if (DateTime.UtcNow < ignoreEscapeUntilUtc)
        {
            return;
        }

        var hasNewEscapePress = TryConsumeEscapePress();
        if (hasNewEscapePress && !pauseRequested)
        {
            pauseRequested = true;
            ignoreEscapeUntilUtc = DateTime.UtcNow.AddMilliseconds(SyntheticEscapeIgnoreMilliseconds);
            Console.WriteLine("Paused current action. Terminal opened. Press Esc again here to stop.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WaitForTerminalEscapeToExit()
    {
        while (!exitRequested)
        {
            var hasNewEscapePress = TryConsumeEscapePress();
            if (hasNewEscapePress)
            {
                PrintShutdownMessageOnce();
                exitRequested = true;
                return;
            }

            Thread.Sleep(EscapePollIntervalMilliseconds);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void BringTerminalToFront()
    {
        pauseTerminalWindow = GetConsoleWindow();
        if (pauseTerminalWindow != IntPtr.Zero)
        {
            pauseTerminalWindowTitle = GetWindowTitle(pauseTerminalWindow);
            pauseTerminalLaunchedByApp = false;
            pauseTerminalProcessId = 0;
        }

        if (pauseTerminalWindow == IntPtr.Zero)
        {
            pauseTerminalWindow = StartPauseTerminalWindow();
        }

        if (pauseTerminalWindow == IntPtr.Zero)
        {
            Console.WriteLine("Terminal opened, but focus tracking will use process detection.");
            return;
        }

        ShowWindow(pauseTerminalWindow, SwRestore);
        SetForegroundWindow(pauseTerminalWindow);
    }

    [SupportedOSPlatform("windows")]
    private static IntPtr StartPauseTerminalWindow()
    {
        try
        {
            pauseTerminalWindowTitle = "Consultation";
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k title {pauseTerminalWindowTitle} & cls & prompt $$$S",
                UseShellExecute = true
            };

            var terminalProcess = Process.Start(startInfo);
            if (terminalProcess is null)
            {
                return IntPtr.Zero;
            }

            pauseTerminalLaunchedByApp = true;
            pauseTerminalProcessId = terminalProcess.Id;

            var deadline = DateTime.UtcNow.AddMilliseconds(TerminalWindowAttachTimeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                terminalProcess.Refresh();
                if (terminalProcess.MainWindowHandle != IntPtr.Zero)
                {
                    return terminalProcess.MainWindowHandle;
                }

                Thread.Sleep(TerminalWindowAttachPollMilliseconds);
            }
        }
        catch
        {
            // Ignore launch errors and fall back gracefully.
        }

        return IntPtr.Zero;
    }

    private static void ClosePauseTerminalIfOwned()
    {
        if (!pauseTerminalLaunchedByApp || pauseTerminalProcessId <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(pauseTerminalProcessId);
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort cleanup; app shutdown should still continue.
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsPauseTerminalFocused()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        if (pauseTerminalWindow != IntPtr.Zero && foregroundWindow == pauseTerminalWindow)
        {
            return true;
        }

        var title = GetWindowTitle(foregroundWindow);
        if (!string.IsNullOrWhiteSpace(pauseTerminalWindowTitle) &&
            title.Contains(pauseTerminalWindowTitle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (GetWindowThreadProcessId(foregroundWindow, out var processId) == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName;
            return processName.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("conhost", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string GetWindowTitle(IntPtr windowHandle)
    {
        const int titleBufferSize = 512;
        var builder = new StringBuilder(titleBufferSize);
        _ = GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    [SupportedOSPlatform("windows")]
    private static void StartGlobalEscapeHook()
    {
        if (keyboardHookThread is not null)
        {
            return;
        }

        keyboardHookThread = new Thread(() =>
        {
            keyboardHookProc = KeyboardHookCallback;
            keyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, keyboardHookProc, IntPtr.Zero, 0);
            if (keyboardHookHandle == IntPtr.Zero)
            {
                return;
            }

            while (GetMessage(out _, IntPtr.Zero, 0, 0) != 0)
            {
            }

            UnhookWindowsHookEx(keyboardHookHandle);
            keyboardHookHandle = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "You.GlobalEscapeHookThread"
        };

        keyboardHookThread.Start();
    }

    [SupportedOSPlatform("windows")]
    private static void StopGlobalEscapeHook()
    {
        if (keyboardHookThread is null)
        {
            return;
        }

        try
        {
            keyboardHookThread.Interrupt();
        }
        catch
        {
            // Ignore thread interruption failures.
        }
    }

    private static bool TryConsumeEscapePress()
    {
        var currentCount = Volatile.Read(ref escapePressCount);
        if (currentCount <= consumedEscapePressCount)
        {
            return false;
        }

        consumedEscapePressCount = currentCount;
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown))
        {
            var keyboardData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if ((keyboardData.Flags & LlkhfInjected) != 0)
            {
                return CallNextHookEx(keyboardHookHandle, nCode, wParam, lParam);
            }

            if (keyboardData.VkCode == VirtualKeyEscape &&
                (pauseRequested || DateTime.UtcNow >= ignoreEscapeUntilUtc))
            {
                Interlocked.Increment(ref escapePressCount);
                if (pauseRequested)
                {
                    PrintShutdownMessageOnce();
                    exitRequested = true;
                }
            }
        }

        return CallNextHookEx(keyboardHookHandle, nCode, wParam, lParam);
    }

    [SupportedOSPlatform("windows")]
    private static void PrintShutdownMessageOnce()
    {
        if (Interlocked.Exchange(ref shutdownMessagePrinted, 1) == 1)
        {
            return;
        }

        var shutdownMessage = $"{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}Esc detected. Shutting down...";
        if (TryWriteToPauseTerminal(shutdownMessage))
        {
            Interlocked.Exchange(ref shutdownMessageWrittenToPauseTerminal, 1);
            return;
        }

        if (TryTypeShutdownMessageIntoFocusedTerminal())
        {
            Interlocked.Exchange(ref shutdownMessageWrittenToPauseTerminal, 1);
            return;
        }

        Console.WriteLine(shutdownMessage);
    }

    [SupportedOSPlatform("windows")]
    private static void EnsureShutdownMessageVisibleInPauseTerminal()
    {
        if (Volatile.Read(ref shutdownMessagePrinted) == 0)
        {
            return;
        }

        if (Volatile.Read(ref shutdownMessageWrittenToPauseTerminal) == 1)
        {
            return;
        }

        var shutdownMessage = $"{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}Esc detected. Shutting down...";
        if (TryWriteToPauseTerminal(shutdownMessage))
        {
            Interlocked.Exchange(ref shutdownMessageWrittenToPauseTerminal, 1);
            return;
        }

        if (TryTypeShutdownMessageIntoFocusedTerminal())
        {
            Interlocked.Exchange(ref shutdownMessageWrittenToPauseTerminal, 1);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryWriteToPauseTerminal(string message)
    {
        if (!pauseTerminalLaunchedByApp || pauseTerminalProcessId <= 0)
        {
            return false;
        }

        var hadExistingConsole = GetConsoleWindow() != IntPtr.Zero;
        var attachedToPauseTerminal = false;
        var detachedBeforeAttach = false;

        try
        {
            var freeConsoleResult = FreeConsole();
            detachedBeforeAttach = freeConsoleResult;
            if (!AttachConsole((uint)pauseTerminalProcessId))
            {
                return false;
            }

            attachedToPauseTerminal = true;
            return WriteLineToAttachedConsole(message);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (attachedToPauseTerminal)
            {
                _ = FreeConsole();
            }

            if (hadExistingConsole || detachedBeforeAttach)
            {
                _ = AttachConsole(AttachParentProcess);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool WriteLineToAttachedConsole(string message)
    {
        var outputHandle = GetStdHandle(StdOutputHandle);
        if (outputHandle == IntPtr.Zero || outputHandle == new IntPtr(-1))
        {
            return false;
        }

        var text = $"{message}{Environment.NewLine}";
        return WriteConsole(outputHandle, text, (uint)text.Length, out _, IntPtr.Zero);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryTypeShutdownMessageIntoFocusedTerminal()
    {
        if (!pauseRequested)
        {
            return false;
        }

        var foregroundWindow = GetForegroundWindow();
        var foregroundTitle = foregroundWindow == IntPtr.Zero ? string.Empty : GetWindowTitle(foregroundWindow);
        if (string.IsNullOrWhiteSpace(foregroundTitle) ||
            !foregroundTitle.Contains("Consultation", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        const string shortMessage = "echo.&echo.&echo.&echo exited.";
        foreach (var character in shortMessage)
        {
            if (!TryGetVirtualKeyForTerminalFallback(character, out var virtualKey))
            {
                return false;
            }

            keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
            keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
        }

        keybd_event(VirtualKeyEnter, 0, 0, UIntPtr.Zero);
        keybd_event(VirtualKeyEnter, 0, KeyEventKeyUp, UIntPtr.Zero);
        return true;
    }

    private static bool TryGetVirtualKeyForTerminalFallback(char character, out byte virtualKey)
    {
        if (character == ' ')
        {
            virtualKey = VirtualKeySpace;
            return true;
        }

        return BrowserInputLogic.TryGetVirtualKeyForCharacter(character, out virtualKey);
    }

    private static bool InterruptibleSleep(int totalMilliseconds)
    {
        var remaining = totalMilliseconds;
        while (remaining > 0)
        {
            PollEscapeHotkey();
            if (pauseRequested || exitRequested)
            {
                return false;
            }

            var delay = Math.Min(EscapePollIntervalMilliseconds, remaining);
            Thread.Sleep(delay);
            remaining -= delay;
        }

        return true;
    }

    [SupportedOSPlatform("windows")]
    private static void OpenDefaultBrowser(string url)
    {
        try
        {
            if (TryOpenDefaultBrowserInNewWindow(url))
            {
                Console.WriteLine($"Opened {url} in a new browser window.");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };

            Process.Start(startInfo);
            Console.WriteLine($"Opened {url} in the default browser.");
        }
        catch (Exception ex)
        {
            try
            {
                var fallbackStartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"\" \"{url}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                Process.Start(fallbackStartInfo);
                Console.WriteLine($"Opened {url} in the default browser (fallback path).");
            }
            catch (Exception fallbackEx)
            {
                Console.WriteLine(
                    $"Could not open {url} in the default browser: {ex.Message}. " +
                    $"Fallback also failed: {fallbackEx.Message}");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryOpenDefaultBrowserInNewWindow(string url)
    {
        var launchStartUtc = DateTime.UtcNow;
        var browserCommand = GetDefaultBrowserCommand();
        if (string.IsNullOrWhiteSpace(browserCommand) || !TryExtractExecutablePath(browserCommand, out var executablePath))
        {
            return false;
        }

        var executableName = Path.GetFileName(executablePath).ToLowerInvariant();
        var arguments = executableName switch
        {
            "firefox.exe" => $"-new-window \"{url}\" --width {BrowserWindowWidth} --height {BrowserWindowHeight}",
            "chrome.exe" or "msedge.exe" or "brave.exe" or "vivaldi.exe" or "opera.exe" or "launcher.exe"
                => $"--new-window --window-size={BrowserWindowWidth},{BrowserWindowHeight} \"{url}\"",
            _ => $"\"{url}\""
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false
        };

        var launchedProcess = Process.Start(startInfo);
        TryForceResizeBrowserWindow(launchedProcess, executablePath, launchStartUtc);
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static void TryForceResizeBrowserWindow(Process? launchedProcess, string executablePath, DateTime launchStartUtc)
    {
        var browserProcessName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(browserProcessName))
        {
            return;
        }

        for (var attempt = 0; attempt < BrowserWindowResizeRetries; attempt++)
        {
            var windowHandle = GetCandidateBrowserWindow(launchedProcess, browserProcessName, launchStartUtc);
            if (windowHandle != IntPtr.Zero && GetWindowRect(windowHandle, out var windowRect))
            {
                var targetX = windowRect.Left;
                var targetY = windowRect.Top;
                if (TryGetPrimaryWorkArea(out var workArea))
                {
                    targetX = Math.Max(workArea.Left, workArea.Right - BrowserWindowWidth);
                    targetY = workArea.Top;
                }

                var flags = SetWindowPosNoZOrder | SetWindowPosNoActivate;
                if (SetWindowPos(
                    windowHandle,
                    IntPtr.Zero,
                    targetX,
                    targetY,
                    BrowserWindowWidth,
                    BrowserWindowHeight,
                    flags))
                {
                    return;
                }
            }

            Thread.Sleep(BrowserWindowResizeRetryDelayMilliseconds);
        }
    }

    [SupportedOSPlatform("windows")]
    private static IntPtr GetCandidateBrowserWindow(Process? launchedProcess, string browserProcessName, DateTime launchStartUtc)
    {
        if (launchedProcess is not null)
        {
            try
            {
                launchedProcess.Refresh();
                if (!launchedProcess.HasExited && launchedProcess.MainWindowHandle != IntPtr.Zero)
                {
                    return launchedProcess.MainWindowHandle;
                }
            }
            catch
            {
                // Ignore process inspection failures and keep searching.
            }
        }

        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow != IntPtr.Zero && GetWindowThreadProcessId(foregroundWindow, out var foregroundPid) != 0)
        {
            try
            {
                using var foregroundProcess = Process.GetProcessById((int)foregroundPid);
                if (string.Equals(foregroundProcess.ProcessName, browserProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return foregroundWindow;
                }
            }
            catch
            {
                // Ignore and try scanning running processes.
            }
        }

        Process? newestBrowserProcess = null;
        try
        {
            foreach (var browserProcess in Process.GetProcessesByName(browserProcessName))
            {
                try
                {
                    if (browserProcess.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    // Prefer windows created around this launch to avoid resizing old windows.
                    if (browserProcess.StartTime.ToUniversalTime() < launchStartUtc.AddSeconds(-10))
                    {
                        continue;
                    }

                    if (newestBrowserProcess is null || browserProcess.StartTime > newestBrowserProcess.StartTime)
                    {
                        newestBrowserProcess = browserProcess;
                    }
                }
                catch
                {
                    browserProcess.Dispose();
                }
            }

            return newestBrowserProcess?.MainWindowHandle ?? IntPtr.Zero;
        }
        finally
        {
            newestBrowserProcess?.Dispose();
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetPrimaryWorkArea(out Rect workArea)
    {
        workArea = default;
        return SystemParametersInfo(SpiGetWorkArea, 0, out workArea, 0);
    }

    [SupportedOSPlatform("windows")]
    private static string? GetDefaultBrowserCommand()
    {
        using var userChoiceKey = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
        var progId = userChoiceKey?.GetValue("ProgId") as string;
        if (string.IsNullOrWhiteSpace(progId))
        {
            return null;
        }

        using var commandKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
        return commandKey?.GetValue(null) as string;
    }

    private static bool TryExtractExecutablePath(string command, out string executablePath)
    {
        executablePath = string.Empty;
        var trimmed = command.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed[0] == '"')
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote <= 1)
            {
                return false;
            }

            executablePath = trimmed.Substring(1, closingQuote - 1);
            return File.Exists(executablePath);
        }

        const string exeToken = ".exe";
        var exeIndex = trimmed.IndexOf(exeToken, StringComparison.OrdinalIgnoreCase);
        if (exeIndex < 0)
        {
            return false;
        }

        executablePath = trimmed.Substring(0, exeIndex + exeToken.Length);
        return File.Exists(executablePath);
    }

    private static void MoveToAddressBarAndNavigate()
    {
        if (pauseRequested || exitRequested)
        {
            return;
        }

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

        if (!InterruptibleSleep(MouseClickDelayMilliseconds))
        {
            return;
        }

        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        if (!InterruptibleSleep(PostClickKeyboardDelayMilliseconds))
        {
            return;
        }

        SelectAllWithKeyboard();
        if (!InterruptibleSleep(Random.Shared.Next(90, 220)))
        {
            return;
        }

        PressKeyHumanLike(VirtualKeyDelete);
        if (!InterruptibleSleep(Random.Shared.Next(140, 280)))
        {
            return;
        }

        TypeTextHumanLike(TargetUrl);
        if (!InterruptibleSleep(Random.Shared.Next(100, 220)))
        {
            return;
        }

        PressKeyHumanLike(VirtualKeyEscape);
        if (!InterruptibleSleep(Random.Shared.Next(90, 180)))
        {
            return;
        }

        EnsureAddressBarContainsTargetUrl();
        if (!InterruptibleSleep(Random.Shared.Next(120, 260)))
        {
            return;
        }

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
        if (pauseRequested || exitRequested)
        {
            return;
        }

        if (virtualKey == VirtualKeyEscape)
        {
            ignoreEscapeUntilUtc = DateTime.UtcNow.AddMilliseconds(SyntheticEscapeIgnoreMilliseconds);
        }

        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        if (!InterruptibleSleep(Random.Shared.Next(MinKeyDownMilliseconds, MaxKeyDownMilliseconds + 1)))
        {
            return;
        }

        keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static void PressCtrlComboHumanLike(byte virtualKey)
    {
        if (pauseRequested || exitRequested)
        {
            return;
        }

        keybd_event(VirtualKeyControl, 0, 0, UIntPtr.Zero);
        if (!InterruptibleSleep(Random.Shared.Next(30, 75)))
        {
            keybd_event(VirtualKeyControl, 0, KeyEventKeyUp, UIntPtr.Zero);
            return;
        }

        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        if (!InterruptibleSleep(Random.Shared.Next(MinKeyDownMilliseconds, MaxKeyDownMilliseconds + 1)))
        {
            keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
            keybd_event(VirtualKeyControl, 0, KeyEventKeyUp, UIntPtr.Zero);
            return;
        }

        keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
        if (!InterruptibleSleep(Random.Shared.Next(20, 60)))
        {
            keybd_event(VirtualKeyControl, 0, KeyEventKeyUp, UIntPtr.Zero);
            return;
        }

        keybd_event(VirtualKeyControl, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static void EnsureAddressBarContainsTargetUrl()
    {
        PressCtrlComboHumanLike(VirtualKeyL);
        if (!InterruptibleSleep(Random.Shared.Next(70, 170)))
        {
            return;
        }

        PressCtrlComboHumanLike(VirtualKeyC);
        if (!InterruptibleSleep(Random.Shared.Next(100, 200)))
        {
            return;
        }

        var copiedText = ReadClipboardTextWithRetries();
        if (BrowserInputLogic.UrlTextMatchesTarget(copiedText, TargetUrl))
        {
            return;
        }

        SelectAllWithKeyboard();
        if (!InterruptibleSleep(Random.Shared.Next(80, 180)))
        {
            return;
        }

        PressKeyHumanLike(VirtualKeyDelete);
        if (!InterruptibleSleep(Random.Shared.Next(120, 240)))
        {
            return;
        }

        TypeTextHumanLike(TargetUrl);
        if (!InterruptibleSleep(Random.Shared.Next(70, 160)))
        {
            return;
        }

        PressKeyHumanLike(VirtualKeyEscape);
    }

    private static string? ReadClipboardTextWithRetries()
    {
        for (var i = 0; i < ClipboardReadRetries; i++)
        {
            if (pauseRequested || exitRequested)
            {
                return null;
            }

            var text = TryReadClipboardUnicodeText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            if (!InterruptibleSleep(ClipboardReadRetryDelayMilliseconds))
            {
                return null;
            }
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
            if (pauseRequested || exitRequested)
            {
                return;
            }

            if (!BrowserInputLogic.TryGetVirtualKeyForCharacter(character, out var virtualKey))
            {
                continue;
            }

            PressKeyHumanLike(virtualKey);
            if (!InterruptibleSleep(Random.Shared.Next(MinInterKeyDelayMilliseconds, MaxInterKeyDelayMilliseconds + 1)))
            {
                return;
            }
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
            if (pauseRequested || exitRequested)
            {
                return false;
            }

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
            if (!InterruptibleSleep(delay))
            {
                return false;
            }
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
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out Rect pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteConsole(
        IntPtr hConsoleOutput,
        string lpBuffer,
        uint nNumberOfCharsToWrite,
        out uint lpNumberOfCharsWritten,
        IntPtr lpReserved);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Message lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr HWnd;
        public uint MessageId;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
        public uint LPrivate;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
}
