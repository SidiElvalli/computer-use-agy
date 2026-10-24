// ComputerBridge v2 - screen capture: multi-monitor, region/window crop, scaling, JPEG/PNG, labelled coordinate grid, numbered element marks, pixel probe, monitors.
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;

public static partial class ComputerBridge {
    private static Rectangle ClipToScreen(Rectangle r) {
        Rectangle v = new Rectangle(VX, VY, VW, VH);
        r.Intersect(v);
        return r;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, uint rop);

    private static Bitmap CaptureRect(Rectangle r) {
        Bitmap bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(bmp)) {
            IntPtr hdcDst = g.GetHdc();
            IntPtr hdcSrc = GetDC(IntPtr.Zero);
            // SRCCOPY | CAPTUREBLT: also grabs layered/transparent windows (overlays, some Electron/UWP surfaces)
            bool ok = BitBlt(hdcDst, 0, 0, r.Width, r.Height, hdcSrc, r.X, r.Y, 0x00CC0020u | 0x40000000u);
            ReleaseDC(IntPtr.Zero, hdcSrc);
            g.ReleaseHdc(hdcDst);
            if (!ok) g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size);
        }
        return bmp;
    }

    private static Color TypeColor(string t) {
        switch (t) {
            case "Button": case "SplitButton": return Color.FromArgb(220, 30, 90, 255);
            case "Edit": case "ComboBox": case "Document": return Color.FromArgb(220, 0, 150, 60);
            case "Hyperlink": return Color.FromArgb(220, 150, 40, 200);
            case "TabItem": case "MenuItem": case "ListItem": case "TreeItem": case "DataItem": return Color.FromArgb(220, 230, 120, 0);
            case "CheckBox": case "RadioButton": case "Slider": case "Spinner": return Color.FromArgb(220, 0, 150, 170);
            case "OCR": return Color.FromArgb(220, 200, 0, 120);
            default: return Color.FromArgb(220, 120, 120, 120);
        }
    }

    // Unified capture + annotate. All overlay labels use real SCREEN coordinates so what you read is what you click.
    private static string Screenshot(string outPath, bool grid, Rectangle region, double scale, string fmt, int quality, bool showCursor, List<El> marks) {
        string result = "";
        RunOnDesktop(() => {
            Rectangle full = new Rectangle(VX, VY, VW, VH);
            Rectangle rg = (region.Width > 0 && region.Height > 0) ? ClipToScreen(region) : full;
            if (rg.Width <= 0 || rg.Height <= 0) { result = "ERROR: screenshot region is outside the screen " + full; return; }
            if (scale <= 0) scale = 1.0;
            if (scale > 8) scale = 8;
            if (rg.Width * scale > 6000) scale = 6000.0 / rg.Width;

            using (Bitmap cap = CaptureRect(rg)) {
                int ow = Math.Max(1, (int)Math.Round(rg.Width * scale));
                int oh = Math.Max(1, (int)Math.Round(rg.Height * scale));
                Bitmap outBmp = cap;
                bool scaled = Math.Abs(scale - 1.0) > 0.001;
                if (scaled) {
                    outBmp = new Bitmap(ow, oh, PixelFormat.Format24bppRgb);
                    using (Graphics sg = Graphics.FromImage(outBmp)) {
                        sg.InterpolationMode = scale > 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                        sg.PixelOffsetMode = PixelOffsetMode.Half;
                        sg.DrawImage(cap, new Rectangle(0, 0, ow, oh), new Rectangle(0, 0, rg.Width, rg.Height), GraphicsUnit.Pixel);
                    }
                }
                try {
                    using (Graphics g = Graphics.FromImage(outBmp)) {
                        g.SmoothingMode = SmoothingMode.None;
                        float S = (float)scale;

                        if (grid) {
                            int step = rg.Width <= 220 ? 10 : (rg.Width <= 520 ? 25 : 50);
                            int labelStep = step * 2;
                            using (Pen majorPen = new Pen(Color.FromArgb(150, 255, 0, 0), 1))
                            using (Pen minorPen = new Pen(Color.FromArgb(80, 255, 80, 80), 1))
                            using (Font font = new Font("Consolas", 8.5f, FontStyle.Bold))
                            using (Brush brush = new SolidBrush(Color.FromArgb(245, 200, 0, 0)))
                            using (Brush bg = new SolidBrush(Color.FromArgb(190, 255, 255, 255))) {
                                minorPen.DashStyle = DashStyle.Dot;
                                int gx0 = (int)Math.Ceiling((double)rg.Left / step) * step;
                                int gy0 = (int)Math.Ceiling((double)rg.Top / step) * step;
                                for (int gx = gx0; gx < rg.Right; gx += step) {
                                    float px = (gx - rg.Left) * S;
                                    g.DrawLine((gx % labelStep == 0) ? majorPen : minorPen, px, 0, px, oh);
                                }
                                for (int gy = gy0; gy < rg.Bottom; gy += step) {
                                    float py = (gy - rg.Top) * S;
                                    g.DrawLine((gy % labelStep == 0) ? majorPen : minorPen, 0, py, ow, py);
                                }
                                for (int gx = gx0; gx < rg.Right; gx += step) {
                                    if (gx % labelStep != 0) continue;
                                    for (int gy = gy0; gy < rg.Bottom; gy += step) {
                                        if (gy % labelStep != 0) continue;
                                        string label = gx + "," + gy;
                                        float px = (gx - rg.Left) * S, py = (gy - rg.Top) * S;
                                        SizeF sz = g.MeasureString(label, font);
                                        g.FillRectangle(bg, px + 1, py + 1, sz.Width, sz.Height);
                                        g.DrawString(label, font, brush, px + 1, py + 1);
                                    }
                                }
                            }
                        }

                        if (marks != null) {
                            using (Font mf = new Font("Segoe UI", 8.5f, FontStyle.Bold)) {
                                foreach (El e in marks) {
                                    float px = (e.X - rg.Left) * S, py = (e.Y - rg.Top) * S, pw = e.W * S, ph = e.H * S;
                                    if (px + pw < 0 || py + ph < 0 || px > ow || py > oh) continue;
                                    Color c = TypeColor(e.Type);
                                    using (Pen p = new Pen(c, 2)) g.DrawRectangle(p, px, py, pw, ph);
                                    string lab = e.Id.ToString();
                                    SizeF sz = g.MeasureString(lab, mf);
                                    float lx = Math.Max(0, Math.Min(ow - sz.Width, px)), ly = Math.Max(0, Math.Min(oh - sz.Height, py - sz.Height + 2));
                                    using (Brush b = new SolidBrush(c)) g.FillRectangle(b, lx, ly, sz.Width, sz.Height);
                                    g.DrawString(lab, mf, Brushes.White, lx, ly);
                                }
                            }
                        }

                        POINT cur;
                        if (showCursor && GetCursorPos(out cur) && rg.Contains(cur.X, cur.Y)) {
                            float cx = (cur.X - rg.Left) * S, cy = (cur.Y - rg.Top) * S;
                            using (Pen curPen = new Pen(Color.FromArgb(230, 0, 200, 255), 2)) {
                                g.DrawEllipse(curPen, cx - 8, cy - 8, 16, 16);
                                g.DrawLine(curPen, cx - 13, cy, cx + 13, cy);
                                g.DrawLine(curPen, cx, cy - 13, cx, cy + 13);
                            }
                        }
                    }

                    string dir = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    bool jpg = fmt == "jpg" || fmt == "jpeg";
                    if (jpg) {
                        ImageCodecInfo codec = null;
                        foreach (ImageCodecInfo ci in ImageCodecInfo.GetImageEncoders()) if (ci.MimeType == "image/jpeg") codec = ci;
                        EncoderParameters ep = new EncoderParameters(1);
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Max(10, Math.Min(100, quality)));
                        outBmp.Save(outPath, codec, ep);
                    } else {
                        outBmp.Save(outPath, ImageFormat.Png);
                    }
                    result = string.Format("SCREENSHOT: {0} ({1}x{2}) region={3},{4},{5},{6} scale={7}  [image pixel (i,j) = screen ({3}+i/{7}, {4}+j/{7})]",
                        outPath, ow, oh, rg.X, rg.Y, rg.Width, rg.Height, scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                } finally {
                    if (scaled) outBmp.Dispose();
                }
            }
        });
        return result;
    }

    private static Rectangle ParseRegion(string s) {
        if (string.IsNullOrEmpty(s)) return Rectangle.Empty;
        string[] p = s.Split(new char[] { ',', ' ', 'x' }, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 4) throw new ArgumentException("region must be x,y,w,h");
        return new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
    }

    public static string PixelColor(int x, int y) {
        string res = "";
        RunOnDesktop(() => {
            if (!InScreen(x, y)) { res = ScreenBoundsError(x, y); return; }
            using (Bitmap b = CaptureRect(new Rectangle(x, y, 1, 1))) {
                Color c = b.GetPixel(0, 0);
                res = string.Format("pixel({0},{1}) = #{2:X2}{3:X2}{4:X2} rgb({2},{3},{4})", x, y, c.R, c.G, c.B);
            }
        });
        return res;
    }

    public static string DescribeMonitors() {
        StringBuilder sb = new StringBuilder();
        RunOnDesktop(() => {
            sb.AppendFormat("virtual desktop: x={0} y={1} w={2} h={3}\n", VX, VY, VW, VH);
            int i = 0;
            foreach (Screen s in Screen.AllScreens) {
                uint dx = 96, dy = 96;
                try { GetDpiForMonitor(MonitorFromPoint(new POINT { X = s.Bounds.X + 1, Y = s.Bounds.Y + 1 }, 2), 0, out dx, out dy); } catch { }
                sb.AppendFormat("monitor {0}: {1} bounds={2},{3},{4},{5} dpi={6} ({7}%){8}\n", i++, s.DeviceName.Replace("\\\\.\\", ""),
                    s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height, dx, (int)Math.Round(dx * 100.0 / 96), s.Primary ? " PRIMARY" : "");
            }
        });
        return sb.ToString().TrimEnd();
    }
}
