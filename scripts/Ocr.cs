// ComputerBridge v2 - OCR fallback via the built-in Windows 10/11 OCR engine (Windows.Media.Ocr), loaded by reflection so no extra build references are needed.
// Use for apps that expose no UI Automation tree (Blender, games, canvas/Electron surfaces, remote desktops, images).
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;

public static partial class ComputerBridge {
    private class OcrWordInfo { public string Text; public int X, Y, W, H; }
    private class OcrLineInfo {
        public string Text; public int X, Y, W, H, Cx, Cy;
        public List<OcrWordInfo> Words = new List<OcrWordInfo>();
    }

    private static Type WT(string typeName, string asm) { return Type.GetType(typeName + ", " + asm + ", ContentType=WindowsRuntime"); }

    private static object WinRtAwait(object op, Type resultType) {
        Assembly a = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
        Type ext = a.GetType("System.WindowsRuntimeSystemExtensions");
        MethodInfo mi = null;
        foreach (MethodInfo m in ext.GetMethods()) {
            if (m.Name != "AsTask") continue;
            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length != 1) continue;
            if (m.IsGenericMethod && m.GetGenericArguments().Length == 1 && ps[0].ParameterType.Name.StartsWith("IAsyncOperation`1", StringComparison.Ordinal) && !ps[0].ParameterType.Name.Contains("Progress"))
                mi = m.MakeGenericMethod(resultType);
        }
        Task task = (Task)mi.Invoke(null, new object[] { op });
        task.Wait(20000);
        return task.GetType().GetProperty("Result").GetValue(task);
    }

    private static double RectVal(object rect, string name) {
        Type t = rect.GetType();
        PropertyInfo p = t.GetProperty(name);
        if (p != null) return Convert.ToDouble(p.GetValue(rect, null));
        FieldInfo f = t.GetField(name);
        return f != null ? Convert.ToDouble(f.GetValue(rect)) : 0;
    }

    // Returns lines in SCREEN coordinates. zoom<=0 => automatic (small areas are upscaled 2x for accuracy on tiny UI text).
    private static List<OcrLineInfo> OcrRegion(Rectangle area, double zoom, out string err) {
        err = null;
        byte[] png = null; double z = zoom;
        RunOnDesktop(() => {
            if (z <= 0) z = (area.Width * area.Height < 600 * 400) ? 3.0 : ((area.Width * area.Height < 1000 * 700) ? 2.0 : 1.0);
            if (area.Width * z > 2500) z = 2500.0 / area.Width;
            if (area.Height * z > 2500) z = Math.Min(z, 2500.0 / area.Height);
            using (Bitmap cap = CaptureRect(area)) {
                int ow = Math.Max(1, (int)Math.Round(area.Width * z)), oh = Math.Max(1, (int)Math.Round(area.Height * z));
                using (Bitmap sc = new Bitmap(ow, oh, PixelFormat.Format24bppRgb)) {
                    using (Graphics g = Graphics.FromImage(sc)) {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(cap, new Rectangle(0, 0, ow, oh), new Rectangle(0, 0, area.Width, area.Height), GraphicsUnit.Pixel);
                    }
                    using (MemoryStream ms = new MemoryStream()) { sc.Save(ms, ImageFormat.Png); png = ms.ToArray(); }
                }
            }
        });
        try {
            Type ocrT = WT("Windows.Media.Ocr.OcrEngine", "Windows.Media.Ocr");
            if (ocrT == null) { err = "ERROR: Windows OCR (Windows.Media.Ocr) is not available on this system"; return null; }
            object eng = ocrT.GetMethod("TryCreateFromUserProfileLanguages").Invoke(null, null);
            if (eng == null) { err = "ERROR: no OCR language pack installed (Settings > Time & language > Language)"; return null; }
            Type imsT = WT("Windows.Storage.Streams.InMemoryRandomAccessStream", "Windows.Storage.Streams");
            object stream = Activator.CreateInstance(imsT);
            Type dwT = WT("Windows.Storage.Streams.DataWriter", "Windows.Storage.Streams");
            object dw = Activator.CreateInstance(dwT, new object[] { stream });
            dwT.GetMethod("WriteBytes").Invoke(dw, new object[] { png });
            WinRtAwait(dwT.GetMethod("StoreAsync").Invoke(dw, null), typeof(uint));
            imsT.GetMethod("Seek").Invoke(stream, new object[] { 0UL });
            Type decT = WT("Windows.Graphics.Imaging.BitmapDecoder", "Windows.Graphics.Imaging");
            object decOp = decT.GetMethod("CreateAsync", new Type[] { WT("Windows.Storage.Streams.IRandomAccessStream", "Windows.Storage.Streams") }).Invoke(null, new object[] { stream });
            object dec = WinRtAwait(decOp, decT);
            Type pfT = WT("Windows.Graphics.Imaging.BitmapPixelFormat", "Windows.Graphics.Imaging");
            Type amT = WT("Windows.Graphics.Imaging.BitmapAlphaMode", "Windows.Graphics.Imaging");
            Type sbT = WT("Windows.Graphics.Imaging.SoftwareBitmap", "Windows.Graphics.Imaging");
            object sbOp = decT.GetMethod("GetSoftwareBitmapAsync", new Type[] { pfT, amT }).Invoke(dec, new object[] { Enum.Parse(pfT, "Bgra8"), Enum.Parse(amT, "Premultiplied") });
            object sbmp = WinRtAwait(sbOp, sbT);
            Type resT = WT("Windows.Media.Ocr.OcrResult", "Windows.Media.Ocr");
            object res = WinRtAwait(ocrT.GetMethod("RecognizeAsync").Invoke(eng, new object[] { sbmp }), resT);

            var lines = new List<OcrLineInfo>();
            System.Collections.IEnumerable ls = (System.Collections.IEnumerable)resT.GetProperty("Lines").GetValue(res, null);
            foreach (object ln in ls) {
                Type lt = ln.GetType();
                OcrLineInfo li = new OcrLineInfo();
                li.Text = (string)lt.GetProperty("Text").GetValue(ln, null);
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                System.Collections.IEnumerable ws = (System.Collections.IEnumerable)lt.GetProperty("Words").GetValue(ln, null);
                foreach (object w in ws) {
                    Type wt = w.GetType();
                    object rect = wt.GetProperty("BoundingRect").GetValue(w, null);
                    OcrWordInfo wi = new OcrWordInfo();
                    wi.Text = (string)wt.GetProperty("Text").GetValue(w, null);
                    wi.X = area.X + (int)Math.Round(RectVal(rect, "X") / z); wi.Y = area.Y + (int)Math.Round(RectVal(rect, "Y") / z);
                    wi.W = Math.Max(1, (int)Math.Round(RectVal(rect, "Width") / z)); wi.H = Math.Max(1, (int)Math.Round(RectVal(rect, "Height") / z));
                    li.Words.Add(wi);
                    minX = Math.Min(minX, wi.X); minY = Math.Min(minY, wi.Y); maxX = Math.Max(maxX, wi.X + wi.W); maxY = Math.Max(maxY, wi.Y + wi.H);
                }
                if (li.Words.Count == 0) continue;
                li.X = minX; li.Y = minY; li.W = maxX - minX; li.H = maxY - minY; li.Cx = minX + li.W / 2; li.Cy = minY + li.H / 2;
                // Rebuild the line text from the words: Windows OCR puts no spaces between CJK words, and this keeps char offsets aligned with Words
                StringBuilder sb = new StringBuilder();
                foreach (OcrWordInfo wi in li.Words) { if (sb.Length > 0) sb.Append(' '); sb.Append(wi.Text); }
                li.Text = sb.ToString();
                lines.Add(li);
            }
            return lines;
        } catch (Exception ex) {
            Exception inner = ex; while (inner.InnerException != null) inner = inner.InnerException;
            err = "ERROR: OCR failed: " + inner.Message;
            return null;
        }
    }

    private static Rectangle OcrArea(string windowTitle, Rectangle region, out string err) {
        err = null;
        if (region.Width > 0 && region.Height > 0) return ClipToScreen(region);
        IntPtr h = IntPtr.Zero;
        if (!string.IsNullOrWhiteSpace(windowTitle)) {
            string m; h = FindWindowByTitle(windowTitle, out m);
            if (h == IntPtr.Zero) { err = "ERROR: window not found: '" + windowTitle + "'"; return Rectangle.Empty; }
            ForceForeground(h);
            System.Threading.Thread.Sleep(60);
        } else h = GetForegroundWindow();
        if (h == IntPtr.Zero) return new Rectangle(VX, VY, VW, VH);
        RECT r; GetWindowRect(h, out r);
        return ClipToScreen(new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
    }

    public static string OcrDump(string windowTitle, Rectangle region, string find, bool words, double zoom) {
        string err; Rectangle area = OcrArea(windowTitle, region, out err);
        if (err != null) return err;
        if (area.Width <= 0) return "ERROR: OCR area is outside the screen";
        List<OcrLineInfo> lines = OcrRegion(area, zoom, out err);
        if (lines == null) return err;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(string.Format("OCR area={0},{1},{2},{3} lines={4} (coordinates are screen pixels)", area.X, area.Y, area.Width, area.Height, lines.Count));
        int n = 0;
        foreach (OcrLineInfo l in lines) {
            if (!string.IsNullOrEmpty(find) && l.Text.IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0) continue;
            n++;
            sb.AppendLine(string.Format("\"{0}\" | center={1},{2} | rect={3},{4},{5},{6}", l.Text, l.Cx, l.Cy, l.X, l.Y, l.W, l.H));
            if (words) foreach (OcrWordInfo w in l.Words) sb.AppendLine(string.Format("    '{0}' center={1},{2}", w.Text, w.X + w.W / 2, w.Y + w.H / 2));
        }
        if (n == 0 && !string.IsNullOrEmpty(find)) sb.AppendLine("(no line contains '" + find + "')");
        return sb.ToString().TrimEnd();
    }

    private class OcrHit { public int Cx, Cy, X, Y, W, H, Score; public string Text; }

    public static string ClickOcr(string text, string windowTitle, Rectangle region, int index, string button, int count, string mods, bool probe, double zoom) {
        string err; Rectangle area = OcrArea(windowTitle, region, out err);
        if (err != null) return err;
        if (area.Width <= 0) return "ERROR: OCR area is outside the screen";
        List<OcrLineInfo> lines = OcrRegion(area, zoom, out err);
        if (lines == null) return err;
        string tn = Norm(text);
        var hits = new List<OcrHit>();
        foreach (OcrLineInfo l in lines) {
            string ln = l.Text;
            int ci = ln.IndexOf(text, StringComparison.OrdinalIgnoreCase);
            if (ci < 0) {
                // tolerate OCR whitespace differences
                string a = Norm(ln); if (a.IndexOf(tn, StringComparison.Ordinal) < 0) continue;
                ci = 0;
                OcrHit h2 = new OcrHit(); h2.Cx = l.Cx; h2.Cy = l.Cy; h2.X = l.X; h2.Y = l.Y; h2.W = l.W; h2.H = l.H; h2.Text = l.Text; h2.Score = 40; hits.Add(h2); continue;
            }
            int end = ci + text.Length, pos = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (OcrWordInfo w in l.Words) {
                int ws = pos, we = pos + w.Text.Length; pos = we + 1;
                if (we <= ci || ws >= end) continue;
                minX = Math.Min(minX, w.X); minY = Math.Min(minY, w.Y); maxX = Math.Max(maxX, w.X + w.W); maxY = Math.Max(maxY, w.Y + w.H);
            }
            if (minX == int.MaxValue) continue;
            OcrHit h = new OcrHit(); h.X = minX; h.Y = minY; h.W = maxX - minX; h.H = maxY - minY; h.Cx = minX + h.W / 2; h.Cy = minY + h.H / 2; h.Text = l.Text;
            h.Score = string.Equals(Norm(ln), tn, StringComparison.Ordinal) ? 100 : (ln.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 80 : 60);
            hits.Add(h);
        }
        if (hits.Count == 0) {
            var near = new List<string>();
            foreach (OcrLineInfo l in lines) { if (near.Count < 8 && l.Text.Length > 1) near.Add("'" + (l.Text.Length > 40 ? l.Text.Substring(0, 37) + "..." : l.Text) + "'"); }
            return "ERROR: text not found by OCR: '" + text + "' (read " + lines.Count + " lines). Visible: " + string.Join(", ", near.ToArray());
        }
        hits.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : (a.Cy != b.Cy ? a.Cy.CompareTo(b.Cy) : a.Cx.CompareTo(b.Cx)));
        int i = index > 0 ? Math.Min(index - 1, hits.Count - 1) : 0;
        OcrHit hit = hits[i];
        string r = Click(hit.Cx, hit.Cy, button, count, mods, 0, probe);
        if (r.StartsWith("ERROR")) return r;
        return string.Format("{0} | OCR matched \"{1}\" (match {2} of {3})", r, hit.Text.Length > 60 ? hit.Text.Substring(0, 57) + "..." : hit.Text, i + 1, hits.Count);
    }
}
