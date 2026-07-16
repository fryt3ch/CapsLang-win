#:property OutputType=WinExe
#:property TargetFramework=net10.0
#:property AllowUnsafeBlocks=true

using System.Runtime.InteropServices;

internal static partial class CapsLang
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;

    private const ushort VK_CAPITAL = 0x14;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_SPACE = 0x20;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const int LLKHF_INJECTED = 0x10;

    private const uint WM_TIMER = 0x0113;

    private static readonly IntPtr CAPS_INJECTION_TAG = new(0x43415053);

    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const uint INPUTLANGCHANGE_FORWARD = 2;

    private static int _longPressMs = 300;
    private static bool _useWinSpace;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)]
        public uint type;

        [FieldOffset(8)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
    private static partial IntPtr GetModuleHandle(IntPtr lpModuleName);

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int nVirtKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial IntPtr DispatchMessage(in MSG lpMsg);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool KillTimer(IntPtr hWnd, IntPtr uIDEvent);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [LibraryImport("user32.dll")]
    private static partial int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private static IntPtr _hookID = IntPtr.Zero;
    private static readonly LowLevelKeyboardProc _proc = HookCallback;

    private static readonly INPUT[] _languageToggleInputs = CreateLanguageToggleInputs();

    private static bool _capsIsDown;
    private static bool _longPressTriggered;
    private static IntPtr _longPressTimerId;

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && int.TryParse(args[0], out var ms) && ms >= 0)
            _longPressMs = ms;

        if (args.Length > 1 && args[1].Equals("win+space", StringComparison.OrdinalIgnoreCase))
            _useWinSpace = true;

        using var mutex = new Mutex(true, "CapsLang_NET_Mutex", out var createdNew);

        if (!createdNew)
            return;

        _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(IntPtr.Zero), 0);

        if (_hookID != IntPtr.Zero)
        {
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_TIMER && msg.wParam == _longPressTimerId)
                {
                    if (_capsIsDown)
                    {
                        _longPressTriggered = true;
                        KillTimer(IntPtr.Zero, _longPressTimerId);
                        _longPressTimerId = IntPtr.Zero;
                        ToggleCapsLock();
                    }
                }
                else
                {
                    TranslateMessage(in msg);
                    DispatchMessage(in msg);
                }
            }

            UnhookWindowsHookEx(_hookID);
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var ks = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            if (ks.vkCode == VK_CAPITAL)
            {
                if ((ks.flags & LLKHF_INJECTED) != 0 && ks.dwExtraInfo == CAPS_INJECTION_TAG)
                    return CallNextHookEx(_hookID, nCode, wParam, lParam);

                if (GetKeyState(VK_SHIFT) < 0)
                    return CallNextHookEx(_hookID, nCode, wParam, lParam);

                if (wParam == WM_KEYDOWN)
                {
                    if (_longPressMs > 0)
                    {
                        if (!_capsIsDown)
                        {
                            _capsIsDown = true;
                            _longPressTriggered = false;
                            _longPressTimerId = SetTimer(IntPtr.Zero, IntPtr.Zero, (uint)_longPressMs, IntPtr.Zero);
                        }
                    }
                    else
                    {
                        ToggleInputLanguage();
                    }

                    return 1;
                }

                if (_longPressMs > 0 && _capsIsDown)
                {
                    _capsIsDown = false;

                    if (_longPressTimerId != IntPtr.Zero)
                    {
                        KillTimer(IntPtr.Zero, _longPressTimerId);
                        _longPressTimerId = IntPtr.Zero;
                    }

                    if (!_longPressTriggered)
                        ToggleInputLanguage();

                    return 1;
                }
            }
        }

        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private static void ToggleLanguage()
    {
        var inputs = _languageToggleInputs;

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT CreateKeyInput(ushort vk, uint flags)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
    }

    private static INPUT[] CreateLanguageToggleInputs()
    {
        return
        [
            CreateKeyInput(VK_LWIN, 0),
            CreateKeyInput(VK_SPACE, 0),
            CreateKeyInput(VK_SPACE, KEYEVENTF_KEYUP),
            CreateKeyInput(VK_LWIN, KEYEVENTF_KEYUP),
        ];
    }

    private static void ToggleInputLanguage()
    {
        if (_useWinSpace)
            ToggleLanguage();
        else
            SwitchLanguage();
    }

    private static void SwitchLanguage()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return;

        var threadId = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
        var currentLayout = GetKeyboardLayout(threadId);

        var count = GetKeyboardLayoutList(0, []);
        if (count <= 1)
            return;

        var layouts = new IntPtr[count];
        count = GetKeyboardLayoutList(count, layouts);
        if (count <= 1)
            return;

        IntPtr nextLayout = IntPtr.Zero;
        for (int i = 0; i < count; i++)
        {
            if (layouts[i] == currentLayout)
            {
                nextLayout = layouts[(i + 1) % count];
                break;
            }
        }

        if (nextLayout != IntPtr.Zero)
            PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, (IntPtr)INPUTLANGCHANGE_FORWARD, nextLayout);
    }

    private static void ToggleCapsLock()
    {
        INPUT[] inputs =
        [
            new INPUT
            {
                type = INPUT_KEYBOARD,
                ki = new KEYBDINPUT
                {
                    wVk = VK_CAPITAL,
                    dwFlags = 0,
                    dwExtraInfo = CAPS_INJECTION_TAG
                }
            },
            new INPUT
            {
                type = INPUT_KEYBOARD,
                ki = new KEYBDINPUT
                {
                    wVk = VK_CAPITAL,
                    dwFlags = KEYEVENTF_KEYUP,
                    dwExtraInfo = CAPS_INJECTION_TAG
                }
            }
        ];

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }
}
