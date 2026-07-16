#:property OutputType=WinExe
#:property TargetFramework=net10.0
#:property AllowUnsafeBlocks=true

using System.Runtime.InteropServices;

internal static unsafe partial class CapsLang
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;

    private const int VK_CAPITAL = 0x14;
    private const int VK_SHIFT = 0x10;
    private const int VK_LWIN = 0x5B;
    private const int VK_SPACE = 0x20;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int LLKHF_INJECTED = 0x10;

    private static readonly nint CAPS_INJECTION_TAG = 0x43415053;

    private static int _longPressMs = 300;

    private static int _capsState;
    private static Timer? _longPressTimer;

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);
    private static readonly LowLevelKeyboardProc _hookProc = HookCallback;

    private static readonly INPUT[] _winSpaceInputs = BuildWinSpaceInputs();

    // ---- structs ----

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    // ---- P/Invoke ----

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
    private static partial nint GetModuleHandle(nint lpModuleName);

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int nVirtKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(in MSG lpMsg);

    // ---- entry ----

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && int.TryParse(args[0], out var ms) && ms >= 0)
            _longPressMs = ms;

        using var mutex = new Mutex(true, "CapsLang_NET_Mutex", out var createdNew);
        if (!createdNew)
            return;

        var hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(0), 0);
        if (hookId == 0)
            return;

        while (GetMessage(out var msg, 0, 0, 0))
        {
            TranslateMessage(in msg);
            DispatchMessage(in msg);
        }

        _longPressTimer?.Dispose();
        UnhookWindowsHookEx(hookId);
    }

    // ---- hook callback ----

    private static nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(0, nCode, wParam, lParam);

        var ks = (KBDLLHOOKSTRUCT*)lParam;

        if (ks->vkCode != VK_CAPITAL)
            return CallNextHookEx(0, nCode, wParam, lParam);

        if ((ks->flags & LLKHF_INJECTED) != 0 && ks->dwExtraInfo == CAPS_INJECTION_TAG)
            return CallNextHookEx(0, nCode, wParam, lParam);

        if (_longPressMs == 0 && GetKeyState(VK_SHIFT) < 0)
            return CallNextHookEx(0, nCode, wParam, lParam);

        if (wParam == WM_KEYDOWN)
            OnCapsKeyDown();
        else
            OnCapsKeyUp();

        return 1;
    }

    // ---- state machine ----

    private static void OnCapsKeyDown()
    {
        if (_longPressMs == 0)
        {
            ToggleInputLanguage();
            return;
        }

        if (Interlocked.CompareExchange(ref _capsState, 1, 0) != 0)
            return;

        _longPressTimer?.Dispose();
        _longPressTimer = new Timer(
            _ =>
            {
                if (Interlocked.CompareExchange(ref _capsState, 2, 1) == 1)
                    ToggleCapsLock();
            },
            null,
            _longPressMs,
            Timeout.Infinite);
    }

    private static void OnCapsKeyUp()
    {
        if (_longPressMs == 0)
            return;

        var prevState = Interlocked.Exchange(ref _capsState, 0);
        _longPressTimer?.Dispose();
        _longPressTimer = null;

        if (prevState == 1)
            ToggleInputLanguage();
    }

    // ---- language switching ----

    private static void ToggleInputLanguage()
    {
        SendInput((uint)_winSpaceInputs.Length, _winSpaceInputs, Marshal.SizeOf<INPUT>());
    }

    // ---- caps lock toggle ----

    private static void ToggleCapsLock()
    {
        INPUT[] inputs =
        [
            MakeKeyInput(VK_CAPITAL, 0, CAPS_INJECTION_TAG),
            MakeKeyInput(VK_CAPITAL, KEYEVENTF_KEYUP, CAPS_INJECTION_TAG),
        ];

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    // ---- helpers ----

    private static INPUT[] BuildWinSpaceInputs()
    {
        return
        [
            MakeKeyInput(VK_LWIN, 0, 0),
            MakeKeyInput(VK_SPACE, 0, 0),
            MakeKeyInput(VK_SPACE, KEYEVENTF_KEYUP, 0),
            MakeKeyInput(VK_LWIN, KEYEVENTF_KEYUP, 0),
        ];
    }

    private static INPUT MakeKeyInput(ushort vk, uint flags, nint extraInfo)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = flags,
                dwExtraInfo = extraInfo,
            },
        };
    }
}
