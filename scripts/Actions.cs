// ComputerBridge v2 - action dispatcher, drag/scroll/window actions, chain pipeline, process entry point with watchdog.
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;
using System.Collections.Generic;

public static partial class ComputerBridge {
    private static readonly HashSet<string> OptKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "out", "filter", "region", "window", "scale", "fmt", "quality", "timeout", "index", "type", "max", "cursor", "zoom", "speed",
        "button", "count", "mod", "radius", "step", "duration"
    };
    // actions whose free text must never be interpreted as key=value options
    private static readonly HashSet<string> RawTextActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "type", "paste", "key", "clip-set", "type-into", "search-at", "launch" };

    private static string O(Dictionary<string, string> o, string k, string def) { string v; return o.TryGetValue(k, out v) ? v : def; }
    private static int OI(Dictionary<string, string> o, string k, int def) { string v; int r; return (o.TryGetValue(k, out v) && int.TryParse(v, out r)) ? r : def; }
    private static double OD(Dictionary<string, string> o, string k, double def) { string v; double r; return (o.TryGetValue(k, out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r)) ? r : def; }

    private static bool Truthy(string s) { return s != null && s != "0" && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) && !string.Equals(s, "no", StringComparison.OrdinalIgnoreCase); }

    // Parses trailing click modifiers: right/middle/double/triple/ctrl/shift/alt/nohit  (+ button= count= mod= options)
    private static void ClickFlags(List<string> pos, int from, Dictionary<string, string> opt, out string button, out int count, out string mods, out bool probe) {
        button = O(opt, "button", "left"); count = OI(opt, "count", 1); mods = O(opt, "mod", ""); probe = Environment.GetEnvironmentVariable("CU_NOHIT") != "1";
        List<string> m = new List<string>();
        for (int i = from; i < pos.Count; i++) {
            string p = pos[i].ToLower();
            if (p == "right" || p == "middle" || p == "left") button = p;
            else if (p == "double" || p == "2") count = 2;
            else if (p == "triple" || p == "3") count = 3;
            else if (p == "ctrl" || p == "shift" || p == "alt" || p == "win") m.Add(p);
            else if (p == "nohit") probe = false;
        }
        if (m.Count > 0) mods = string.IsNullOrEmpty(mods) ? string.Join("+", m.ToArray()) : mods + "+" + string.Join("+", m.ToArray());
    }

    private static string DefaultShotPath(string fmt) {
        string outDir = Path.Combine(Path.GetTempPath(), "agy-computer-use");
        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
        return Path.Combine(outDir, string.Format("shot_{0}.{1}", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"), (fmt == "jpg" || fmt == "jpeg") ? "jpg" : "png"));
    }

    // ---------------------------------------------------------------- remaining mouse/window actions
    public static string Drag(List<Point> pts, string button, string mods, int durationMs) {
        string result = "";
        RunOnDesktop(() => {
            foreach (Point p in pts) if (!InScreen(p.X, p.Y)) { result = ScreenBoundsError(p.X, p.Y); return; }
            EnsureWindowAtPointActive(pts[0].X, pts[0].Y);
            MoveTo(pts[0].X, pts[0].Y);
            Thread.Sleep(40);
            uint down, up; ButtonFlags(button, out down, out up);
            ModKeys(mods, true);
            SendMouse(down, 0, 0, 0);
            Thread.Sleep(60);
            double total = 0;
            for (int i = 1; i < pts.Count; i++) total += Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2));
            int dur = durationMs > 0 ? durationMs : (int)Math.Max(120, Math.Min(700, total / 3));
            for (int i = 1; i < pts.Count; i++) {
                double seg = Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2));
                int segMs = total > 0 ? (int)(dur * seg / total) : 0;
                int steps = Math.Max(4, Math.Min(80, (int)(seg / 10)));
                for (int s = 1; s <= steps; s++) {
                    double t = (double)s / steps; double e = t * t * (3.0 - 2.0 * t);
                    MoveTo((int)Math.Round(pts[i - 1].X + (pts[i].X - pts[i - 1].X) * e), (int)Math.Round(pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * e));
                    Thread.Sleep(Math.Max(1, segMs / steps));
                }
            }
            Thread.Sleep(50);
            SendMouse(up, 0, 0, 0);
            ModKeys(mods, false);
            Point a = pts[0], b = pts[pts.Count - 1];
            result = string.Format("Dragged ({0}, {1}) -> ({2}, {3}){4}", a.X, a.Y, b.X, b.Y, pts.Count > 2 ? " via " + (pts.Count - 2) + " waypoint(s)" : "");
        });
        return result;
    }

    public static string MouseButton(int x, int y, string button, bool press) {
        string result = "";
        RunOnDesktop(() => {
            if (x >= 0 && y >= 0) { if (!InScreen(x, y)) { result = ScreenBoundsError(x, y); return; } if (press) EnsureWindowAtPointActive(x, y); MoveTo(x, y); Thread.Sleep(15); }
            uint down, up; ButtonFlags(button, out down, out up);
            SendMouse(press ? down : up, 0, 0, 0);
            result = string.Format("Mouse {0} {1}", button, press ? "down" : "up");
        });
        return result;
    }

    public static string Scroll(string direction, int ticks, int x, int y) {
        string result = "";
        RunOnDesktop(() => {
            if (x >= 0 && y >= 0) {
                if (!InScreen(x, y)) { result = ScreenBoundsError(x, y); return; }
                EnsureWindowAtPointActive(x, y);
                MoveTo(x, y); Thread.Sleep(15);
            }
            string dir = direction.ToLower().Trim();
            uint flag; int sign;
            if (dir == "down") { flag = MOUSEEVENTF_WHEEL; sign = -1; }
            else if (dir == "up") { flag = MOUSEEVENTF_WHEEL; sign = 1; }
            else if (dir == "right") { flag = MOUSEEVENTF_HWHEEL; sign = 1; }
            else if (dir == "left") { flag = MOUSEEVENTF_HWHEEL; sign = -1; }
            else { result = "ERROR: scroll direction must be up|down|left|right"; return; }
            int n = Math.Max(1, Math.Min(200, ticks));
            for (int i = 0; i < n; i++) { SendMouse(flag, 0, 0, (uint)(sign * 120)); if (n > 1) Thread.Sleep(5); } // one notch per event: honoured by Blender/games that ignore large deltas
            POINT p; GetCursorPos(out p);
            result = string.Format("Scrolled {0} ({1} ticks) at ({2}, {3})", dir, n, p.X, p.Y);
        });
        return result;
    }

    public static string FocusWindow(string titleSubstring) {
        string result = "ERROR: window not found: '" + titleSubstring + "' (run 'windows')";
        RunOnDesktop(() => {
            string title; IntPtr hWnd = FindWindowByTitle(titleSubstring, out title);
            if (hWnd == IntPtr.Zero) return;
            bool ok = ForceForeground(hWnd);
            RECT r; GetWindowRect(hWnd, out r);
            result = string.Format("Focused: {0} | rect={1},{2},{3},{4}{5}", title, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, ok ? "" : " [WARNING: Windows refused foreground activation; window may still be behind others]");
        });
        return result;
    }

    public static string WindowAction(string titleSubstring, string op, List<string> extra) {
        string result = "ERROR: window not found: '" + titleSubstring + "' (run 'windows')";
        RunOnDesktop(() => {
            string title; IntPtr hWnd = FindWindowByTitle(titleSubstring, out title);
            if (hWnd == IntPtr.Zero) return;
            string action = op.ToLower().Trim();
            if (action == "maximize" || action == "max") { ForceForeground(hWnd); ShowWindow(hWnd, 3); result = "Maximized: " + title; }
            else if (action == "minimize" || action == "min") { ShowWindow(hWnd, 6); result = "Minimized: " + title; }
            else if (action == "restore") { ShowWindow(hWnd, 9); ForceForeground(hWnd); result = "Restored: " + title; }
            else if (action == "close") { PostMessage(hWnd, 0x0010, IntPtr.Zero, IntPtr.Zero); result = "Closed: " + title; }
            else if (action == "center") {
                ShowWindow(hWnd, 9);
                int nw = (int)(VW * 0.8), nh = (int)(VH * 0.8);
                MoveWindow(hWnd, VX + (VW - nw) / 2, VY + (VH - nh) / 2, nw, nh, true);
                ForceForeground(hWnd); result = "Centered: " + title;
            } else if (action == "move" || action == "resize") {
                if (extra.Count < 4) { result = "ERROR: usage: window TITLE move X Y W H"; return; }
                ShowWindow(hWnd, 9);
                MoveWindow(hWnd, int.Parse(extra[0]), int.Parse(extra[1]), int.Parse(extra[2]), int.Parse(extra[3]), true);
                result = string.Format("Moved: {0} -> {1},{2} {3}x{4}", title, extra[0], extra[1], extra[2], extra[3]);
            } else result = "ERROR: unknown window action: " + op + " (maximize|minimize|restore|close|center|move X Y W H)";
        });
        return result;
    }

    public static List<string> ListWindows() {
        var list = new List<string>();
        RunOnDesktop(() => {
            foreach (WinInfo w in EnumWindows(true)) {
                if (IsNoiseWindow(w) || w.W <= 10 || w.Ht <= 10) continue;
                string state = w.Min ? "minimized" : (w.Max ? "maximized" : "normal");
                if (w.Fg) state = "ACTIVE," + state;
                list.Add(string.Format("{0} | rect={1},{2},{3},{4} | center={5},{6} | state={7} | hwnd={8}", w.Title, w.R.Left, w.R.Top, w.W, w.Ht, w.R.Left + w.W / 2, w.R.Top + w.Ht / 2, state, w.H));
            }
        });
        return list;
    }

    public static string GetCursorInfo() {
        string res = "0,0";
        RunOnDesktop(() => {
            POINT p; GetCursorPos(out p);
            string d = DescribePoint(p.X, p.Y);
            res = string.Format("{0},{1}{2}", p.X, p.Y, d.Length > 0 ? " -> " + d : "");
        });
        return res;
    }

    private static List<string> TokenizeStep(string step) {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(step)) return tokens;
        StringBuilder cur = new StringBuilder(); char quote = '\0'; bool had = false;
        for (int i = 0; i < step.Length; i++) {
            char c = step[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; else cur.Append(c); }
            else {
                if (c == '"' || c == '\'') { quote = c; had = true; }
                else if (char.IsWhiteSpace(c)) { if (cur.Length > 0 || had) { tokens.Add(cur.ToString()); cur.Clear(); had = false; } }
                else cur.Append(c);
            }
        }
        if (cur.Length > 0 || had) tokens.Add(cur.ToString());
        return tokens;
    }

    private static string Help() {
        return @"computer.exe <action> [args]   (all coordinates = physical SCREEN pixels, multi-monitor aware)

SEE (fast & accurate)
  snap [WINDOW] [max=150] [all] [ocr]   screenshot with NUMBERED boxes on every clickable element + id list  -> then click-id N
  screenshot [grid] [region=x,y,w,h] [window=T] [scale=N] [fmt=jpg] [out=P] [cursor=0]
  zoom X Y [radius=80] [scale=4]        magnified, coordinate-gridded crop around a point (verify exact pixels)
  ui [WINDOW] [filter=TEXT]             list UI elements with exact center=X,Y   |   find TEXT [WINDOW] [type=Button]  ranked matches
  ocr [window=T|region=x,y,w,h] [find=TEXT] [words]   read on-screen text with coordinates (Windows OCR; for apps without UIA)
  windows | monitors | cursor | pixel X Y | read-ui NAME [WINDOW] | doctor

ACT (semantic first)
  click-id N [right|double|ctrl|shift|alt]   hover-id N   type-id N TEXT   search-id N TEXT   (ids from the last snap)
  click-text TEXT [WINDOW] [type=Button] [index=N] [timeout=1200] [right|double]   waits for the element, ranks matches, reports what was hit
  click-ocr TEXT [window=T|region=..] [index=N]      click text found by OCR
  invoke TEXT [WINDOW]                  UIA Invoke/Select/Toggle/Expand (no mouse; works on background windows)
  type-ui NAME TEXT [WINDOW] | search-ui NAME TEXT [WINDOW] | set-ui NAME TEXT [WINDOW]  (set-ui writes the value directly)
  click X Y [right|middle|double|triple|ctrl|shift|alt|nohit]   double X Y   right-click X Y   move X Y
  drag X1 Y1 X2 Y2 [X3 Y3 ...] [button=right] [mod=ctrl] [duration=ms]     mouse-down [X Y] / mouse-up [X Y]
  scroll <up|down|left|right> [ticks] [X Y]
  click-window TITLE

KEYBOARD
  type TEXT...   paste TEXT...   key COMBO [COMBO2..] (e.g. ctrl+shift+t, down*5)   type-into X Y TEXT   search-at X Y TEXT   clip-get | clip-set TEXT

WINDOWS & FLOW
  focus TITLE | window TITLE <maximize|minimize|restore|close|center|move X Y W H> | launch APP [args]
  wait MS | wait-for TEXT [WINDOW] [timeout=5000] | wait-gone TEXT [WINDOW] | wait-window TITLE [timeout=]
  chain ""step1"" ""step2"" ...        one process, stops at the first ERROR (use --keep-going to continue)";
    }

    public static string ExecuteAction(string action, List<string> args) {
        string act = (action ?? "help").ToLower().Trim();
        var pos = new List<string>();
        var opt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool raw = RawTextActions.Contains(act);
        foreach (var a in args) {
            int eq = a.IndexOf('=');
            if (!raw && eq > 0 && OptKeys.Contains(a.Substring(0, eq).Trim())) opt[a.Substring(0, eq).Trim()] = a.Substring(eq + 1).Trim();
            else pos.Add(a);
        }
        string button, mods; int count; bool probe;

        switch (act) {
            case "help": case "--help": case "-h": return Help();

            case "doctor": {
                string uiaMs = "n/a";
                try { Stopwatch sw = Stopwatch.StartNew(); IntPtr fg = GetForegroundWindow(); string d, e; var l = CollectScope("", false, false, out d, out e); uiaMs = (l == null ? 0 : l.Count) + " elements in " + sw.ElapsedMilliseconds + "ms (" + d + ")"; } catch (Exception ex) { uiaMs = "FAILED: " + ex.Message; }
                return string.Format("OK: ComputerBridge v2 on desktop '{0}', Per-Monitor-V2 DPI aware.\n{1}\nUIA scan of foreground: {2}\nOCR: {3}", GetCurrentDesktopName(), DescribeMonitors(), uiaMs,
                    WT("Windows.Media.Ocr.OcrEngine", "Windows.Media.Ocr") != null ? "available (Windows.Media.Ocr)" : "NOT available");
            }

            case "monitors": return DescribeMonitors();

            case "screenshot": case "shot": case "screen": {
                bool grid = pos.Exists(s => string.Equals(s, "grid", StringComparison.OrdinalIgnoreCase));
                string fmt = O(opt, "fmt", "png").ToLower();
                string path = O(opt, "out", DefaultShotPath(fmt));
                Rectangle region = ParseRegion(O(opt, "region", ""));
                string win = O(opt, "window", "");
                if (win.Length > 0) {
                    string e2; region = OcrArea(win, Rectangle.Empty, out e2);
                    if (e2 != null) return e2;
                }
                return Screenshot(path, grid, region, OD(opt, "scale", 1.0), fmt, OI(opt, "quality", 85), Truthy(O(opt, "cursor", "1")), null);
            }

            case "zoom": {
                if (pos.Count < 2) throw new ArgumentException("usage: zoom X Y [radius=80] [scale=4]");
                int zx = int.Parse(pos[0]), zy = int.Parse(pos[1]), r = OI(opt, "radius", 80);
                double sc = OD(opt, "scale", Math.Min(8.0, 640.0 / (2 * r)));
                string fmt = O(opt, "fmt", "png").ToLower();
                return Screenshot(O(opt, "out", DefaultShotPath(fmt)), true, new Rectangle(zx - r, zy - r, 2 * r, 2 * r), sc, fmt, 90, true, null);
            }

            case "snap": case "see": case "marks": {
                string win = pos.Count > 0 && !string.Equals(pos[0], "all", StringComparison.OrdinalIgnoreCase) && !string.Equals(pos[0], "ocr", StringComparison.OrdinalIgnoreCase) ? pos[0] : O(opt, "window", "");
                bool all = pos.Exists(s => string.Equals(s, "all", StringComparison.OrdinalIgnoreCase));
                bool withOcr = pos.Exists(s => string.Equals(s, "ocr", StringComparison.OrdinalIgnoreCase));
                string fmt = O(opt, "fmt", "png").ToLower();
                return Snap(win, OI(opt, "max", 150), all, withOcr, O(opt, "out", DefaultShotPath(fmt)), OD(opt, "scale", 1.0), fmt, OI(opt, "quality", 85));
            }

            case "click-id": case "hover-id": case "type-id": case "search-id": {
                if (pos.Count < 1) throw new ArgumentException("usage: " + act + " N" + (act.EndsWith("type-id") || act == "search-id" ? " TEXT" : ""));
                int id = int.Parse(pos[0].TrimStart('#'));
                ClickFlags(pos, 1, opt, out button, out count, out mods, out probe);
                if (act == "hover-id") return ClickId(id, button, count, mods, probe, null, false, true);
                if (act == "type-id" || act == "search-id") {
                    if (pos.Count < 2) throw new ArgumentException("usage: " + act + " N TEXT");
                    return ClickId(id, button, 1, "", false, string.Join(" ", pos.GetRange(1, pos.Count - 1).ToArray()), act == "search-id", false);
                }
                return ClickId(id, button, count, mods, probe, null, false, false);
            }

            case "windows": {
                var wins = ListWindows();
                if (wins.Count == 0) return "No visible windows found.";
                StringBuilder sb = new StringBuilder();
                foreach (var w in wins) sb.AppendLine("- " + w);
                return sb.ToString().TrimEnd();
            }

            case "ui": case "elements": case "inspect": {
                string winTitle = pos.Count > 0 ? pos[0] : O(opt, "window", "");
                string filter = opt.ContainsKey("filter") ? opt["filter"] : (pos.Count > 1 ? pos[1] : "");
                var els = ListUIElements(winTitle, filter);
                if (els.Count == 0) return "No matching UI elements found.";
                StringBuilder sb = new StringBuilder();
                foreach (var e in els) sb.AppendLine(e);
                return sb.ToString().TrimEnd();
            }

            case "find": {
                if (pos.Count < 1) throw new ArgumentException("usage: find TEXT [WINDOW] [type=Button]");
                return FindUI(pos[0], pos.Count > 1 ? pos[1] : O(opt, "window", ""), O(opt, "type", ""), OI(opt, "timeout", 600));
            }

            case "cursor": return "cursor=" + GetCursorInfo();

            case "pixel": {
                if (pos.Count < 2) throw new ArgumentException("usage: pixel X Y");
                return PixelColor(int.Parse(pos[0]), int.Parse(pos[1]));
            }

            case "focus":
                if (pos.Count < 1) throw new ArgumentException("usage: focus TITLE");
                return FocusWindow(string.Join(" ", pos.ToArray()));

            case "window": {
                if (pos.Count < 2) throw new ArgumentException("usage: window TITLE <maximize|minimize|restore|close|center|move X Y W H>");
                return WindowAction(pos[0], pos[1], pos.GetRange(2, pos.Count - 2));
            }

            case "click-window":
                if (pos.Count < 1) throw new ArgumentException("usage: click-window TITLE");
                return ClickWindow(string.Join(" ", pos.ToArray()));

            case "click-text": case "click-el": case "click-element": case "click-ui": {
                if (pos.Count < 1) throw new ArgumentException("usage: click-text TEXT [WINDOW_TITLE] [type=Button] [index=N]");
                string win = pos.Count > 1 && !IsClickWord(pos[1]) ? pos[1] : O(opt, "window", "");
                ClickFlags(pos, 1, opt, out button, out count, out mods, out probe);
                return ClickUIElement(pos[0], win, O(opt, "type", ""), OI(opt, "index", 0), OI(opt, "timeout", 1200), button, count, mods, probe);
            }

            case "invoke": {
                if (pos.Count < 1) throw new ArgumentException("usage: invoke TEXT [WINDOW_TITLE]");
                return InvokeUIElement(pos[0], pos.Count > 1 ? pos[1] : O(opt, "window", ""), O(opt, "type", ""), OI(opt, "index", 0), OI(opt, "timeout", 1200));
            }

            case "read-ui": case "get-ui": {
                if (pos.Count < 1) throw new ArgumentException("usage: read-ui NAME [WINDOW_TITLE]");
                return ReadUIValue(pos[0], pos.Count > 1 ? pos[1] : O(opt, "window", ""), O(opt, "type", ""), OI(opt, "index", 0), OI(opt, "timeout", 800));
            }

            case "set-ui": {
                if (pos.Count < 2) throw new ArgumentException("usage: set-ui NAME TEXT [WINDOW_TITLE]");
                return SetUIValue(pos[0], pos[1], pos.Count > 2 ? pos[2] : O(opt, "window", ""), O(opt, "type", ""), OI(opt, "index", 0), OI(opt, "timeout", 1200));
            }

            case "type-ui": case "search-ui": {
                if (pos.Count < 2) throw new ArgumentException("usage: " + act + " ELEMENT_NAME TEXT [WINDOW_TITLE]");
                return TypeIntoUIElement(pos[0], pos[1], pos.Count > 2 ? pos[2] : O(opt, "window", ""), act == "search-ui", O(opt, "type", ""), OI(opt, "index", 0), OI(opt, "timeout", 1200));
            }

            case "ocr": {
                bool words = pos.Exists(s => string.Equals(s, "words", StringComparison.OrdinalIgnoreCase));
                return OcrDump(pos.Count > 0 && !string.Equals(pos[0], "words", StringComparison.OrdinalIgnoreCase) ? pos[0] : O(opt, "window", ""), ParseRegion(O(opt, "region", "")), O(opt, "filter", O(opt, "find", "")), words, OD(opt, "zoom", 0));
            }

            case "click-ocr": {
                if (pos.Count < 1) throw new ArgumentException("usage: click-ocr TEXT [window=T] [region=x,y,w,h] [index=N]");
                string win = pos.Count > 1 && !IsClickWord(pos[1]) ? pos[1] : O(opt, "window", "");
                ClickFlags(pos, 1, opt, out button, out count, out mods, out probe);
                return ClickOcr(pos[0], win, ParseRegion(O(opt, "region", "")), OI(opt, "index", 0), button, count, mods, probe, OD(opt, "zoom", 0));
            }

            case "click": {
                if (pos.Count < 2) throw new ArgumentException("usage: click X Y [right|middle|double|triple|ctrl|shift|alt|nohit]");
                ClickFlags(pos, 2, opt, out button, out count, out mods, out probe);
                return Click(int.Parse(pos[0]), int.Parse(pos[1]), button, count, mods, OI(opt, "speed", 0), probe);
            }

            case "double": case "double-click": {
                if (pos.Count < 2) throw new ArgumentException("usage: double X Y");
                ClickFlags(pos, 2, opt, out button, out count, out mods, out probe);
                return Click(int.Parse(pos[0]), int.Parse(pos[1]), button, 2, mods, 0, probe);
            }

            case "triple": case "triple-click": {
                if (pos.Count < 2) throw new ArgumentException("usage: triple X Y");
                ClickFlags(pos, 2, opt, out button, out count, out mods, out probe);
                return Click(int.Parse(pos[0]), int.Parse(pos[1]), button, 3, mods, 0, probe);
            }

            case "right-click": {
                if (pos.Count < 2) throw new ArgumentException("usage: right-click X Y");
                ClickFlags(pos, 2, opt, out button, out count, out mods, out probe);
                return Click(int.Parse(pos[0]), int.Parse(pos[1]), "right", 1, mods, 0, probe);
            }

            case "move": case "hover": {
                if (pos.Count < 2) throw new ArgumentException("usage: move X Y [speed=ms]");
                return Click(int.Parse(pos[0]), int.Parse(pos[1]), "none", 0, "", OI(opt, "speed", 0), Environment.GetEnvironmentVariable("CU_NOHIT") != "1");
            }

            case "drag": {
                var ints = new List<int>();
                var rest = new List<string>();
                foreach (string p in pos) { int v; if (int.TryParse(p, out v)) ints.Add(v); else rest.Add(p); }
                if (ints.Count < 4 || ints.Count % 2 != 0) throw new ArgumentException("usage: drag X1 Y1 X2 Y2 [X3 Y3 ...] [button=right] [mod=ctrl] [duration=ms]");
                var pts = new List<Point>();
                for (int i = 0; i < ints.Count; i += 2) pts.Add(new Point(ints[i], ints[i + 1]));
                ClickFlags(rest, 0, opt, out button, out count, out mods, out probe);
                return Drag(pts, button, mods, OI(opt, "duration", 0));
            }

            case "mouse-down": case "mouse-up": {
                int mx = pos.Count >= 2 && char.IsDigit(pos[0][0]) ? int.Parse(pos[0]) : -1, my = pos.Count >= 2 && char.IsDigit(pos[1][0]) ? int.Parse(pos[1]) : -1;
                ClickFlags(pos, 0, opt, out button, out count, out mods, out probe);
                return MouseButton(mx, my, button, act == "mouse-down");
            }

            case "scroll": {
                if (pos.Count < 1) throw new ArgumentException("usage: scroll <up|down|left|right> [ticks] [X Y]");
                int ticks = pos.Count > 1 ? int.Parse(pos[1]) : OI(opt, "count", 3);
                int sx = pos.Count > 3 ? int.Parse(pos[2]) : -1, sy = pos.Count > 3 ? int.Parse(pos[3]) : -1;
                return Scroll(pos[0], ticks, sx, sy);
            }

            case "type": {
                if (args.Count < 1) throw new ArgumentException("usage: type TEXT...");
                return TypeText(string.Join(" ", args.ToArray()));
            }

            case "paste": {
                if (args.Count < 1) throw new ArgumentException("usage: paste TEXT...");
                return PasteText(string.Join(" ", args.ToArray()));
            }

            case "clip-get": {
                string t = "";
                RunOnDesktop(() => { try { t = Clipboard.ContainsText() ? Clipboard.GetText() : "<clipboard has no text>"; } catch (Exception ex) { t = "ERROR: " + ex.Message; } });
                return t;
            }

            case "clip-set": {
                if (args.Count < 1) throw new ArgumentException("usage: clip-set TEXT...");
                string t = string.Join(" ", args.ToArray()), res = "Clipboard set (" + t.Length + " chars)";
                RunOnDesktop(() => { try { Clipboard.SetText(t); } catch (Exception ex) { res = "ERROR: " + ex.Message; } });
                return res;
            }

            case "type-into": case "search-at": {
                if (args.Count < 3) throw new ArgumentException("usage: " + act + " X Y TEXT...");
                return TypeInto(int.Parse(args[0]), int.Parse(args[1]), string.Join(" ", args.GetRange(2, args.Count - 2).ToArray()), act == "search-at", true);
            }

            case "key": {
                if (args.Count < 1) throw new ArgumentException("usage: key COMBO [COMBO2...]");
                List<string> res = new List<string>();
                foreach (var k in args) {
                    string r = SendKey(k);
                    res.Add(r);
                    if (r.StartsWith("ERROR")) break;
                    if (args.Count > 1) Thread.Sleep(25);
                }
                return string.Join("; ", res.ToArray());
            }

            case "wait": case "sleep": {
                int ms = pos.Count > 0 ? int.Parse(pos[0]) : 500;
                Thread.Sleep(ms);
                return string.Format("Waited {0}ms", ms);
            }

            case "wait-for": case "wait-text": case "wait-gone": {
                if (pos.Count < 1) throw new ArgumentException("usage: " + act + " TEXT [WINDOW_TITLE] [timeout=5000]");
                return WaitFor(pos[0], pos.Count > 1 ? pos[1] : O(opt, "window", ""), OI(opt, "timeout", 5000), act == "wait-gone", O(opt, "type", ""));
            }

            case "wait-window": {
                if (pos.Count < 1) throw new ArgumentException("usage: wait-window TITLE [timeout=8000]");
                return WaitWindow(pos[0], OI(opt, "timeout", 8000));
            }

            case "launch": {
                if (args.Count < 1) throw new ArgumentException("usage: launch APP_OR_PATH [args...]");
                string appArgs = args.Count > 1 ? string.Join(" ", args.GetRange(1, args.Count - 1).ToArray()) : "";
                Process.Start(args[0], appArgs);
                Thread.Sleep(300);
                return "Launched " + args[0];
            }

            case "chain": case "batch": {
                if (args.Count < 1) throw new ArgumentException("usage: chain \"step1\" \"step2\" ...");
                bool keepGoing = false; int n = 0;
                StringBuilder outSb = new StringBuilder();
                foreach (var step in args) {
                    n++;
                    if (step == "--keep-going") { keepGoing = true; continue; }
                    var tokens = TokenizeStep(step);
                    if (tokens.Count == 0) continue;
                    string r;
                    try { r = ExecuteAction(tokens[0], tokens.GetRange(1, tokens.Count - 1)); } catch (Exception ex) { r = "ERROR: " + ex.Message; }
                    outSb.AppendLine(r);
                    if (r.StartsWith("ERROR") && !keepGoing) { outSb.AppendLine("CHAIN STOPPED at step " + n + " ('" + step + "') - later steps were NOT executed."); break; }
                    Thread.Sleep(15);
                }
                return outSb.ToString().TrimEnd();
            }

            default:
                return "ERROR: unknown action '" + action + "'. Run with 'help'.";
        }
    }

    private static bool IsClickWord(string s) {
        string p = s.ToLower();
        return p == "right" || p == "middle" || p == "left" || p == "double" || p == "triple" || p == "ctrl" || p == "shift" || p == "alt" || p == "nohit";
    }

    private static string QuoteArg(string arg) {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (arg.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return arg;
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    [STAThread]
    public static int Main(string[] rawArgs) {
        EnsureDpiAware();
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        try {
            List<string> args = new List<string>(rawArgs ?? new string[0]);
            string outFile = null; bool forcedDefault = false;
            while (args.Count > 0 && args[0].StartsWith("--") && args[0] != "--help" && args[0] != "--keep-going") {
                if (args[0] == "--on-default") { forcedDefault = true; args.RemoveAt(0); }
                else if (args[0] == "--out" && args.Count > 1) { outFile = args[1]; args.RemoveRange(0, 2); }
                else break;
            }

            // Sandbox desktop (e.g. exebox-*): respawn this exe directly on Winsta0\Default
            if (!forcedDefault && !IsOnDefaultDesktop()) {
                string outDir = Path.Combine(Path.GetTempPath(), "agy-computer-use");
                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                string tmpOut = Path.Combine(outDir, "out_" + Guid.NewGuid().ToString("N") + ".txt");
                StringBuilder cmd = new StringBuilder();
                cmd.Append(QuoteArg(System.Reflection.Assembly.GetExecutingAssembly().Location));
                cmd.Append(" --on-default --out ").Append(QuoteArg(tmpOut));
                foreach (var a in args) cmd.Append(" ").Append(QuoteArg(a));
                RunCommandOnDefaultDesktop(cmd.ToString(), 60000);
                if (File.Exists(tmpOut)) {
                    string output = File.ReadAllText(tmpOut, Encoding.UTF8);
                    Console.Write(output);
                    try { File.Delete(tmpOut); } catch { }
                    return output.StartsWith("ERROR") ? 1 : 0;
                }
                return 0;
            }

            string action = args.Count > 0 ? args[0] : "help";
            List<string> rest = args.Count > 1 ? args.GetRange(1, args.Count - 1) : new List<string>();

            // Watchdog: a hung UIA provider (frozen app) must never hang the agent. CU_TIMEOUT seconds (default 60).
            string res = null; Exception fail = null;
            int tmo; if (!int.TryParse(Environment.GetEnvironmentVariable("CU_TIMEOUT"), out tmo) || tmo <= 0) tmo = 60;
            Thread worker = new Thread(() => { try { res = ExecuteAction(action, rest); } catch (Exception ex) { fail = ex; } });
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            if (!worker.Join(tmo * 1000)) {
                Console.Error.WriteLine("ERROR: timed out after " + tmo + "s (target app's accessibility provider may be hung)");
                Environment.Exit(2);
            }
            if (fail != null) res = "ERROR: " + fail.Message;

            if (!string.IsNullOrEmpty(outFile)) File.WriteAllText(outFile, res + Environment.NewLine, new UTF8Encoding(false));
            else Console.WriteLine(res);
            return (res != null && res.StartsWith("ERROR")) ? 1 : 0;
        } catch (Exception ex) {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }
}
