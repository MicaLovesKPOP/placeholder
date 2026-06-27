using System;
using System.Runtime.InteropServices;
using System.Text;

namespace InputFlow.Windows
{
    /// <summary>
    /// Exposes native Win32 APIs used for enumerating and switching keyboard layouts and input methods.
    /// </summary>
    public static class InputApis
    {
        /// <summary>
        /// Retrieves the keyboard layouts available for the system or the calling thread.
        /// </summary>
        /// <param name="nBuff">Number of handles that <paramref name="list"/> can contain.</param>
        /// <param name="list">An array that receives the input locale identifiers (HKLs).</param>
        /// <returns>The number of handles copied to the buffer or, if nBuff is zero, the number of locales available.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] list);

        /// <summary>
        /// Retrieves the name of the active keyboard layout.
        /// </summary>
        /// <param name="pwszKLID">A pointer to a buffer that receives the 8-character KLID string.</param>
        /// <returns>True if successful; otherwise false.</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetKeyboardLayoutName(StringBuilder pwszKLID);

        /// <summary>
        /// Retrieves the active input locale identifier (HKL) for the specified thread.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetKeyboardLayout(uint idThread);

        /// <summary>
        /// Loads the specified keyboard layout into the system.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

        /// <summary>
        /// Activates a loaded keyboard layout for the calling thread.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint Flags);

        /// <summary>
        /// Sets the default input language for the system or the current thread.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        public const uint SPI_SETDEFAULTINPUTLANG = 0x005A;
        public const uint SPIF_SENDWININICHANGE = 0x02;
        public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        public const uint KLF_ACTIVATE = 0x00000001;
        public const uint KLF_SETFORPROCESS = 0x00000100;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            IntPtr lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);

        public const int IME_CMODE_NATIVE = 0x0001;
        public const int IME_CMODE_FULLSHAPE = 0x0008;

        [DllImport("imm32.dll")]
        public static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        public static extern bool ImmGetConversionStatus(IntPtr hIMC, out int conversion, out int sentence);

        [DllImport("imm32.dll")]
        public static extern bool ImmSetConversionStatus(IntPtr hIMC, int conversion, int sentence);

        [DllImport("imm32.dll")]
        public static extern bool ImmGetOpenStatus(IntPtr hIMC);

        [DllImport("imm32.dll")]
        public static extern bool ImmSetOpenStatus(IntPtr hIMC, bool open);

        public const uint WM_IME_CONTROL = 0x0283;
        public const int IMC_GETCONVERSIONMODE = 0x0001;
        public const int IMC_SETCONVERSIONMODE = 0x0002;
        public const int IMC_GETOPENSTATUS = 0x0005;
        public const int IMC_SETOPENSTATUS = 0x0006;

        [DllImport("imm32.dll")]
        public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Retrieves a handle to the foreground window.
        /// </summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        /// <summary>
        /// Retrieves the identifier of the thread that created the specified window and the identifier of the process that created the window.
        /// </summary>
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        /// <summary>
        /// Obtains the full path of the executable file of the specified process.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] StringBuilder lpFilename, int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint PROCESS_VM_READ = 0x0010;

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public UIntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
