// ComputerBridge v2 - UI Automation: cached bulk reads (10x faster than v1), ranked matching, wait-for, invoke/value patterns,
// set-of-marks snapshots (numbered boxes + click-id), hit probing.
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Windows.Automation;

public static partial class ComputerBridge {
    private class El {
        public int Id; public string Type = "", Name = "", AutoId = "", Win = "";
        public int X, Y, W, H, Cx, Cy;
        public bool Enabled = true, Interactive;
        public AutomationElement Ref;
    }

    private class Match { public El E; public int Score; public int NameDelta; public long Area; }

    private static readonly HashSet<string> InteractiveTypes = new HashSet<string> {
        "Button", "RadioButton", "CheckBox", "Edit", "TabItem", "MenuItem", "Hyperlink", "ComboBox", "ListItem", "TreeItem", "SplitButton", "Slider", "Spinner", "DataItem", "HeaderItem"
    };
    private static readonly HashSet<string> ContainerTypes = new HashSet<string> {
        "Window", "Pane", "Group", "ScrollBar", "Thumb", "TitleBar", "ToolBar", "Separator", "Header", "Table", "List", "Tree", "Menu", "MenuBar", "StatusBar", "Tab", "Custom", "Image", "Text", "Document"
    };

    private static bool ProbeHit = true;
    private static string LastHit = "";
    private static HashSet<IntPtr> _wokenWindows = new HashSet<IntPtr>();

    private static string Norm(string s) {
        if (string.IsNullOrEmpty(s)) return "";
        StringBuilder sb = new StringBuilder(s.Length);
        bool sp = false;
        foreach (char c in s) {
            if (c == '&') continue;
            if (char.IsWhiteSpace(c) || c == '\u00A0') { sp = true; continue; }
            if (sp && sb.Length > 0) sb.Append(' ');
            sp = false;
            sb.Append(char.ToLowerInvariant(c));
        }
        string r = sb.ToString();
        if (r.EndsWith("...")) r = r.Substring(0, r.Length - 3);
        if (r.EndsWith("\u2026")) r = r.Substring(0, r.Length - 1);
        return r.Trim();
    }

    private static bool ContainsWord(string hay, string needle) {
        int i = 0;
        while ((i = hay.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) {
            bool l = i == 0 || !char.IsLetterOrDigit(hay[i - 1]);
            bool r = i + needle.Length >= hay.Length || !char.IsLetterOrDigit(hay[i + needle.Length]);
            if (l && r) return true;
            i++;
        }
        return false;
    }

    private static void WakeUpBrowserAccessibility(IntPtr hWnd) {
        if (hWnd == IntPtr.Zero || _wokenWindows.Contains(hWnd)) return;
        _wokenWindows.Add(hWnd);
        try {
            Guid iid = new Guid("{618736E0-3C3D-11CF-810C-00AA00389B71}");
            object acc;
            AccessibleObjectFromWindow(hWnd, 0xFFFFFFFCu, ref iid, out acc);
            EnumChildWindows(hWnd, (child, l) => {
                string cn = WinClass(child);
                if (cn.IndexOf("Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase) >= 0 || cn.IndexOf("Intermediate D3D Window", StringComparison.OrdinalIgnoreCase) >= 0) {
                    object childAcc;
                    AccessibleObjectFromWindow(child, 0xFFFFFFFCu, ref iid, out childAcc);
                    try { AutomationElement.FromHandle(child); } catch { }
                }
                return true;
            }, IntPtr.Zero);
        } catch { }
    }

    // One cross-process call fetches every property for the whole subtree (v1 made ~6 calls PER element).
    private static List<El> CollectFromRoot(AutomationElement root, Rectangle clip, bool needRef, string winLabel) {
        var res = new List<El>();
        if (root == null || clip.Width <= 0 || clip.Height <= 0) return res;
        CacheRequest cr = new CacheRequest();
        cr.AutomationElementMode = needRef ? AutomationElementMode.Full : AutomationElementMode.None;
        cr.Add(AutomationElement.NameProperty);
        cr.Add(AutomationElement.ControlTypeProperty);
        cr.Add(AutomationElement.BoundingRectangleProperty);
        cr.Add(AutomationElement.IsOffscreenProperty);
        cr.Add(AutomationElement.AutomationIdProperty);
        cr.Add(AutomationElement.IsEnabledProperty);
        AutomationElementCollection col = null;
        try {
            using (cr.Activate()) { col = root.FindAll(TreeScope.Descendants, Automation.ControlViewCondition); }
        } catch { return res; }
        if (col == null) return res;
        var seen = new HashSet<string>();
        foreach (AutomationElement el in col) {
            try {
                AutomationElement.AutomationElementInformation c = el.Cached;
                if (c.IsOffscreen) continue;
                System.Windows.Rect r = c.BoundingRectangle;
                if (r.IsEmpty || double.IsInfinity(r.Left) || double.IsNaN(r.Left) || double.IsInfinity(r.Width)) continue;
                int l = Math.Max((int)Math.Round(r.Left), clip.Left), t = Math.Max((int)Math.Round(r.Top), clip.Top);
                int rr = Math.Min((int)Math.Round(r.Right), clip.Right), bb = Math.Min((int)Math.Round(r.Bottom), clip.Bottom);
                if (rr - l < 3 || bb - t < 3) continue; // fully clipped away / too small to click
                string type = c.ControlType != null ? c.ControlType.ProgrammaticName.Replace("ControlType.", "") : "";
                string name = (c.Name ?? "").Trim().Replace("\r", " ").Replace("\n", " ");
                El e = new El();
                e.Type = type; e.Name = name; e.AutoId = c.AutomationId ?? ""; e.Enabled = c.IsEnabled; e.Win = winLabel;
                e.X = l; e.Y = t; e.W = rr - l; e.H = bb - t;
                e.Cx = l + (rr - l) / 2; e.Cy = t + (bb - t) / 2;
                e.Interactive = InteractiveTypes.Contains(type);
                e.Ref = needRef ? el : null;
                string key = type + "|" + name + "|" + e.Cx + "," + e.Cy;
                if (!seen.Add(key)) continue;
                res.Add(e);
            } catch { }
        }
        return res;
    }

    // Foreground window (+ its popups/menus), or the named window. Returns null + err when the window cannot be found.
    private static List<El> CollectScope(string windowTitle, bool needRef, bool focus, out string desc, out string err) {
        err = null; desc = "";
        var all = new List<El>();
        List<IntPtr> hwnds = new List<IntPtr>();
        if (!string.IsNullOrWhiteSpace(windowTitle)) {
            string matched;
            IntPtr h = FindWindowByTitle(windowTitle, out matched);
            if (h == IntPtr.Zero) { err = "ERROR: window not found: '" + windowTitle + "' (run 'windows' to list titles)"; return null; }
            if (focus) ForceForeground(h);
            hwnds.Add(h); desc = "'" + matched + "'";
        } else {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero) {
                hwnds.Add(fg); desc = "'" + WinText(fg) + "' (foreground)";
                foreach (WinInfo w in EnumWindows(false)) {
                    if (w.H == fg || w.Min) continue;
                    IntPtr owner = GetWindow(w.H, 4); // GW_OWNER
                    if (owner == fg || w.Cls == "#32768") hwnds.Add(w.H);
                }
            }
        }
        if (hwnds.Count == 0) {
            Rectangle scr = new Rectangle(VX, VY, VW, VH);
            all.AddRange(CollectFromRoot(AutomationElement.RootElement, scr, needRef, "desktop"));
            desc = "desktop";
            return all;
        }
        foreach (IntPtr h in hwnds) {
            RECT wr; GetWindowRect(h, out wr);
            Rectangle clip = ClipToScreen(new Rectangle(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top));
            bool first = !_wokenWindows.Contains(h);
            WakeUpBrowserAccessibility(h);
            AutomationElement root = null;
            try { root = AutomationElement.FromHandle(h); } catch { }
            string label = WinText(h);
            var part = CollectFromRoot(root, clip, needRef, label);
            if (first && part.Count < 40 && WinClass(h).StartsWith("Chrome_WidgetWin", StringComparison.Ordinal)) {
                Thread.Sleep(250); // browser builds its accessibility tree lazily after the first wake-up
                part = CollectFromRoot(root, clip, needRef, label);
            }
            all.AddRange(part);
        }
        return all;
    }

    private static int ScoreName(El e, string tNorm, string tRaw) {
        string n = Norm(e.Name);
        int s = 0;
        if (n.Length > 0 && n == tNorm) s = e.Interactive ? 100 : 85;
        else if (e.AutoId.Length > 0 && string.Equals(e.AutoId, tRaw, StringComparison.OrdinalIgnoreCase)) s = 95;
        else if (n.Length > 0 && n.StartsWith(tNorm, StringComparison.Ordinal)) s = e.Interactive ? 75 : 60;
        else if (n.Length > 0 && ContainsWord(n, tNorm)) s = e.Interactive ? 65 : 50;
        else if (n.Length > 0 && n.IndexOf(tNorm, StringComparison.Ordinal) >= 0) s = e.Interactive ? 55 : 40;
        if (s > 0 && !e.Enabled) s -= 15;
        return s;
    }

    private static List<Match> RankMatches(List<El> pool, string text, string typeFilter, bool preferInput) {
        string tn = Norm(text);
        var ms = new List<Match>();
        foreach (El e in pool) {
            if (e.Type == "Window" || e.Type == "ScrollBar" || e.Type == "Thumb") continue;
            if (!string.IsNullOrEmpty(typeFilter) && !string.Equals(e.Type, typeFilter, StringComparison.OrdinalIgnoreCase)) continue;
            int s = ScoreName(e, tn, text);
            if (s <= 0) continue;
            bool isInput = e.Type == "Edit" || e.Type == "ComboBox" || e.Type == "Document";
            if (preferInput && isInput) s += 30;
            Match m = new Match(); m.E = e; m.Score = s; m.NameDelta = Math.Abs(Norm(e.Name).Length - tn.Length); m.Area = (long)e.W * e.H;
            ms.Add(m);
        }
        ms.Sort((a, b) => {
            if (a.Score != b.Score) return b.Score.CompareTo(a.Score);
            if (a.NameDelta != b.NameDelta) return a.NameDelta.CompareTo(b.NameDelta);
            if (a.Area != b.Area) return a.Area.CompareTo(b.Area);
            if (a.E.Y != b.E.Y) return a.E.Y.CompareTo(b.E.Y);
            return a.E.X.CompareTo(b.E.X);
        });
        return ms;
    }

    private static string NearMisses(List<El> pool, string text) {
        string tn = Norm(text);
        string[] toks = tn.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var hits = new List<string>(); var seen = new HashSet<string>();
        foreach (El e in pool) {
            if (e.Name.Length == 0 || e.Type == "Window") continue;
            string n = Norm(e.Name);
            bool near = false;
            foreach (string t in toks) if (t.Length >= 3 && n.IndexOf(t, StringComparison.Ordinal) >= 0) near = true;
            if (!near && n.Length >= 3 && tn.IndexOf(n, StringComparison.Ordinal) >= 0) near = true;
            if (near && seen.Add(e.Type + e.Name)) hits.Add("[" + e.Type + "] '" + (e.Name.Length > 50 ? e.Name.Substring(0, 47) + "..." : e.Name) + "'");
            if (hits.Count >= 8) break;
        }
        if (hits.Count == 0) {
            foreach (El e in pool) {
                if (!e.Interactive || e.Name.Length == 0) continue;
                if (seen.Add(e.Type + e.Name)) hits.Add("[" + e.Type + "] '" + (e.Name.Length > 40 ? e.Name.Substring(0, 37) + "..." : e.Name) + "'");
                if (hits.Count >= 8) break;
            }
            return hits.Count == 0 ? "" : " Visible interactive elements: " + string.Join(", ", hits.ToArray());
        }
        return " Did you mean: " + string.Join(", ", hits.ToArray());
    }

    // Poll until a match appears (replaces blind 'wait 1500' steps). Falls back to a desktop-wide search at the end if no window was given.
    private static List<Match> FindMatches(string windowTitle, string text, string typeFilter, bool preferInput, int timeoutMs, bool needRef, bool focus, out string scopeDesc, out string err, out List<El> lastPool) {
        Stopwatch sw = Stopwatch.StartNew();
        List<Match> ms = new List<Match>();
        scopeDesc = ""; err = null; lastPool = new List<El>();
        bool firstPass = true;
        while (true) {
            string d, e;
            List<El> pool = CollectScope(windowTitle, needRef, focus && firstPass, out d, out e);
            if (pool == null) { err = e; return ms; }
            scopeDesc = d; lastPool = pool;
            ms = RankMatches(pool, text, typeFilter, preferInput);
            if (ms.Count > 0) return ms;
            firstPass = false;
            if (sw.ElapsedMilliseconds >= timeoutMs) break;
            Thread.Sleep(90);
        }
        if (string.IsNullOrWhiteSpace(windowTitle)) {
            var desk = CollectFromRoot(AutomationElement.RootElement, new Rectangle(VX, VY, VW, VH), needRef, "desktop");
            var dm = RankMatches(desk, text, typeFilter, preferInput);
            if (dm.Count > 0) { scopeDesc = "desktop (fallback)"; lastPool = desk; return dm; }
        }
        return ms;
    }

    private static string NotFound(string what, string scopeDesc, List<El> pool, string text) {
        return "ERROR: UI element not found: '" + what + "' in " + scopeDesc + " (searched " + pool.Count + " elements)." + NearMisses(pool, text);
    }

    private static string DescribePoint(int x, int y) {
        string win = "", el = "";
        try {
            IntPtr h = WindowFromPoint(new POINT { X = x, Y = y });
            if (h != IntPtr.Zero) { IntPtr root = GetAncestor(h, 2); if (root == IntPtr.Zero) root = h; win = WinText(root); if (win.Length == 0) win = "<" + WinClass(root) + ">"; }
        } catch { }
        try {
            AutomationElement e = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (e != null) {
                string t = e.Current.ControlType != null ? e.Current.ControlType.ProgrammaticName.Replace("ControlType.", "") : "";
                string n = (e.Current.Name ?? "").Trim().Replace("\r", " ").Replace("\n", " ");
                if (n.Length > 60) n = n.Substring(0, 57) + "...";
                el = "[" + t + "] '" + n + "'";
            }
        } catch { }
        if (el.Length == 0 && win.Length == 0) return "";
        return (el.Length > 0 ? el : "[?]") + " in '" + (win.Length > 60 ? win.Substring(0, 57) + "..." : win) + "'";
    }

    // ---------------------------------------------------------------- click flavours (all report what was under the cursor)
    public static string Click(int x, int y, string button, int count, string mods, int moveMs, bool probe) {
        string result = "";
        RunOnDesktop(() => {
            if (count <= 0 || string.Equals(button, "none", StringComparison.OrdinalIgnoreCase)) {
                if (!InScreen(x, y)) { result = ScreenBoundsError(x, y); return; }
                if (moveMs > 0) MoveSmooth(x, y, moveMs); else MoveTo(x, y);
                Thread.Sleep(15);
                string h0 = probe ? DescribePoint(x, y) : "";
                result = string.Format("Moved to ({0}, {1}){2}", x, y, h0.Length > 0 ? " -> " + h0 : "");
                return;
            }
            LastHit = ""; ProbeHit = probe;
            string err = ClickCore(x, y, button, count, mods, moveMs, true);
            if (err.Length > 0) { result = err; return; }
            POINT p; GetCursorPos(out p);
            string warn = (p.X != x || p.Y != y) ? string.Format(" [WARNING: cursor ended at ({0},{1})]", p.X, p.Y) : "";
            result = string.Format("Clicked {0}{1} at ({2}, {3}) x{4}{5}{6}", button, string.IsNullOrEmpty(mods) ? "" : "+" + mods, x, y, count, LastHit.Length > 0 ? " -> " + LastHit : "", warn);
        });
        return result;
    }

    public static string ClickWindow(string titleSubstring) {
        string result = "ERROR: window not found: " + titleSubstring;
        RunOnDesktop(() => {
            string title;
            IntPtr hWnd = FindWindowByTitle(titleSubstring, out title);
            if (hWnd == IntPtr.Zero) return;
            ForceForeground(hWnd);
            Thread.Sleep(40);
            RECT r; GetWindowRect(hWnd, out r);
            result = Click(r.Left + (r.Right - r.Left) / 2, r.Top + (r.Bottom - r.Top) / 2, "left", 1, "", 0, true).Replace("Clicked", "Clicked window '" + title + "':");
        });
        return result;
    }

    private static string ClickMatch(Match m, string scopeDesc, int totalSame, string button, int count, string mods, bool probe) {
        El e = m.E;
        string r = Click(e.Cx, e.Cy, button, count, mods, 0, probe);
        if (r.StartsWith("ERROR")) return r;
        string amb = totalSame > 1 ? string.Format(" (+{0} other equally-ranked matches; use index=N or type= to disambiguate)", totalSame - 1) : "";
        return string.Format("{0} | matched [{1}] '{2}' score={3}{4}", r, e.Type, e.Name.Length > 60 ? e.Name.Substring(0, 57) + "..." : e.Name, m.Score, amb);
    }

    private static int PickIndex(List<Match> ms, int index, out int sameCount) {
        sameCount = 0;
        foreach (Match m in ms) if (m.Score == ms[0].Score) sameCount++;
        int i = index > 0 ? index - 1 : 0;
        return Math.Min(i, ms.Count - 1);
    }

    public static string ClickUIElement(string targetText, string windowTitle, string typeFilter, int index, int timeoutMs, string button, int count, string mods, bool probe) {
        string scope, err; List<El> pool;
        List<Match> ms = FindMatches(windowTitle, targetText, typeFilter, false, timeoutMs, false, true, out scope, out err, out pool);
        if (err != null) return err;
        if (ms.Count == 0) return NotFound(targetText, scope, pool, targetText);
        int same; int i = PickIndex(ms, index, out same);
        return ClickMatch(ms[i], scope, index > 0 ? 1 : same, button, count, mods, probe);
    }

    public static string FindUI(string text, string windowTitle, string typeFilter, int timeoutMs) {
        string scope, err; List<El> pool;
        List<Match> ms = FindMatches(windowTitle, text, typeFilter, false, timeoutMs, false, true, out scope, out err, out pool);
        if (err != null) return err;
        if (ms.Count == 0) return NotFound(text, scope, pool, text);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("matches in " + scope + ":");
        int n = 0;
        foreach (Match m in ms) {
            if (++n > 15) { sb.AppendLine("  ... (" + (ms.Count - 15) + " more)"); break; }
            sb.AppendLine(string.Format("  #{0} [{1}] \"{2}\" score={3} center={4},{5} rect={6},{7},{8},{9}{10}", n, m.E.Type, m.E.Name.Length > 70 ? m.E.Name.Substring(0, 67) + "..." : m.E.Name,
                m.Score, m.E.Cx, m.E.Cy, m.E.X, m.E.Y, m.E.W, m.E.H, m.E.Enabled ? "" : " (disabled)"));
        }
        return sb.ToString().TrimEnd();
    }

    public static string TypeIntoUIElement(string targetText, string textToType, string windowTitle, bool pressEnter, string typeFilter, int index, int timeoutMs) {
        string scope, err; List<El> pool;
        List<Match> ms = FindMatches(windowTitle, targetText, typeFilter, true, timeoutMs, false, true, out scope, out err, out pool);
        if (err != null) return err;
        if (ms.Count == 0) return NotFound(targetText, scope, pool, targetText);
        int same; int i = PickIndex(ms, index, out same);
        El e = ms[i].E;
        string r = TypeInto(e.Cx, e.Cy, textToType, pressEnter, true);
        if (r.StartsWith("ERROR")) return r;
        return string.Format("Typed {0} chars into [{1}] '{2}' at ({3}, {4}){5}", textToType.Length, e.Type, e.Name, e.Cx, e.Cy, pressEnter ? " and pressed enter" : "");
    }

    public static string TypeInto(int x, int y, string text, bool pressEnter, bool clearFirst) {
        string r = Click(x, y, "left", 1, "", 0, false);
        if (r.StartsWith("ERROR")) return r;
        Thread.Sleep(60);
        if (clearFirst) { SendKey("ctrl+a"); Thread.Sleep(25); }
        TypeText(text);
        if (pressEnter) { Thread.Sleep(60); SendKey("enter"); return string.Format("Clicked ({0}, {1}), typed {2} chars, and pressed enter", x, y, text.Length); }
        return string.Format("Clicked ({0}, {1}) and typed {2} chars", x, y, text.Length);
    }

    // ---------------------------------------------------------------- pattern-based actions (no mouse needed, works on background windows)
    private static string InvokeEl(El e) {
        if (e.Ref == null) return null;
        object pat; string how = null;
        Action act = null;
        if (e.Ref.TryGetCurrentPattern(InvokePattern.Pattern, out pat)) { InvokePattern ip = (InvokePattern)pat; how = "Invoke"; act = () => ip.Invoke(); }
        else if (e.Ref.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pat)) { SelectionItemPattern sp = (SelectionItemPattern)pat; how = "Select"; act = () => sp.Select(); }
        else if (e.Ref.TryGetCurrentPattern(TogglePattern.Pattern, out pat)) { TogglePattern tp = (TogglePattern)pat; how = "Toggle"; act = () => tp.Toggle(); }
        else if (e.Ref.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pat)) {
            ExpandCollapsePattern xp = (ExpandCollapsePattern)pat; how = "ExpandCollapse";
            act = () => { if (xp.Current.ExpandCollapseState == ExpandCollapseState.Expanded) xp.Collapse(); else xp.Expand(); };
        }
        if (act == null) return null;
        Exception ex = null;
        Thread t = new Thread(() => { try { act(); } catch (Exception x) { ex = x; } });
        t.IsBackground = true; t.Start();
        if (!t.Join(1500)) return how + " dispatched (still running - probably opened a modal dialog)";
        if (ex != null) return "ERROR: " + how + " failed: " + ex.Message;
        return how;
    }

    public static string InvokeUIElement(string targetText, string windowTitle, string typeFilter, int index, int timeoutMs) {
        string scope, err; List<El> pool;
        List<Match> ms = FindMatches(windowTitle, targetText, typeFilter, false, timeoutMs, true, true, out scope, out err, out pool);
        if (err != null) return err;
        if (ms.Count == 0) return NotFound(targetText, scope, pool, targetText);
        int same; int i = PickIndex(ms, index, out same);
        El e = ms[i].E;
        string how = InvokeEl(e);
        if (how == null) return string.Format("ERROR: [{0}] '{1}' supports no Invoke/Select/Toggle/ExpandCollapse pattern - use click-text instead", e.Type, e.Name);
        if (how.StartsWith("ERROR")) return how;
        return string.Format("{0} on [{1}] '{2}' (no mouse used)", how, e.Type, e.Name);
    }

    public static string SetUIValue(string targetText, string value, string windowTitle, string typeFilter, int index, int timeoutMs) {
        string scope, err; List<El> pool;
        List<Match> ms = FindMatches(windowTitle, targetText, typeFilter, true, timeoutMs, true, true, out scope, out err, out pool);
        if (err != null) return err;
        if (ms.Count == 0) return NotFound(targetText, scope, pool, targetText);
        int same; int i = PickIndex(ms, index, out same);
        El e = ms[i].E; object pat;
        if (e.Ref != null && e.Ref.TryGetCurrentPattern(ValuePattern.Pattern, out pat)) {
            ValuePattern vp = (ValuePattern)pat;
            if (vp.Current.IsReadOnly) return string.Format("ERROR: [{0}] '{1}' is read-only", e.Type, e.Name);
            vp.SetValue(value);
            return string.Format("Set value of [{0}] '{1}' ({2} chars, no typing)", e.Type, e.Name, value.Length);
        }
        return string.Format("ERROR: [{0}] '{1}' has no ValuePattern - use type-ui", e.Type, e.Name);
    }

    public static string ReadUIValue(string targetText, string windowTitle, string typeFilter, int index, int timeoutMs) {
        string scope, err; List<El> pool;
        List<Match> ms = FindMatches(windowTitle, targetText, typeFilter, true, timeoutMs, true, false, out scope, out err, out pool);
        if (err != null) return err;
        if (ms.Count == 0) return NotFound(targetText, scope, pool, targetText);
        int same; int i = PickIndex(ms, index, out same);
        El e = ms[i].E; object pat; string val = null;
        try {
            if (e.Ref != null && e.Ref.TryGetCurrentPattern(ValuePattern.Pattern, out pat)) val = ((ValuePattern)pat).Current.Value;
            else if (e.Ref != null && e.Ref.TryGetCurrentPattern(TextPattern.Pattern, out pat)) val = ((TextPattern)pat).DocumentRange.GetText(4000);
            else if (e.Ref != null && e.Ref.TryGetCurrentPattern(TogglePattern.Pattern, out pat)) val = ((TogglePattern)pat).Current.ToggleState.ToString();
            else if (e.Ref != null && e.Ref.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pat)) val = "selected=" + ((SelectionItemPattern)pat).Current.IsSelected;
        } catch (Exception ex) { return "ERROR: " + ex.Message; }
        return string.Format("[{0}] '{1}' value: {2}", e.Type, e.Name, val ?? e.Name);
    }

    public static string WaitFor(string text, string windowTitle, int timeoutMs, bool gone, string typeFilter) {
        Stopwatch sw = Stopwatch.StartNew();
        while (true) {
            string d, err;
            List<El> pool = CollectScope(windowTitle, false, false, out d, out err);
            if (pool != null) {
                var ms = RankMatches(pool, text, typeFilter, false);
                if (!gone && ms.Count > 0) return string.Format("Found '{0}' [{1}] center={2},{3} after {4}ms", ms[0].E.Name, ms[0].E.Type, ms[0].E.Cx, ms[0].E.Cy, sw.ElapsedMilliseconds);
                if (gone && ms.Count == 0) return string.Format("Gone: '{0}' after {1}ms", text, sw.ElapsedMilliseconds);
            } else if (gone) {
                return "Gone: window closed after " + sw.ElapsedMilliseconds + "ms";
            }
            if (sw.ElapsedMilliseconds >= timeoutMs) return "ERROR: timeout (" + timeoutMs + "ms) waiting for '" + text + "' to " + (gone ? "disappear" : "appear") + " in " + (string.IsNullOrEmpty(windowTitle) ? "foreground window" : "'" + windowTitle + "'");
            Thread.Sleep(80);
        }
    }

    public static string WaitWindow(string title, int timeoutMs) {
        Stopwatch sw = Stopwatch.StartNew();
        while (true) {
            string m; IntPtr h = FindWindowByTitle(title, out m);
            if (h != IntPtr.Zero) return string.Format("Window '{0}' present after {1}ms", m, sw.ElapsedMilliseconds);
            if (sw.ElapsedMilliseconds >= timeoutMs) return "ERROR: timeout (" + timeoutMs + "ms) waiting for window '" + title + "'";
            Thread.Sleep(80);
        }
    }

    // ---------------------------------------------------------------- listing (ui action)
    public static List<string> ListUIElements(string windowTitle, string filter) {
        var results = new List<string>();
        string desc, err;
        List<El> pool = CollectScope(windowTitle, false, true, out desc, out err);
        if (pool == null) { results.Add(err); return results; }
        pool.Sort(ReadingOrder);
        foreach (El e in pool) {
            if (!e.Interactive && e.Name.Length == 0) continue;
            if (e.Type == "Pane" || e.Type == "Window" || e.Type == "Group" || e.Type == "ScrollBar" || e.Type == "Thumb") continue;
            if (!string.IsNullOrWhiteSpace(filter)) {
                if (e.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && e.Type.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && e.AutoId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            } else if (e.Name.Length == 0 && e.Type != "Edit") continue;
            string name = e.Name.Length > 80 ? e.Name.Substring(0, 77) + "..." : e.Name;
            results.Add(string.Format("[{0}] \"{1}\" | center={2},{3} | rect={4},{5},{6},{7}{8}{9}", e.Type, name, e.Cx, e.Cy, e.X, e.Y, e.W, e.H,
                e.Enabled ? "" : " | disabled", e.AutoId.Length > 0 && e.AutoId.Length < 40 ? " | id=" + e.AutoId : ""));
        }
        return results;
    }

    private static int ReadingOrder(El a, El b) {
        int ra = a.Y / 14, rb = b.Y / 14;
        if (ra != rb) return ra.CompareTo(rb);
        return a.X.CompareTo(b.X);
    }

    // ---------------------------------------------------------------- set-of-marks snapshot: numbered boxes on a screenshot + stable id list
    private static string SnapCachePath() {
        string dir = Path.Combine(Path.GetTempPath(), "agy-computer-use");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, "snap.tsv");
    }

    public static string Snap(string windowTitle, int max, bool all, bool withOcr, string outPath, double scale, string fmt, int quality) {
        string desc, err;
        List<El> pool = CollectScope(windowTitle, false, true, out desc, out err);
        if (pool == null) return err;
        Thread.Sleep(30);
        var picked = new List<El>();
        foreach (El e in pool) {
            bool take = e.Interactive;
            if (!take && all && e.Name.Length > 0 && e.Type != "Window" && e.Type != "ScrollBar" && e.Type != "Thumb" && e.Type != "Pane" && e.Type != "Group") take = true;
            if (take && (e.Interactive || e.W * e.H < 600000)) picked.Add(e);
        }
        picked.Sort(ReadingOrder);
        int truncated = 0;
        if (picked.Count > max) { truncated = picked.Count - max; picked = picked.GetRange(0, max); }

        if (withOcr) {
            Rectangle area = Rectangle.Empty;
            if (!string.IsNullOrWhiteSpace(windowTitle)) { string mt; IntPtr h = FindWindowByTitle(windowTitle, out mt); if (h != IntPtr.Zero) { RECT wr; GetWindowRect(h, out wr); area = new Rectangle(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top); } }
            else { IntPtr fg = GetForegroundWindow(); if (fg != IntPtr.Zero) { RECT wr; GetWindowRect(fg, out wr); area = new Rectangle(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top); } }
            area = ClipToScreen(area);
            if (area.Width > 0) {
                string oerr; List<OcrLineInfo> lines = OcrRegion(area, 0, out oerr);
                if (lines != null) {
                    foreach (OcrLineInfo ln in lines) {
                        bool covered = false;
                        foreach (El e in picked) if (ln.Cx >= e.X && ln.Cx <= e.X + e.W && ln.Cy >= e.Y && ln.Cy <= e.Y + e.H) { covered = true; break; }
                        if (covered || ln.Text.Trim().Length < 2) continue;
                        El o = new El(); o.Type = "OCR"; o.Name = ln.Text; o.X = ln.X; o.Y = ln.Y; o.W = ln.W; o.H = ln.H; o.Cx = ln.Cx; o.Cy = ln.Cy; o.Interactive = true;
                        picked.Add(o);
                    }
                    picked.Sort(ReadingOrder);
                }
            }
        }

        int id = 1;
        foreach (El e in picked) e.Id = id++;
        string shot = Screenshot(outPath, false, Rectangle.Empty, scale, fmt, quality, true, picked);

        StringBuilder sb = new StringBuilder(), cache = new StringBuilder();
        sb.AppendLine(shot);
        sb.AppendLine(string.Format("SNAP {0}: {1} elements{2}. Use click-id N / type-id N TEXT / hover-id N (ids are valid until the UI changes - re-snap after navigation).", desc, picked.Count, truncated > 0 ? " (+" + truncated + " not shown; narrow with a window title or use ui filter=)" : ""));
        foreach (El e in picked) {
            string nm = e.Name.Length > 70 ? e.Name.Substring(0, 67) + "..." : e.Name;
            sb.AppendLine(string.Format("#{0} [{1}] \"{2}\" @{3},{4} ({5}x{6}){7}", e.Id, e.Type, nm, e.Cx, e.Cy, e.W, e.H, e.Enabled ? "" : " disabled"));
            cache.AppendLine(string.Join("\t", new string[] { e.Id.ToString(), e.Type, e.Cx.ToString(), e.Cy.ToString(), e.X.ToString(), e.Y.ToString(), e.W.ToString(), e.H.ToString(), e.Name.Replace("\t", " ") }));
        }
        File.WriteAllText(SnapCachePath(), cache.ToString(), Encoding.UTF8);
        return sb.ToString().TrimEnd();
    }

    private static El LoadSnapEl(int id, out string err) {
        err = null;
        string p = SnapCachePath();
        if (!File.Exists(p)) { err = "ERROR: no snapshot yet - run 'snap' first"; return null; }
        foreach (string line in File.ReadAllLines(p, Encoding.UTF8)) {
            string[] f = line.Split('\t');
            if (f.Length < 9) continue;
            if (f[0] != id.ToString()) continue;
            El e = new El(); e.Id = id; e.Type = f[1]; e.Cx = int.Parse(f[2]); e.Cy = int.Parse(f[3]); e.X = int.Parse(f[4]); e.Y = int.Parse(f[5]); e.W = int.Parse(f[6]); e.H = int.Parse(f[7]); e.Name = f[8];
            return e;
        }
        err = "ERROR: snapshot has no element #" + id;
        return null;
    }

    public static string ClickId(int id, string button, int count, string mods, bool probe, string typeText, bool enter, bool hoverOnly) {
        string err; El e = LoadSnapEl(id, out err);
        if (e == null) return err;
        string r;
        if (hoverOnly) r = Click(e.Cx, e.Cy, "none", 0, "", 0, probe);
        else if (typeText != null) r = TypeInto(e.Cx, e.Cy, typeText, enter, true);
        else r = Click(e.Cx, e.Cy, button, count, mods, 0, probe);
        return r + string.Format(" | snap #{0} [{1}] '{2}'", id, e.Type, e.Name.Length > 50 ? e.Name.Substring(0, 47) + "..." : e.Name);
    }
}
