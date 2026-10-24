// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Client\Services\HookMonitorService.cs
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ExamMonitor.Common.Models;

namespace ExamMonitor.Client.Services;

public sealed class HookMonitorService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int LLKHF_ALTDOWN = 0x20;

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static readonly HashSet<string> FlaggedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "taskmgr", "cmd", "powershell", "pwsh", "regedit",
        "whatsapp", "telegram", "discord", "AnyDesk", "TeamViewer"
    };

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private readonly LowLevelKeyboardProc _hookCallback;
    private IntPtr _hookId = IntPtr.Zero;

    public HookMonitorService()
    {
        _hookCallback = HookCallback;
    }

    public bool SuppressSystemSwitchKeys { get; set; }

    public event Action<string>? FocusLostDetected;

    public void InstallKeyboardHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            return;
        }

        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule? curModule = curProcess.MainModule;
        IntPtr moduleHandle = GetModuleHandle(curModule?.ModuleName);
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallback, moduleHandle, 0);
    }

    public void UninstallKeyboardHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    public (bool IsCompliant, string ActiveWindowDescription) InspectForegroundWindow()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return (true, "Desktop");
        }

        _ = GetWindowThreadProcessId(hwnd, out uint foregroundPid);
        var sb = new StringBuilder(256);
        int len = GetWindowText(hwnd, sb, sb.Capacity);
        string windowTitle = len > 0 ? sb.ToString() : "Desktop";

        string procName = "System";
        try
        {
            using Process proc = Process.GetProcessById((int)foregroundPid);
            procName = proc.ProcessName;
        }
        catch
        {
        }

        string description = $"[{procName}] {windowTitle}";
        bool isFlagged = FlaggedProcessNames.Contains(procName);
        if (isFlagged)
        {
            FocusLostDetected?.Invoke(description);
        }

        return (!isFlagged, description);
    }

    public static void ExecuteRemoteMouse(CommandPacket command)
    {
        int screenW = Math.Max(1, GetSystemMetrics(SM_CXSCREEN));
        int screenH = Math.Max(1, GetSystemMetrics(SM_CYSCREEN));

        int targetX = (int)Math.Round(Math.Clamp(command.NormalizedX, 0.0, 1.0) * (screenW - 1));
        int targetY = (int)Math.Round(Math.Clamp(command.NormalizedY, 0.0, 1.0) * (screenH - 1));

        SetCursorPos(targetX, targetY);

        switch (command.MouseButton)
        {
            case 1:
                mouse_event(MOUSEEVENTF_LEFTDOWN, targetX, targetY, 0, UIntPtr.Zero);
                break;
            case 2:
                mouse_event(MOUSEEVENTF_LEFTUP, targetX, targetY, 0, UIntPtr.Zero);
                break;
            case 3:
                mouse_event(MOUSEEVENTF_RIGHTDOWN, targetX, targetY, 0, UIntPtr.Zero);
                break;
            case 4:
                mouse_event(MOUSEEVENTF_RIGHTUP, targetX, targetY, 0, UIntPtr.Zero);
                break;
        }
    }

    public static void ExecuteRemoteKey(CommandPacket command)
    {
        if (command.VirtualKey <= 0 || command.VirtualKey > 255)
        {
            return;
        }

        uint flags = command.IsKeyUp ? KEYEVENTF_KEYUP : 0u;
        keybd_event((byte)command.VirtualKey, 0, flags, UIntPtr.Zero);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && SuppressSystemSwitchKeys && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            KBDLLHOOKSTRUCT kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool altDown = (kb.flags & LLKHF_ALTDOWN) != 0;

            if (kb.vkCode == VK_LWIN ||
                kb.vkCode == VK_RWIN ||
                (altDown && (kb.vkCode == VK_TAB || kb.vkCode == VK_ESCAPE)))
            {
                FocusLostDetected?.Invoke($"BlockedKey:{kb.vkCode}");
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        UninstallKeyboardHook();
    }
}
