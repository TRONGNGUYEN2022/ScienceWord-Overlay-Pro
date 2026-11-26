using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ScienceWordOverlay.Utils
{
    public class OfficeDoubleClickHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const uint GA_ROOT = 2;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT pt);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelMouseProc? _proc;
        private int _lastClickTick = 0;
        private POINT _lastClickPt;

        public event Action<string, IntPtr, int, int>? TargetAppDoubleClicked;

        public void Install()
        {
            if (_hookId != IntPtr.Zero) return;
            _proc = HookCallback;
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var (procName, rootHwnd) = GetSupportedAppAtPoint(info.pt);
                    if (procName != null)
                    {
                        int now = Environment.TickCount;
                        int dt = unchecked(now - _lastClickTick);
                        uint dblTime = GetDoubleClickTime();
                        if (_lastClickTick != 0 && dt >= 0 && dt <= dblTime &&
                            Math.Abs(info.pt.x - _lastClickPt.x) <= 10 &&
                            Math.Abs(info.pt.y - _lastClickPt.y) <= 10)
                        {
                            _lastClickTick = 0;
                            TargetAppDoubleClicked?.Invoke(procName, rootHwnd, info.pt.x, info.pt.y);
                        }
                        else
                        {
                            _lastClickTick = now;
                            _lastClickPt = info.pt;
                        }
                    }
                    else
                    {
                        _lastClickTick = 0;
                    }
                }
            }
            catch { }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static (string? proc, IntPtr rootHwnd) GetSupportedAppAtPoint(POINT pt)
        {
            IntPtr hWnd = WindowFromPoint(pt);
            if (hWnd == IntPtr.Zero) return (null, IntPtr.Zero);
            IntPtr root = GetAncestor(hWnd, GA_ROOT);
            if (root == IntPtr.Zero) root = hWnd;

            var sb = new StringBuilder(64);
            if (GetClassName(root, sb, sb.Capacity) > 0)
            {
                string cls = sb.ToString();
                if (cls.Equals("OpusApp", StringComparison.OrdinalIgnoreCase)) return ("WINWORD", root);
                if (cls.Equals("PPTFrameClass", StringComparison.OrdinalIgnoreCase)) return ("POWERPNT", root);
                if (cls.Equals("XLMAIN", StringComparison.OrdinalIgnoreCase)) return ("EXCEL", root);
            }

            try
            {
                GetWindowThreadProcessId(root, out uint pid);
                using var p = Process.GetProcessById((int)pid);
                string up = p.ProcessName.ToUpperInvariant();
                if (up.Contains("SCIENCEWORD") || up == "SW")
                    return (p.ProcessName, root);
            }
            catch { }

            return (null, IntPtr.Zero);
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
    }
}
