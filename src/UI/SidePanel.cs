using System;
using System.Drawing;
using System.Windows.Forms;

namespace MetroUninstaller
{
    /// <summary>从右侧滑入的详情面板：固定标题区 + 可滚动正文 + 固定操作区。</summary>
    public class SidePanel : Panel
    {
        public Panel Header;
        public ScrollPanel Body;
        public Panel Footer;
        public MetroButton CloseButton;
        public string Title = "";
        public string Subtitle = "";

        private Timer _slide;
        private int _targetLeft;
        private bool _closing;

        public event EventHandler CloseRequested;

        public SidePanel(int designWidth)
        {
            BackColor = Color.White;
            Width = Theme.S(designWidth);
            Visible = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;

            Header = new Panel();
            Header.BackColor = Color.White;
            Controls.Add(Header);

            Body = new ScrollPanel();
            Body.BackColor = Color.White;
            Body.AutoScroll = true;
            Controls.Add(Body);

            Footer = new Panel();
            Footer.BackColor = Color.White;
            Controls.Add(Footer);

            CloseButton = new MetroButton();
            CloseButton.Ghost = true;
            CloseButton.Text = "×";
            CloseButton.FontSize = 13f;
            CloseButton.Size = new Size(Theme.S(40), Theme.S(40));
            CloseButton.Click += delegate { if (CloseRequested != null) CloseRequested(this, EventArgs.Empty); };
            Header.Controls.Add(CloseButton);

            _slide = new Timer();
            _slide.Interval = 12;
            _slide.Tick += delegate
            {
                if (Parent == null) { _slide.Stop(); return; }
                int left = Left;
                int delta = _targetLeft - left;
                if (Math.Abs(delta) <= Theme.S(2))
                {
                    Left = _targetLeft;
                    _slide.Stop();
                    if (_closing)
                    {
                        _closing = false;
                        Visible = false;
                    }
                }
                else Left = left + (delta / 3 == 0 ? (delta > 0 ? Theme.S(8) : -Theme.S(8)) : delta / 3);
            };
        }

        public void ShowIn(Control host)
        {
            if (Parent != host) Parent = host;
            Parent.Controls.SetChildIndex(this, 0);
            Visible = true;
            _closing = false;
            Left = host.ClientSize.Width;
            _targetLeft = Math.Max(0, host.ClientSize.Width - Width);
            _slide.Start();
            BringToFront();
            Header.Height = Theme.S(126);
            LayoutParts();
        }

        public void HideSlide()
        {
            if (!Visible) return;
            _closing = true;
            _targetLeft = Parent == null ? Width : Parent.ClientSize.Width;
            _slide.Start();
        }

        public void LayoutParts()
        {
            if (Header == null) return;
            int hh = Theme.S(126);
            int fh = Theme.S(104);
            Header.SetBounds(0, 0, Width, hh);
            Body.SetBounds(1, hh, Width - 1, Math.Max(0, Height - hh - fh));
            Footer.SetBounds(1, Height - fh, Width - 1, fh);
            CloseButton.SetBounds(Width - Theme.S(58), Theme.S(18), Theme.S(40), Theme.S(40));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutParts();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Fill(g, ClientRectangle, Color.White);
            using (Pen p = new Pen(Theme.Line, 1))
            {
                g.DrawLine(p, 1, 0, 1, Height);
                g.DrawLine(p, 1, Theme.S(126) - 1, Width, Theme.S(126) - 1);
                g.DrawLine(p, 1, Height - Theme.S(104), Width, Height - Theme.S(104));
            }

            int w = Width - Theme.S(64);
            TextRenderer.DrawText(g, Title, Theme.Text(16f),
                new Rectangle(Theme.S(28), Theme.S(16), w, Theme.S(34)), Theme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (Subtitle.Length > 0)
            {
                TextRenderer.DrawText(g, Subtitle, Theme.Text(9f),
                    new Rectangle(Theme.S(28), Theme.S(56), w, Theme.S(58)), Theme.InkSoft,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }
    }
}


