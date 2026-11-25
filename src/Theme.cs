using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MetroUninstaller
{
    public enum GlyphKind
    {
        Uninstall, Driver, History, Status, Settings, Restore, Whitelist,
        Scan, Report, Trash, Refresh, Back, Shield
    }

    /// <summary>Windows 8 Modern UI 基础视觉：纯色、无渐变、无阴影、无圆角。</summary>
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(230, 230, 230);
        public static readonly Color BgCard = Color.FromArgb(242, 242, 242);
        public static readonly Color BgWhite = Color.FromArgb(250, 250, 250);
        public static readonly Color Ink = Color.FromArgb(26, 26, 26);
        public static readonly Color InkSoft = Color.FromArgb(112, 112, 112);
        public static readonly Color InkFaint = Color.FromArgb(150, 150, 150);
        public static readonly Color Accent = Color.FromArgb(27, 161, 226);
        public static readonly Color AccentDeep = Color.FromArgb(0, 120, 215);
        public static readonly Color Red = Color.FromArgb(193, 58, 58);
        public static readonly Color Orange = Color.FromArgb(216, 98, 42);
        public static readonly Color Green = Color.FromArgb(96, 169, 23);
        public static readonly Color Teal = Color.FromArgb(0, 153, 133);
        public static readonly Color Gray = Color.FromArgb(118, 118, 118);
        public static readonly Color Slate = Color.FromArgb(52, 60, 68);
        public static readonly Color Line = Color.FromArgb(206, 206, 206);
        public static readonly Color LineSoft = Color.FromArgb(222, 222, 222);

        /// <summary>全局 DPI 缩放系数（96 DPI 时为 1）。所有像素尺寸统一经过 S()。</summary>
        public static float DpiFactor = 1f;

        public static int S(int v) { return (int)Math.Round(v * DpiFactor); }

        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();

        public static Font GetFont(string family, float size, FontStyle style)
        {
            string key = family + "|" + size.ToString("F1") + "|" + style.ToString();
            Font f = FontCache.ContainsKey(key) ? FontCache[key] : null;
            if (f == null)
            {
                try { f = new Font(family, size, style, GraphicsUnit.Point); }
                catch { f = new Font("Segoe UI", size, style, GraphicsUnit.Point); }
                FontCache[key] = f;
            }
            return f;
        }

        /// <summary>大号细字重标题（中文用雅黑 Light，避免 Segoe UI Light 缺字）</summary>
        public static Font Display(float size) { return GetFont("Microsoft YaHei UI Light", size, FontStyle.Regular); }
        /// <summary>中文正文（微软雅黑 UI）</summary>
        public static Font Text(float size) { return GetFont("Microsoft YaHei UI", size, FontStyle.Regular); }
        public static Font TextBold(float size) { return GetFont("Microsoft YaHei UI", size, FontStyle.Bold); }
        public static Font Light(float size) { return GetFont("Segoe UI Semilight", size, FontStyle.Regular); }
        public static Font Num(float size) { return GetFont("Segoe UI Semilight", size, FontStyle.Regular); }

        public static readonly Font TileTitleFont = GetFont("Microsoft YaHei UI", 15f, FontStyle.Regular);
        public static readonly Font TileSubFont = GetFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
        public static readonly Font TileValueFont = GetFont("Segoe UI Light", 38f, FontStyle.Regular);

        public static Color Lighten(Color c, int delta)
        {
            int r = Math.Min(255, c.R + delta), g = Math.Min(255, c.G + delta), b = Math.Min(255, c.B + delta);
            return Color.FromArgb(c.A, r, g, b);
        }

        /// <summary>按名称散列出稳定的磁贴色，用于列表图标块。</summary>
        public static Color ColorFor(string s)
        {
            if (string.IsNullOrEmpty(s)) return Gray;
            Color[] palette = new Color[] {
                Accent, Teal, Orange, Green, Slate,
                Color.FromArgb(162, 0, 255), Color.FromArgb(0, 120, 215),
                Color.FromArgb(224, 86, 143), Color.FromArgb(94, 114, 52),
                Color.FromArgb(113, 80, 137)
            };
            int h = 0;
            foreach (char c in s) h = (h * 31 + (int)c) & 0x7FFFFFF;
            return palette[h % palette.Length];
        }

        public static Color Darken(Color c, int delta) { return Lighten(c, -delta); }

        public static void Fill(Graphics g, Rectangle r, Color c)
        {
            using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, r);
        }

        public static void Text(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags);
        }

        public static void LineH(Graphics g, int x1, int x2, int y, Color c)
        {
            using (Pen p = new Pen(c, 1)) g.DrawLine(p, x1, y, x2, y);
        }

        /// <summary>Metro 几何图标：全部用直线、圆、多边形，保持扁平。</summary>
        public static void Glyph(Graphics g, GlyphKind kind, Rectangle r, Color c)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int s = Math.Min(r.Width, r.Height);
            float w = Math.Max(2f, s / 13f);
            using (Pen pen = new Pen(c, w))
            using (SolidBrush br = new SolidBrush(c))
            {
                int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                int x = cx - s / 2, y = cy - s / 2;
                switch (kind)
                {
                    case GlyphKind.Uninstall:
                        {
                            // 方框 + 向下箭头（软件被移出容器）
                            int bw = (int)(s * 0.66f), bh = (int)(s * 0.52f);
                            g.DrawRectangle(pen, x + (s - bw) / 2, y + 2, bw, bh);
                            Point[] a = new Point[] {
                                new Point(cx, y + s - 2),
                                new Point(cx - (int)(s * 0.16f), y + (int)(s * 0.62f)),
                                new Point(cx + (int)(s * 0.16f), y + (int)(s * 0.62f))
                            };
                            g.FillPolygon(br, a);
                            g.DrawLine(pen, cx, y + (int)(s * 0.28f), cx, y + (int)(s * 0.64f));
                            break;
                        }
                    case GlyphKind.Driver:
                        {
                            int bw = (int)(s * 0.54f);
                            int ix = cx - bw / 2, iy = cy - bw / 2;
                            g.DrawRectangle(pen, ix, iy, bw, bw);
                            g.DrawRectangle(pen, ix + bw / 5, iy + bw / 5, bw - 2 * (bw / 5), bw - 2 * (bw / 5));
                            int n = 3, len = (int)(s * 0.13f);
                            for (int i = 0; i < n; i++)
                            {
                                int off = (i + 1) * bw / (n + 1);
                                g.DrawLine(pen, ix + off, iy - len, ix + off, iy);
                                g.DrawLine(pen, ix + off, iy + bw, ix + off, iy + bw + len);
                                g.DrawLine(pen, ix - len, iy + off, ix, iy + off);
                                g.DrawLine(pen, ix + bw, iy + off, ix + bw + len, iy + off);
                            }
                            break;
                        }
                    case GlyphKind.History:
                        {
                            Rectangle cir = new Rectangle(cx - s / 2, cy - s / 2, s, s);
                            g.DrawEllipse(pen, cir);
                            g.DrawLine(pen, cx, cy - s / 3, cx, cy);
                            g.DrawLine(pen, cx, cy, cx + s / 4, cy + s / 6);
                            break;
                        }
                    case GlyphKind.Status:
                        {
                            int bw = s / 5, gap = (s - 3 * bw) / 2;
                            g.FillRectangle(br, x, y + s / 3, bw, s - s / 3);
                            g.FillRectangle(br, x + bw + gap, y + s / 2, bw, s / 2);
                            g.FillRectangle(br, x + 2 * (bw + gap), y + 4, bw, s - 4);
                            break;
                        }
                    case GlyphKind.Settings:
                        {
                            int h = s / 3, sw = (int)(s * 0.42f);
                            for (int i = 0; i < 3; i++)
                            {
                                int yy = y + i * h + h / 2;
                                g.DrawLine(pen, x + 2, yy, x + s - 2, yy);
                                int kx = x + 2 + ((i == 1) ? sw + (s - sw) / 3 : (i == 2 ? (int)(s * 0.72f) : (int)(s * 0.22f)));
                                g.FillEllipse(br, kx - 4, yy - 4, 9, 9);
                            }
                            break;
                        }
                    case GlyphKind.Restore:
                        {
                            Rectangle cir = new Rectangle(cx - s / 2, cy - s / 2, s, s);
                            g.DrawArc(pen, cir, 40, 280);
                            Point[] a = new Point[] {
                                new Point(cx - s / 2, cy - (int)(s * 0.30f)),
                                new Point(cx - (int)(s * 0.20f), cy - (int)(s * 0.38f)),
                                new Point(cx - (int)(s * 0.30f), cy - (int)(s * 0.02f))
                            };
                            g.FillPolygon(br, a);
                            break;
                        }
                    case GlyphKind.Whitelist:
                    case GlyphKind.Shield:
                        {
                            Point[] shield = new Point[] {
                                new Point(cx, y + 2),
                                new Point(x + s - 4, y + s / 5),
                                new Point(x + s - 4, cy + s / 6),
                                new Point(cx, y + s - 2),
                                new Point(x + 4, cy + s / 6),
                                new Point(x + 4, y + s / 5)
                            };
                            g.DrawPolygon(pen, shield);
                            g.DrawLine(pen, cx - s / 5, cy, cx - 2, cy + s / 5);
                            g.DrawLine(pen, cx - 2, cy + s / 5, cx + s / 4, cy - s / 5);
                            break;
                        }
                    case GlyphKind.Scan:
                        {
                            int d = (int)(s * 0.62f);
                            Rectangle cir = new Rectangle(cx - d / 2 - 2, cy - d / 2 - 2, d, d);
                            g.DrawEllipse(pen, cir);
                            g.DrawLine(pen, cir.Right - 3, cir.Bottom - 3, x + s - 1, y + s - 1);
                            break;
                        }
                    case GlyphKind.Report:
                        {
                            int bw = (int)(s * 0.58f), bh = (int)(s * 0.76f);
                            int bx = cx - bw / 2, by = cy - bh / 2;
                            g.DrawRectangle(pen, bx, by, bw, bh);
                            for (int i = 0; i < 3; i++)
                                g.DrawLine(pen, bx + bw / 5, by + (int)(bh * (0.28f + i * 0.18f)), bx + bw - bw / 5, by + (int)(bh * (0.28f + i * 0.18f)));
                            break;
                        }
                    case GlyphKind.Trash:
                        {
                            int bw = (int)(s * 0.55f), bh = (int)(s * 0.62f);
                            int bx = cx - bw / 2, by = cy - bh / 2 + 4;
                            g.DrawRectangle(pen, bx, by, bw, bh);
                            g.DrawLine(pen, bx - 6, by - 4, bx + bw + 6, by - 4);
                            g.DrawLine(pen, cx - 8, by - 10, cx + 8, by - 10);
                            break;
                        }
                    case GlyphKind.Refresh:
                        {
                            Rectangle cir = new Rectangle(cx - s / 2, cy - s / 2, s, s);
                            g.DrawArc(pen, cir, 200, 140);
                            g.DrawArc(pen, cir, 20, 140);
                            Point[] a = new Point[] {
                                new Point(cx + s / 2, cy - s / 8),
                                new Point(cx + s / 2 - (int)(s * 0.22f), cy - (int)(s * 0.20f)),
                                new Point(cx + s / 2 - (int)(s * 0.18f), cy + (int)(s * 0.12f))
                            };
                            g.FillPolygon(br, a);
                            break;
                        }
                    case GlyphKind.Back:
                        {
                            g.DrawLine(pen, cx + s / 4, y + 4, cx - s / 4, cy);
                            g.DrawLine(pen, cx - s / 4, cy, cx + s / 4, y + s - 4);
                            break;
                        }
                }
            }
            g.SmoothingMode = old;
        }
    }

    /// <summary>控件快速布局助手。</summary>
    public static class Ui
    {
        public static T Add<T>(Control parent, T child, int x, int y, int w, int h) where T : Control
        {
            child.Bounds = new Rectangle(x, y, w, h);
            parent.Controls.Add(child);
            return child;
        }

        public static Panel Panel(Control parent, int x, int y, int w, int h, Color bg)
        {
            Panel p = new Panel();
            p.BackColor = bg;
            p.Bounds = new Rectangle(x, y, w, h);
            parent.Controls.Add(p);
            return p;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return "—";
            if (bytes == 0) return "0 B";
            double b = bytes;
            string[] u = new string[] { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1024 && i < 4) { b /= 1024; i++; }
            return (b >= 100 || i == 0 ? b.ToString("F0") : b.ToString("F1")) + " " + u[i];
        }

        public static string FormatDate(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "未知";
            string t = raw.Trim();
            if (t.Length == 8 && t.IndexOf("-") < 0)
                return t.Substring(0, 4) + "-" + t.Substring(4, 2) + "-" + t.Substring(6, 2);
            return raw;
        }
    }
}


