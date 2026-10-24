// ComputerBridge v2 - core: P/Invoke, DPI, virtual-screen geometry, windows, input primitives.
// Compiled together with Capture.cs, Uia.cs, Ocr.cs, Actions.cs (partial class). C# 5 syntax only (csc 4.x).
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static partial class ComputerBridge {
    // ---------------------------------------------------------------- P/Invoke
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetThreadDesktop(IntPtr hDesktop);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetThreadDesktop(uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, byte[] pvInfo, int nLength, out int lpnLengthNeeded);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseDesktop(IntPtr hDesktop);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int value);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern short VkKeyScan(char ch);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int dwProcessId);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT Point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint dwId, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);

    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION U; }
    [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    // ---------------------------------------------------------------- DPI / desktop
    private static bool _dpiInitialized = false;

    public static void EnsureDpiAware() {
        if (_dpiInitialized) return;
        _dpiInitialized = true;
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { } // PER_MONITOR_AWARE_V2
        try { SetProcessDpiAwareness(2); } catch { try { SetProcessDPIAware(); } catch { } }
    }

    public static string GetCurrentDesktopName() {
        int needed; byte[] buf = new byte[256];
        IntPtr hDesk = GetThreadDesktop(GetCurrentThreadId());
        if (GetUserObjectInformation(hDesk, 2, buf, buf.Length, out needed))
            return Encoding.ASCII.GetString(buf, 0, Math.Max(0, needed - 1)).TrimEnd('\0');
        return "";
    }

    public static bool IsOnDefaultDesktop() { return string.Equals(GetCurrentDesktopName(), "Default", StringComparison.OrdinalIgnoreCase); }

    public static int RunCommandOnDefaultDesktop(string cmdLine, uint waitMs) {
        STARTUPINFO si = new STARTUPINFO();
        si.cb = Marshal.SizeOf(si);
        si.lpDesktop = "Winsta0\\Default";
        si.dwFlags = 0x00000001; si.wShowWindow = 0;
        PROCESS_INFORMATION pi;
        if (!CreateProcess(null, cmdLine, IntPtr.Zero, IntPtr.Zero, false, 0x08000000, IntPtr.Zero, null, ref si, out pi)) return -1;
        WaitForSingleObject(pi.hProcess, waitMs);
        CloseHandle(pi.hProcess); CloseHandle(pi.hThread);
        return pi.dwProcessId;
    }

    private static void RunOnDesktop(Action action) {
        EnsureDpiAware();
        if (IsOnDefaultDesktop()) { action(); return; }
        IntPtr hDesktop = OpenDesktop("Default", 0, false, 0x01FF);
        Exception thrown = null;
        Thread t = new Thread(() => {
            if (hDesktop != IntPtr.Zero) SetThreadDesktop(hDesktop);
            EnsureDpiAware();
            try { action(); } catch (Exception ex) { thrown = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (hDesktop != IntPtr.Zero) CloseDesktop(hDesktop);
        if (thrown != null) throw thrown;
    }

    // ---------------------------------------------------------------- virtual screen (multi-monitor)
    private static int VX { get { return GetSystemMetrics(76); } }
    private static int VY { get { return GetSystemMetrics(77); } }
    private static int VW { get { int w = GetSystemMetrics(78); return w > 0 ? w : GetSystemMetrics(0); } }
    private static int VH { get { int h = GetSystemMetrics(79); return h > 0 ? h : GetSystemMetrics(1); } }

    private static bool InScreen(int x, int y) { return x >= VX && y >= VY && x < VX + VW && y < VY + VH; }

    private static string ScreenBoundsError(int x, int y) {
        return string.Format("ERROR: point ({0}, {1}) is outside the screen (virtual desktop x={2}..{3}, y={4}..{5}). Window may be minimized/off-screen - use 'focus' or 'windows'.",
            x, y, VX, VX + VW - 1, VY, VY + VH - 1);
    }

    // ---------------------------------------------------------------- window helpers
    private class WinInfo {
        public IntPtr H; public string Title; public string Cls; public RECT R; public bool Min; public bool Max; public uint Pid; public bool Fg;
        public int W { get { return R.Right - R.Left; } }
        public int Ht { get { return R.Bottom - R.Top; } }
    }

    private static string WinText(IntPtr h) { StringBuilder sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }
    private static string WinClass(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }

    private static List<WinInfo> EnumWindows(bool titledOnly) {
        var list = new List<WinInfo>();
        IntPtr hDesktop = OpenDesktop("Default", 0, false, 0x01FF);
        IntPtr fg = GetForegroundWindow();
        EnumDesktopWindows(hDesktop, (hWnd, lParam) => {
            if (!IsWindowVisible(hWnd)) return true;
            string title = WinText(hWnd);
            if (titledOnly && string.IsNullOrWhiteSpace(title)) return true;
            WinInfo wi = new WinInfo();
            wi.H = hWnd; wi.Title = title; wi.Cls = WinClass(hWnd);
            GetWindowRect(hWnd, out wi.R);
            wi.Min = IsIconic(hWnd); wi.Max = IsZoomed(hWnd); wi.Fg = (hWnd == fg);
            uint pid; GetWindowThreadProcessId(hWnd, out pid); wi.Pid = pid;
            list.Add(wi);
            return true;
        }, IntPtr.Zero);
        if (hDesktop != IntPtr.Zero) CloseDesktop(hDesktop);
        return list;
    }

    private static bool IsNoiseWindow(WinInfo w) {
        return w.Title == "Program Manager" || w.Title == "Microsoft Text Input Application" || w.Title == "DesktopWindowXamlSource" || w.Title == "OmApSvcBroker";
    }

    // Best match: exact > prefix > contains; then active, then restored windows, then larger area. Accepts "hwnd=12345".
    private static IntPtr FindWindowByTitle(string query, out string matchedTitle) {
        matchedTitle = "";
        if (string.IsNullOrEmpty(query)) return IntPtr.Zero;
        if (query.StartsWith("hwnd=", StringComparison.OrdinalIgnoreCase)) {
            long v; if (long.TryParse(query.Substring(5), out v)) {
                IntPtr h = new IntPtr(v);
                if (IsWindow(h)) { matchedTitle = WinText(h); return h; }
            }
            return IntPtr.Zero;
        }
        WinInfo best = null; long bestScore = -1;
        foreach (WinInfo w in EnumWindows(true)) {
            if (IsNoiseWindow(w)) continue;
            int rank = 0;
            if (string.Equals(w.Title, query, StringComparison.OrdinalIgnoreCase)) rank = 3;
            else if (w.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase)) rank = 2;
            else if (w.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) rank = 1;
            if (rank == 0) continue;
            if (!w.Min && w.W <= 1) continue;
            long area = Math.Min(99999L, (long)Math.Max(0, w.W) * Math.Max(0, w.Ht) / 100);
            long score = rank * 10000000L + (w.Fg ? 3000000L : 0) + (!w.Min ? 1000000L : 0) + area;
            if (score > bestScore) { bestScore = score; best = w; }
        }
        if (best == null) return IntPtr.Zero;
        matchedTitle = best.Title;
        return best.H;
    }

    private static bool ForceForeground(IntPtr hWnd) {
        if (hWnd == IntPtr.Zero) return false;
        try { AllowSetForegroundWindow(-1); } catch { }
        bool wasIconic = IsIconic(hWnd);
        if (wasIconic) { ShowWindow(hWnd, 9); Thread.Sleep(80); }
        IntPtr fg = GetForegroundWindow();
        if (fg == hWnd) return true;

        uint fgPid = 0, targetPid = 0;
        uint fgThread = GetWindowThreadProcessId(fg, out fgPid);
        uint targetThread = GetWindowThreadProcessId(hWnd, out targetPid);
        if (!wasIconic && fgPid != 0 && fgPid == targetPid) return true; // same process already active: keep open popups/menus

        uint curThread = GetCurrentThreadId();
        bool attachedFg = false, attachedTarget = false;
        try {
            if (fgThread != 0 && fgThread != curThread) attachedFg = AttachThreadInput(curThread, fgThread, true);
            if (targetThread != 0 && targetThread != curThread && targetThread != fgThread) attachedTarget = AttachThreadInput(curThread, targetThread, true);
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
        } finally {
            if (attachedTarget) AttachThreadInput(curThread, targetThread, false);
            if (attachedFg) AttachThreadInput(curThread, fgThread, false);
        }
        if (GetForegroundWindow() != hWnd) {
            byte altScan = (byte)MapVirtualKey(0x12, 0);
            keybd_event(0x12, altScan, 0, UIntPtr.Zero);
            keybd_event(0x12, altScan, 2, UIntPtr.Zero);
            try { SwitchToThisWindow(hWnd, true); } catch { }
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
        }
        // Guaranteed Z-order raise even when Windows ForegroundLockTimeout blocks SetForegroundWindow from CLI subshells:
        const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_SHOWWINDOW = 0x0040;
        SetWindowPos(hWnd, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW); // HWND_TOPMOST
        SetWindowPos(hWnd, new IntPtr(-2), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW); // HWND_NOTOPMOST
        SetForegroundWindow(hWnd);
        // brief settle so the first keystroke/click is not swallowed during activation
        for (int i = 0; i < 10 && GetForegroundWindow() != hWnd; i++) Thread.Sleep(10);
        return GetForegroundWindow() == hWnd;
    }

    private static void EnsureWindowAtPointActive(int x, int y) {
        try {
            POINT pt = new POINT { X = x, Y = y };
            IntPtr hWnd = WindowFromPoint(pt);
            if (hWnd == IntPtr.Zero) return;
            IntPtr root = GetAncestor(hWnd, 2);
            if (root == IntPtr.Zero) root = hWnd;
            string className = WinClass(root);
            if (className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd" || className == "Progman" || className == "WorkerW" || className == "#32768" ||
                className.IndexOf("Popup", StringComparison.OrdinalIgnoreCase) >= 0 || className == "NotifyIconOverflowWindow" || className == "TopLevelWindowForOverflowXamlIsland") return;
            IntPtr fg = GetForegroundWindow();
            if (fg == root) return;
            uint fgPid = 0, rootPid = 0;
            GetWindowThreadProcessId(fg, out fgPid); GetWindowThreadProcessId(root, out rootPid);
            if (fgPid != 0 && fgPid == rootPid) return;
            ForceForeground(root);
        } catch { }
    }

    // ---------------------------------------------------------------- low-level input primitives
    private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004, MOUSEEVENTF_RIGHTDOWN = 0x0008,
        MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_WHEEL = 0x0800,
        MOUSEEVENTF_HWHEEL = 0x1000, MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;

    private static void SendMouse(uint flags, int dx, int dy, uint data) {
        INPUT[] a = new INPUT[1];
        a[0].type = 0;
        a[0].U.mi.dx = dx; a[0].U.mi.dy = dy; a[0].U.mi.mouseData = data; a[0].U.mi.dwFlags = flags;
        SendInput(1, a, Marshal.SizeOf(typeof(INPUT)));
    }

    // Exact pixel move: generates a real hardware-level mouse-move event (apps like Blender/games need it) and then verifies / corrects the final position.
    private static bool MoveTo(int x, int y) {
        int vx = VX, vy = VY, vw = VW, vh = VH;
        int nx = (int)(((long)(x - vx) * 65536 + vw - 1) / vw);
        int ny = (int)(((long)(y - vy) * 65536 + vh - 1) / vh);
        SendMouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, nx, ny, 0);
        POINT p;
        for (int i = 0; i < 3; i++) {
            if (GetCursorPos(out p) && p.X == x && p.Y == y) return true;
            SetCursorPos(x, y);
        }
        return GetCursorPos(out p) && p.X == x && p.Y == y;
    }

    private static void MoveSmooth(int tx, int ty, int durationMs) {
        POINT s;
        if (durationMs <= 0 || !GetCursorPos(out s)) { MoveTo(tx, ty); return; }
        double dist = Math.Sqrt((double)(tx - s.X) * (tx - s.X) + (double)(ty - s.Y) * (ty - s.Y));
        if (dist < 6) { MoveTo(tx, ty); return; }
        int steps = Math.Max(4, Math.Min(60, (int)(dist / 14)));
        int sleep = Math.Max(1, durationMs / steps);
        for (int i = 1; i < steps; i++) {
            double t = (double)i / steps; double e = t * t * (3.0 - 2.0 * t);
            MoveTo((int)Math.Round(s.X + (tx - s.X) * e), (int)Math.Round(s.Y + (ty - s.Y) * e));
            Thread.Sleep(sleep);
        }
        MoveTo(tx, ty);
    }

    private static void ButtonFlags(string button, out uint down, out uint up) {
        down = MOUSEEVENTF_LEFTDOWN; up = MOUSEEVENTF_LEFTUP;
        if (string.Equals(button, "right", StringComparison.OrdinalIgnoreCase)) { down = MOUSEEVENTF_RIGHTDOWN; up = MOUSEEVENTF_RIGHTUP; }
        else if (string.Equals(button, "middle", StringComparison.OrdinalIgnoreCase)) { down = MOUSEEVENTF_MIDDLEDOWN; up = MOUSEEVENTF_MIDDLEUP; }
    }

    private static void ModKeys(string mods, bool press) {
        if (string.IsNullOrEmpty(mods)) return;
        string[] parts = mods.ToLower().Split(new char[] { '+', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (!press) Array.Reverse(parts);
        foreach (string m in parts) {
            byte vk = ResolveVirtualKey(m.Trim());
            if (vk == 0) continue;
            byte scan = (byte)MapVirtualKey((uint)vk, 0);
            keybd_event(vk, scan, press ? 0u : 2u, UIntPtr.Zero);
        }
        if (press) Thread.Sleep(10);
    }

    // Core click used by every click-flavoured action. Returns "" on success, or an ERROR string.
    private static string ClickCore(int x, int y, string button, int count, string mods, int moveMs, bool activate) {
        if (!InScreen(x, y)) return ScreenBoundsError(x, y);
        if (activate) EnsureWindowAtPointActive(x, y);
        if (moveMs > 0) MoveSmooth(x, y, moveMs); else MoveTo(x, y);
        Thread.Sleep(10); // hover settle: lets the target app register WM_MOUSEMOVE / hover state before the press
        if (ProbeHit) LastHit = DescribePoint(x, y); // what is really under the cursor right now (reported back to the caller)
        uint down, up; ButtonFlags(button, out down, out up);
        ModKeys(mods, true);
        for (int i = 0; i < count; i++) {
            SendMouse(down, 0, 0, 0);
            Thread.Sleep(8);
            SendMouse(up, 0, 0, 0);
            if (i + 1 < count) Thread.Sleep(30);
        }
        ModKeys(mods, false);
        return "";
    }

    // ---------------------------------------------------------------- keyboard primitives
    private static void SendUnicodeBatch(List<INPUT> batch) {
        if (batch.Count == 0) return;
        SendInput((uint)batch.Count, batch.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        batch.Clear();
    }

    private static void AddUnicode(List<INPUT> batch, char ch) {
        INPUT d = new INPUT(); d.type = 1; d.U.ki.wVk = 0; d.U.ki.wScan = (ushort)ch; d.U.ki.dwFlags = 0x0004;
        INPUT u = new INPUT(); u.type = 1; u.U.ki.wVk = 0; u.U.ki.wScan = (ushort)ch; u.U.ki.dwFlags = 0x0004 | 0x0002;
        batch.Add(d); batch.Add(u);
    }

    private static void AddVk(List<INPUT> batch, ushort vk) {
        ushort scan = (ushort)MapVirtualKey(vk, 0);
        INPUT d = new INPUT(); d.type = 1; d.U.ki.wVk = vk; d.U.ki.wScan = scan; d.U.ki.dwFlags = 0;
        INPUT u = new INPUT(); u.type = 1; u.U.ki.wVk = vk; u.U.ki.wScan = scan; u.U.ki.dwFlags = 0x0002;
        batch.Add(d); batch.Add(u);
    }

    // Batched typing: 1 SendInput call per ~48 chars (v1 was 1 call + 6ms sleep per char).
    public static string TypeText(string text) {
        RunOnDesktop(() => {
            var batch = new List<INPUT>();
            foreach (char ch in text) {
                if (ch == '\r') continue;
                if (ch == '\n') AddVk(batch, 0x0D);
                else if (ch == '\t') AddVk(batch, 0x09);
                else AddUnicode(batch, ch);
                if (batch.Count >= 96) { SendUnicodeBatch(batch); Thread.Sleep(2); }
            }
            SendUnicodeBatch(batch);
        });
        return "Typed " + text.Length + " chars";
    }

    private static bool IsExtendedKey(byte vk) {
        return (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E || vk == 0x5B || vk == 0x5C || vk == 0x6F || vk == 0x90;
    }

    private static byte ResolveVirtualKey(string k) {
        switch (k) {
            case "ctrl": case "control": case "lctrl": return 0x11;
            case "shift": case "lshift": return 0x10;
            case "alt": case "menu": case "lalt": return 0x12;
            case "win": case "lwin": case "super": case "meta": case "cmd": case "command": return 0x5B;
            case "enter": case "return": return 0x0D;
            case "esc": case "escape": return 0x1B;
            case "tab": return 0x09;
            case "space": case "spacebar": return 0x20;
            case "backspace": case "bksp": case "bs": return 0x08;
            case "delete": case "del": return 0x2E;
            case "insert": case "ins": return 0x2D;
            case "home": return 0x24;
            case "end": return 0x23;
            case "pageup": case "pgup": return 0x21;
            case "pagedown": case "pgdn": return 0x22;
            case "left": return 0x25;
            case "up": return 0x26;
            case "right": return 0x27;
            case "down": return 0x28;
            case "capslock": case "caps": return 0x14;
            case "numlock": return 0x90;
            case "printscreen": case "prtsc": return 0x2C;
            case "apps": case "contextmenu": return 0x5D;
            case "f1": return 0x70; case "f2": return 0x71; case "f3": return 0x72; case "f4": return 0x73;
            case "f5": return 0x74; case "f6": return 0x75; case "f7": return 0x76; case "f8": return 0x77;
            case "f9": return 0x78; case "f10": return 0x79; case "f11": return 0x7A; case "f12": return 0x7B;
            case "volup": return 0xAF; case "voldown": return 0xAE; case "mute": return 0xAD;
            case "playpause": return 0xB3; case "nexttrack": return 0xB0; case "prevtrack": return 0xB1;
            case "plus": case "=": return 0xBB;
            case "minus": case "-": return 0xBD;
            case "period": case ".": return 0xBE;
            case "comma": case ",": return 0xBC;
            case "slash": case "/": return 0xBF;
            case "backslash": case "\\": return 0xDC;
            case "semicolon": case ";": return 0xBA;
            case "quote": case "'": return 0xDE;
            case "backtick": case "`": return 0xC0;
            case "lbracket": case "[": return 0xDB;
            case "rbracket": case "]": return 0xDD;
            default:
                if (k.Length == 1) {
                    if (char.IsLetterOrDigit(k[0])) return (byte)char.ToUpperInvariant(k[0]);
                    return (byte)(VkKeyScan(k[0]) & 0xFF);
                }
                if (k.StartsWith("num") && k.Length == 4 && char.IsDigit(k[3])) return (byte)(0x60 + (k[3] - '0'));
                return 0;
        }
    }

    // combo syntax: "ctrl+shift+t", optional repeat suffix "down*5"
    public static string SendKey(string combo) {
        string err = null;
        RunOnDesktop(() => {
            string spec = combo; int repeat = 1;
            int star = combo.LastIndexOf('*');
            if (star > 0) { int r; if (int.TryParse(combo.Substring(star + 1), out r) && r > 0) { repeat = Math.Min(r, 500); spec = combo.Substring(0, star); } }
            string[] parts = spec.ToLower().Split('+');
            List<byte> vks = new List<byte>();
            foreach (var p in parts) {
                string tok = p.Trim();
                if (tok.Length == 0) continue;
                byte vk = ResolveVirtualKey(tok);
                if (vk == 0) { err = "ERROR: unknown key '" + tok + "' in combo '" + combo + "'"; return; }
                vks.Add(vk);
            }
            if (vks.Count == 0) { err = "ERROR: empty key combo"; return; }
            for (int n = 0; n < repeat; n++) {
                foreach (var vk in vks) {
                    byte scan = (byte)MapVirtualKey((uint)vk, 0);
                    keybd_event(vk, scan, IsExtendedKey(vk) ? 0x0001u : 0u, UIntPtr.Zero);
                }
                Thread.Sleep(12);
                for (int i = vks.Count - 1; i >= 0; i--) {
                    byte vk = vks[i]; byte scan = (byte)MapVirtualKey((uint)vk, 0);
                    keybd_event(vk, scan, 0x0002u | (IsExtendedKey(vk) ? 0x0001u : 0u), UIntPtr.Zero);
                }
                if (repeat > 1) Thread.Sleep(8);
            }
        });
        return err != null ? err : "Pressed: " + combo;
    }

    public static string PasteText(string text) {
        string err = null;
        RunOnDesktop(() => {
            string prev = null; bool hadText = false;
            try { if (Clipboard.ContainsText()) { prev = Clipboard.GetText(); hadText = true; } } catch { }
            bool set = false;
            for (int i = 0; i < 5 && !set; i++) { try { Clipboard.SetText(text); set = true; } catch { Thread.Sleep(30); } }
            if (!set) { err = "ERROR: could not access clipboard"; return; }
            Thread.Sleep(25);
            byte ctrlScan = (byte)MapVirtualKey(0x11, 0), vScan = (byte)MapVirtualKey(0x56, 0);
            keybd_event(0x11, ctrlScan, 0, UIntPtr.Zero);
            keybd_event(0x56, vScan, 0, UIntPtr.Zero);
            Thread.Sleep(15);
            keybd_event(0x56, vScan, 2, UIntPtr.Zero);
            keybd_event(0x11, ctrlScan, 2, UIntPtr.Zero);
            Thread.Sleep(120); // let the target app read the clipboard before restoring it
            if (hadText) { try { Clipboard.SetText(prev); } catch { } }
        });
        return err != null ? err : "Pasted " + text.Length + " chars";
    }
}
