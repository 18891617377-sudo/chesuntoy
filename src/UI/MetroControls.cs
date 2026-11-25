using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MetroUninstaller
{
    /// <summary>磁贴：310×310 / 310×150 / 150×150，纯色块，无圆角无阴影。</summary>
    public class MetroTile : Control
    {
        public string Title = "";
        public string Subtitle = "";
        public string Value = "";
        public string Unit = "";
        public Color TileColor = Theme.Accent;
        public GlyphKind Kind = GlyphKind.Uninstall;
        public int DesignWidth = 310;
        public int DesignHeight = 310;
        private bool _hover;

        public MetroTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            BackColor = Theme.Accent;
        }

        public void SetSize(int w, int h)
        {
            DesignWidth = w;
            DesignHeight = h;
            Size = new Size(Theme.S(w), Theme.S(h));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;
            Color bg = _hover ? Theme.Lighten(TileColor, 16) : TileColor;
            using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, r);

            int pad = Theme.S(DesignWidth >= 300 ? 26 : 18);
            bool large = DesignHeight >= 300;
            int icon = Theme.S(large ? 54 : (DesignHeight >= 140 ? 34 : 30));
            Theme.Glyph(g, Kind, new Rectangle(pad, pad, icon, icon), Color.FromArgb(240, 255, 255, 255));

            int titleSize = large ? 20 : (DesignHeight >= 140 ? 16 : 13);
            int subSize = large ? 10 : (DesignHeight >= 140 ? 9 : 8);

            int titleH = Theme.S(titleSize + 10);
            int subH = Theme.S(subSize + 10);
            int titleY = Height - Theme.S(large ? 74 : 46) - titleH / 2;
            int subY = Height - Theme.S(large ? 40 : 24);

            if (Value.Length > 0)
            {
                Font vf = Theme.Num(large ? 38f : 28f);
                int valueH = Theme.S(large ? 66 : 50);
                Rectangle vr = new Rectangle(pad, titleY - valueH - Theme.S(4), Width - pad * 2, valueH);
                TextRenderer.DrawText(g, Value, vf, vr, Color.White,
                    TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPrefix);
                if (Unit.Length > 0)
                {
                    Size vs = TextRenderer.MeasureText(g, Value, vf, new Size(0, 0), TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, Unit, Theme.Text(9f),
                        new Rectangle(pad + vs.Width + Theme.S(4), vr.Bottom - Theme.S(20), vr.Width, Theme.S(20)),
                        Color.FromArgb(210, 255, 255, 255),
                        TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPrefix);
                }
            }

            TextRenderer.DrawText(g, Title, Theme.Text((float)titleSize),
                new Rectangle(pad, titleY, Width - pad * 2, titleH), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            if (Subtitle.Length > 0)
            {
                TextRenderer.DrawText(g, Subtitle, Theme.Text((float)subSize),
                    new Rectangle(pad, subY - Theme.S(6), Width - pad * 2, subH), Color.FromArgb(200, 255, 255, 255),
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>扁平方形按钮，强调色/中性/危险三种。</summary>
    public class MetroButton : Control
    {
        public bool Accent;
        public bool Danger;
        public bool Outline;
        public bool Ghost;
        public GlyphKind? Icon;
        public float FontSize = 11f;
        private bool _hover, _down;

        public MetroButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
            BackColor = Theme.BgCard;
            Font = Theme.Text(11f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_down && e.Button == MouseButtons.Left)
            {
                _down = false; Invalidate();
                if (ClientRectangle.Contains(e.Location)) OnClick(EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color baseColor = Ghost ? Theme.Bg
                : (Accent ? Theme.Accent : (Danger ? Theme.Red : (Outline ? Theme.Bg : Color.FromArgb(206, 206, 206))));
            if (!Enabled) baseColor = Color.FromArgb(226, 226, 226);
            Color draw = baseColor;
            if (_down) draw = Theme.Darken(baseColor, 18);
            else if (_hover) draw = Ghost ? Color.FromArgb(214, 214, 214) : Theme.Lighten(baseColor, 14);

            Theme.Fill(g, ClientRectangle, draw);
            if (Outline)
            {
                using (Pen p = new Pen(Accent ? Theme.Accent : Theme.Line, 1))
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }

            Color ink = Accent || Danger ? Color.White : (_hover && !Enabled ? Theme.InkSoft : Theme.Ink);
            if (Outline && Accent) ink = Theme.Accent;
            if (!Enabled) ink = Theme.InkFaint;

            Rectangle textRect = ClientRectangle;
            if (Icon.HasValue)
            {
                int ic = Math.Min(Height - Theme.S(16), Theme.S(22));
                Rectangle ir = new Rectangle(Theme.S(14), (Height - ic) / 2, ic, ic);
                Theme.Glyph(g, Icon.Value, ir, Accent || Danger ? Color.White : Theme.Ink);
                textRect = new Rectangle(Theme.S(14) + ic + Theme.S(8), 0, Width - (Theme.S(14) + ic + Theme.S(8)) - Theme.S(8), Height);
                TextRenderer.DrawText(g, Text, Theme.Text(FontSize), textRect, ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            else
            {
                TextRenderer.DrawText(g, Text, Theme.Text(FontSize), textRect, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            if (Focused && ShowFocusCues)
            {
                using (Pen p = new Pen(Color.FromArgb(140, Theme.Ink), 1))
                    g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
            }
        }
    }

    /// <summary>长条磁贴行：图标块 + 主标题 + 副标题 + 右侧信息 + 色块标签。</summary>
    public class MetroRow : Control
    {
        public string Title = "";
        public string Subtitle = "";
        public string RightText = "";
        public string Chip = "";
        public Color ChipColor = Theme.Red;
        public Color IconColor = Theme.Accent;
        public string Initial = "";
        public object Data;
        public bool Selected;
        public bool Danger;
        public bool IsAction;
        private bool _hover;

        public MetroRow()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Height = Theme.S(70);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;
            Color bg = Selected ? Theme.Accent : (_hover ? Theme.BgWhite : Theme.Bg);
            Theme.Fill(g, r, bg);
            Color ink = Selected ? Color.White : Theme.Ink;
            Color sub = Selected ? Color.FromArgb(225, 255, 255, 255) : Theme.InkSoft;

            int pad = Theme.S(16);
            int ih = Math.Max(Theme.S(28), Height - Theme.S(22));
            int iy = (Height - ih) / 2;
            Rectangle ir = new Rectangle(pad, iy, ih, ih);
            Theme.Fill(g, ir, Selected ? Theme.Darken(Theme.Accent, 26) : IconColor);
            string initial = Initial.Length > 0 ? Initial : (Title.Length > 0 ? Title.Substring(0, 1) : "?");
            TextRenderer.DrawText(g, initial, Theme.Text(13f), ir, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int x = ir.Right + pad;
            int rightW = 0;
            if (RightText.Length > 0)
            {
                Size rs = TextRenderer.MeasureText(g, RightText, Theme.Text(10f), new Size(0, 0), TextFormatFlags.NoPrefix);
                rightW = rs.Width + pad;
                TextRenderer.DrawText(g, RightText, Theme.Text(10f),
                    new Rectangle(Width - pad - rs.Width, 0, rs.Width, Height), sub,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            int tagW = 0;
            if (Chip.Length > 0)
            {
                Size ts = TextRenderer.MeasureText(g, Chip, Theme.Text(9f), new Size(0, 0), TextFormatFlags.NoPrefix);
                tagW = ts.Width + Theme.S(16);
                int th = Theme.S(24);
                Rectangle tr = new Rectangle(Width - pad - rightW - tagW, (Height - th) / 2, tagW, th);
                Theme.Fill(g, tr, Selected ? Color.FromArgb(220, 255, 255, 255) : ChipColor);
                TextRenderer.DrawText(g, Chip, Theme.Text(9f), tr,
                    Selected ? Theme.Accent : Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            int textW = Width - x - pad - rightW - (tagW > 0 ? tagW + Theme.S(10) : 0);
            TextRenderer.DrawText(g, Title, Theme.Text(12f),
                new Rectangle(x, Theme.S(12), textW, Theme.S(24)), ink,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (Subtitle.Length > 0)
            {
                TextRenderer.DrawText(g, Subtitle, Theme.Text(9f),
                    new Rectangle(x, Theme.S(38), textW, Height - Theme.S(38) - Theme.S(8)), sub,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            if (Danger)
            {
                Theme.Fill(g, new Rectangle(0, 0, Theme.S(4), Height), Theme.Red);
            }
            if (!Selected) Theme.LineH(g, pad, Width - pad, Height - 1, Theme.LineSoft);
        }
    }

    /// <summary>扁平搜索框（内部嵌一个无边框 TextBox）。</summary>
    public class MetroSearch : Control
    {
        private TextBox _box;
        private string _placeholder = "搜索…";
        private bool _focused;
        public event EventHandler QueryChanged;

        public MetroSearch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;

            _box = new TextBox();
            _box.BorderStyle = BorderStyle.None;
            _box.Font = Theme.Text(11f);
            _box.BackColor = Color.White;
            _box.ForeColor = Theme.InkFaint;
            _box.Text = _placeholder;
            _box.GotFocus += delegate
            {
                _focused = true;
                if (_box.Text == _placeholder) { _box.Text = ""; _box.ForeColor = Theme.Ink; }
                Invalidate();
            };
            _box.LostFocus += delegate
            {
                _focused = false;
                if (_box.Text.Trim().Length == 0) { _box.Text = _placeholder; _box.ForeColor = Theme.InkFaint; }
                Invalidate();
            };
            _box.TextChanged += delegate
            {
                if (QueryChanged != null) QueryChanged(this, EventArgs.Empty);
            };
            Controls.Add(_box);
        }

        public string Query
        {
            get { return _box.Text == _placeholder ? "" : _box.Text.Trim(); }
            set { _box.Text = value; }
        }

        public void SetPlaceholder(string p) { _placeholder = p; if (!_focused) _box.Text = p; }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int pad = Theme.S(14);
            int h = _box.PreferredHeight;
            _box.SetBounds(pad, (Height - h) / 2, Width - pad * 2, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Fill(g, ClientRectangle, Color.White);
            using (Pen p = new Pen(_focused ? Theme.Accent : Theme.Line, _focused ? 2 : 1))
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            int s = Theme.S(14);
            Theme.Glyph(g, GlyphKind.Scan, new Rectangle(Width - s - Theme.S(12), (Height - s) / 2, s, s), Theme.InkFaint);
        }

        public void FocusBox() { _box.Focus(); }
    }

    /// <summary>面板式滚动容器（双缓冲，减少长列表闪烁）。</summary>
    public class ScrollPanel : Panel
    {
        public ScrollPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            AutoScroll = true;
            BackColor = Theme.Bg;
        }
    }

    /// <summary>全屏半透明覆盖层 + 居中卡片；支持两步确认与忙碌态。</summary>
    public class Overlay : Control
    {
        public string Title = "";
        public string Subtitle = "";
        public List<string> Items = new List<string>();
        public string PrimaryText = "确认";
        public string SecondaryText = "取消";
        public bool Danger;
        public bool Busy;
        public bool TwoStep;
        private bool _stage2;
        public event EventHandler Primary;
        public event EventHandler Secondary;

        private Bitmap _backdrop;
        private Rectangle _primaryRect;
        private Rectangle _secondaryRect;
        private int _hover;   // 1 primary, 2 secondary

        public Overlay()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            TabStop = true;
        }

        public void Attach(Control host)
        {
            Dock = DockStyle.Fill;
            CaptureBackdrop(host);
            host.Controls.Add(this);
            BringToFront();
            Focus();
        }

        private void CaptureBackdrop(Control host)
        {
            try
            {
                if (host.Width <= 0 || host.Height <= 0) return;
                _backdrop = new Bitmap(host.Width, host.Height);
                host.DrawToBitmap(_backdrop, new Rectangle(0, 0, host.Width, host.Height));
            }
            catch { _backdrop = null; }
        }

        public void Reset()
        {
            _stage2 = false;
            Title = "";
            Subtitle = "";
            Items.Clear();
            PrimaryText = "确认";
            SecondaryText = "取消";
            Danger = false;
            Busy = false;
            TwoStep = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            if (_backdrop != null) g.DrawImage(_backdrop, ClientRectangle);
            else Theme.Fill(g, ClientRectangle, Theme.Bg);
            Theme.Fill(g, ClientRectangle, Color.FromArgb(170, 0, 0, 0));

            int cw = Math.Min(Theme.S(940), Width - Theme.S(80));
            int lineH = Theme.S(26);
            int rows = Math.Min(Items.Count, 12);
            int ch = Theme.S(120) + rows * lineH + (Items.Count > rows ? Theme.S(24) : 0) + Theme.S(120);
            ch = Math.Min(ch, Height - Theme.S(80));
            Rectangle card = new Rectangle((Width - cw) / 2, (Height - ch) / 2, cw, ch);
            Theme.Fill(g, card, Color.White);

            int pad = Theme.S(48);
            int y = card.Y + pad - Theme.S(10);
            TextRenderer.DrawText(g, Title, Theme.Display(28f), new Rectangle(card.X + pad, y, card.Width - pad * 2, Theme.S(54)),
                Theme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += Theme.S(56);

            if (Subtitle.Length > 0)
            {
                TextRenderer.DrawText(g, Subtitle, Theme.Text(10f), new Rectangle(card.X + pad, y, card.Width - pad * 2, Theme.S(44)),
                    Theme.InkSoft, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                y += Theme.S(46);
            }

            int listTop = y;
            int maxRowsHeight = rows * lineH;
            for (int i = 0; i < rows; i++)
            {
                string line = Items[i];
                Color c = Theme.Ink;
                if (line.StartsWith("!")) { c = Theme.Red; line = line.Substring(1); }
                else if (line.StartsWith("~")) { c = Theme.Orange; line = line.Substring(1); }
                TextRenderer.DrawText(g, line, Theme.Text(9.5f),
                    new Rectangle(card.X + pad, listTop + i * lineH, card.Width - pad * 2, lineH), c,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            if (Items.Count > rows)
            {
                TextRenderer.DrawText(g, "… 其余 " + (Items.Count - rows) + " 项已省略",
                    Theme.Text(9f), new Rectangle(card.X + pad, listTop + rows * lineH, card.Width - pad * 2, Theme.S(24)),
                    Theme.InkFaint, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }

            // 按钮
            _primaryRect = Rectangle.Empty;
            _secondaryRect = Rectangle.Empty;
            if (!Busy)
            {
                int bh = Theme.S(56);
                int bw = Theme.S(200);
                int bx = card.Right - pad - bw;
                int by = card.Bottom - pad - bh;
                _primaryRect = new Rectangle(bx, by, bw, bh);
                Theme.Fill(g, _primaryRect, _hover == 1 ? Theme.Lighten(Danger ? Theme.Red : Theme.Accent, 14)
                    : (Danger ? Theme.Red : Theme.Accent));
                TextRenderer.DrawText(g, PrimaryText, Theme.Text(11f), _primaryRect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                if (SecondaryText.Length > 0)
                {
                    int b2 = Theme.S(170);
                    _secondaryRect = new Rectangle(bx - Theme.S(10) - b2, by, b2, bh);
                    Theme.Fill(g, _secondaryRect, _hover == 2 ? Color.FromArgb(196, 196, 196) : Color.FromArgb(214, 214, 214));
                    TextRenderer.DrawText(g, SecondaryText, Theme.Text(11f), _secondaryRect, Theme.Ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
            else
            {
                TextRenderer.DrawText(g, "执行中，请稍候…", Theme.Text(10f),
                    new Rectangle(card.X + pad, card.Bottom - pad - Theme.S(30), card.Width - pad * 2, Theme.S(30)),
                    Theme.InkSoft, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = 0;
            if (_primaryRect.Contains(e.Location)) h = 1;
            else if (_secondaryRect.Contains(e.Location)) h = 2;
            if (h != _hover) { _hover = h; Cursor = h > 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_primaryRect.Contains(e.Location)) DoPrimary();
                else if (_secondaryRect.Contains(e.Location)) { if (Secondary != null) Secondary(this, EventArgs.Empty); }
            }
            base.OnMouseUp(e);
        }

        public void DoPrimary()
        {
            if (Busy) return;
            if (TwoStep && !_stage2)
            {
                _stage2 = true;
                Title = "二次确认";
                Subtitle = "这是最后一次确认。删除后只能通过备份或系统还原点恢复。";
                PrimaryText = "永久删除";
                Danger = true;
                Invalidate();
                return;
            }
            if (Primary != null) Primary(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _backdrop != null) { try { _backdrop.Dispose(); } catch { } _backdrop = null; }
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { if (Secondary != null) Secondary(this, EventArgs.Empty); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}




