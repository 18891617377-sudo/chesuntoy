using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MetroUninstaller
{
    /// <summary>页面基类：标题展示在顶栏，页面只负责内容区。</summary>
    public class MetroPage : UserControl
    {
        public string PageTitle = "";
        public string PageSubtitle = "";
        public MainForm Main;

        public MetroPage(MainForm main)
        {
            Main = main;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
        }
    }

    /// <summary>顶栏：返回按钮 + 大标题 + 权限状态。</summary>
    public class HeaderBar : Control
    {
        public string HeadTitle = "CHESUN Uninstaller";
        public string HeadSub = "";
        public bool ShowBack;
        private MetroButton _back;
        private MetroButton _elevate;
        private System.Windows.Forms.Timer _clock;
        private string _right = "";

        public event EventHandler BackClicked;

        public HeaderBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;

            _back = new MetroButton();
            _back.Ghost = true;
            _back.Icon = GlyphKind.Back;
            _back.Size = new Size(Theme.S(44), Theme.S(44));
            _back.Visible = false;
            _back.Click += delegate { if (BackClicked != null) BackClicked(this, EventArgs.Empty); };
            Controls.Add(_back);

            _elevate = new MetroButton();
            _elevate.Text = "以管理员身份重启";
            _elevate.Accent = true;
            _elevate.FontSize = 9f;
            _elevate.Size = new Size(Theme.S(176), Theme.S(34));
            _elevate.Visible = !Admin.IsAdmin;
            _elevate.Click += delegate { Admin.RelaunchElevated(); };
            Controls.Add(_elevate);

            _clock = new System.Windows.Forms.Timer();
            _clock.Interval = 2000;
            _clock.Tick += delegate { Refresh_(); };
            _clock.Start();
            Refresh_();
        }

        private void Refresh_()
        {
            _right = (Admin.IsAdmin ? "管理员" : "标准权限") + "  ·  " + DateTime.Now.ToString("HH:mm");
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_back == null || _elevate == null) return;
            _back.Location = new Point(Theme.S(18), Theme.S(26));
            _elevate.Location = new Point(Width - _elevate.Width - Theme.S(24), Theme.S(32));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Fill(g, ClientRectangle, Theme.Bg);
            Theme.LineH(g, 0, Width, Height - 1, Theme.Line);

            int x = ShowBack ? Theme.S(78) : Theme.S(28);
            TextRenderer.DrawText(g, HeadTitle, Theme.Display(26f),
                new Rectangle(x, Theme.S(14), Width - x - Theme.S(260), Theme.S(46)), Theme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            if (HeadSub.Length > 0)
            {
                TextRenderer.DrawText(g, HeadSub, Theme.Text(9f),
                    new Rectangle(x, Theme.S(58), Width - x - Theme.S(260), Theme.S(24)), Theme.InkSoft,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            int rightX = Width - Theme.S(24);
            if (_elevate.Visible) rightX = _elevate.Left - Theme.S(16);
            TextRenderer.DrawText(g, _right, Theme.Text(9f),
                new Rectangle(rightX - Theme.S(180), Theme.S(30), Theme.S(180), Theme.S(34)), Theme.InkSoft,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        public void Set(string title, string sub, bool showBack)
        {
            HeadTitle = title;
            HeadSub = sub;
            ShowBack = showBack;
            _back.Visible = showBack;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _clock != null) _clock.Dispose();
            base.Dispose(disposing);
        }
    }

    public class MainForm : Form
    {
        public List<InstalledApp> Apps;
        public List<DriverInfo> Drivers;
        public bool DataLoading;
        public event EventHandler DataReady;

        private HeaderBar _header;
        private Panel _content;
        private Label _status;
        private StartPage _start;
        private MetroPage _current;
        private string _lastLog = "就绪";

        public MainForm()
        {
            Text = "CHESUN Uninstaller · 专业级残留与内核驱动清理";
            BackColor = Theme.Bg;
            ForeColor = Theme.Ink;
            Font = Theme.Text(10f);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(Theme.S(960), Theme.S(620));

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(Theme.S(1280), wa.Width - Theme.S(24));
            int h = Math.Min(Theme.S(840), wa.Height - Theme.S(24));
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);

            _header = new HeaderBar();
            _header.BackClicked += delegate { GoHome(); };
            Controls.Add(_header);

            _content = new Panel();
            _content.BackColor = Theme.Bg;
            Controls.Add(_content);

            _status = new Label();
            _status.BackColor = Theme.Bg;
            _status.ForeColor = Theme.InkSoft;
            _status.Font = Theme.Text(8.5f);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(Theme.S(28), 0, 0, 0);
            _status.Text = "就绪 · 等待读取本机软件清单";
            Controls.Add(_status);

            Log.Entry += delegate (LogEntry e)
            {
                _lastLog = e.ToString();
                try { if (IsHandleCreated && !IsDisposed) BeginInvoke((MethodInvoker)delegate { _status.Text = _lastLog; }); }
                catch { }
            };

            _start = new StartPage(this);
            Navigate(_start);
            LayoutUi();
            LoadDataAsync();
        }

        private void LayoutUi()
        {
            if (_header == null || _status == null || _content == null) return;
            _header.Bounds = new Rectangle(0, 0, ClientSize.Width, Theme.S(96));
            _status.Bounds = new Rectangle(0, ClientSize.Height - Theme.S(26), ClientSize.Width, Theme.S(26));
            _content.Bounds = new Rectangle(0, Theme.S(96), ClientSize.Width, ClientSize.Height - Theme.S(96) - Theme.S(26));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutUi();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutUi();
        }

        public void Navigate(MetroPage page)
        {
            if (_current != null && _current != _start)
            {
                _content.Controls.Remove(_current);
                _current.Dispose();
            }
            _current = page;
            _content.Controls.Clear();
            page.Dock = DockStyle.Fill;
            _content.Controls.Add(page);
            _header.Set(page.PageTitle, page.PageSubtitle, page != _start);
            _status.Text = _lastLog;
        }

        public void GoHome()
        {
            if (_current != _start) Navigate(_start);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && _current != _start)
            {
                GoHome();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadDataAsync()
        {
            DataLoading = true;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    List<InstalledApp> apps = SoftwareInventory.Load(null);
                    Apps = apps;
                    Log.Info("已读取 " + apps.Count + " 个已安装程序。");
                }
                catch (Exception ex) { Log.Warn("读取软件清单失败：" + ex.Message); }

                try
                {
                    List<DriverInfo> ds = DriverInventory.LoadAll(null);
                    if (Apps != null) DriverInventory.AttributeAll(ds, Apps);
                    Drivers = ds;
                    int susp = 0;
                    foreach (DriverInfo d in ds) if (d.Suspicious) susp++;
                    Log.Info("已读取 " + ds.Count + " 个内核驱动，可疑 " + susp + " 个。");
                }
                catch (Exception ex) { Log.Warn("读取驱动清单失败：" + ex.Message); }

                DataLoading = false;
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (DataReady != null) DataReady(this, EventArgs.Empty);
                        });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Name = "inventory-loader";
            t.Start();
        }

        /// <summary>页面内的通用确认覆盖层。</summary>
        public Overlay ShowConfirm(MetroPage host, string title, string body, List<string> lines,
            string primary, bool danger, bool twoStep, EventHandler onConfirm)
        {
            Overlay ov = new Overlay();
            ov.Title = title;
            ov.Subtitle = body;
            ov.Items = lines ?? new List<string>();
            ov.PrimaryText = primary;
            ov.Danger = danger;
            ov.TwoStep = twoStep;
            ov.SecondaryText = "取消";
            ov.Primary += delegate (object s, EventArgs e) { if (onConfirm != null) onConfirm(s, e); };
            ov.Secondary += delegate (object s, EventArgs e) { host.Controls.Remove(ov); ov.Dispose(); };
            ov.Attach(host);
            return ov;
        }

        /// <summary>打开报告或目录。</summary>
        public static void OpenPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { Log.Warn("打开失败：" + path + " — " + ex.Message); }
        }
    }

    public class StartPage : MetroPage
    {
        private MetroTile _uninstall, _driver, _history, _status, _settings, _restore, _white;
        private Label _group1, _group2;

        public StartPage(MainForm main) : base(main)
        {
            PageTitle = "CHESUN Uninstaller";
            PageSubtitle = "Windows 8 Modern UI · 全量残留清理 + 内核驱动四维归属校验";
            AutoScroll = true;

            _uninstall = NewTile("软件卸载", "已安装程序 · 深度清理", "", GlyphKind.Uninstall, Theme.Accent, 310, 310, delegate
            {
                Main.Navigate(new SoftwarePage(Main));
            });
            _driver = NewTile("驱动管理", "可疑驱动 · 归属校验", "", GlyphKind.Driver, Theme.Red, 310, 310, delegate
            {
                Main.Navigate(new DriverPage(Main));
            });
            _history = NewTile("扫描历史", "清理报告归档", "", GlyphKind.History, Theme.Teal, 310, 150, delegate
            {
                Main.Navigate(new HistoryPage(Main));
            });
            _status = NewTile("系统状态", "待重启项 / 备份占用", "", GlyphKind.Status, Theme.Slate, 310, 150, delegate
            {
                Main.Navigate(new StatusPage(Main));
            });
            _settings = NewTile("设置", "还原点 · 备份策略", "", GlyphKind.Settings, Theme.Gray, 150, 150, delegate
            {
                Main.Navigate(new SettingsPage(Main));
            });
            _restore = NewTile("还原点", "回滚与备份恢复", "", GlyphKind.Restore, Theme.Green, 150, 150, delegate
            {
                Main.Navigate(new RestorePage(Main));
            });
            _white = NewTile("白名单", "永不删除保护", "", GlyphKind.Whitelist, Color.FromArgb(162, 0, 255), 150, 150, delegate
            {
                Main.Navigate(new WhitelistPage(Main));
            });

            _group1 = MakeGroupLabel("卸载与驱动");
            _group2 = MakeGroupLabel("工具与安全");

            Main.DataReady += delegate { RefreshStats(); };
            RefreshStats();
        }

        private Label MakeGroupLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.BackColor = Color.Transparent;
            l.ForeColor = Theme.InkSoft;
            l.Font = Theme.Text(9f);
            l.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(l);
            return l;
        }

        private MetroTile NewTile(string title, string sub, string value, GlyphKind kind, Color color,
            int w, int h, EventHandler onClick)
        {
            MetroTile t = new MetroTile();
            t.Title = title;
            t.Subtitle = sub;
            t.Value = value;
            t.Kind = kind;
            t.TileColor = color;
            t.SetSize(w, h);
            t.Click += onClick;
            Controls.Add(t);
            return t;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutTiles();
        }

        private void LayoutTiles()
        {
            if (_uninstall == null || _group1 == null || _group2 == null) return;
            int x0 = Theme.S(28);
            int y0 = Theme.S(18);
            int gap = Theme.S(10);

            _group1.SetBounds(x0, y0, Theme.S(600), Theme.S(24));
            int y1 = y0 + Theme.S(32);

            _uninstall.Location = new Point(x0, y1);
            _driver.Location = new Point(x0 + _uninstall.Width + gap, y1);
            _history.Location = new Point(x0 + 2 * (_uninstall.Width + gap), y1);
            _status.Location = new Point(x0 + 2 * (_uninstall.Width + gap), y1 + _history.Height + gap);

            int y2 = y1 + _uninstall.Height + Theme.S(24);
            _group2.SetBounds(x0, y2, Theme.S(600), Theme.S(24));
            int y3 = y2 + Theme.S(32);
            _settings.Location = new Point(x0, y3);
            _restore.Location = new Point(x0 + _settings.Width + gap, y3);
            _white.Location = new Point(x0 + 2 * (_settings.Width + gap), y3);

            AutoScrollMinSize = new Size(x0 + 3 * (_uninstall.Width + gap), y3 + _settings.Height + Theme.S(24));
        }

        public void RefreshStats()
        {
            try
            {
                if (Main.Apps != null)
                {
                    _uninstall.Value = Main.Apps.Count.ToString();
                    _uninstall.Unit = "个";
                    _uninstall.Subtitle = "已安装程序 · 全量残留清理";
                }
                else
                {
                    _uninstall.Value = "…";
                    _uninstall.Unit = "";
                    _uninstall.Subtitle = "正在读取本机软件清单";
                }

                if (Main.Drivers != null)
                {
                    int susp = 0, deletable = 0;
                    foreach (DriverInfo d in Main.Drivers)
                    {
                        if (d.Suspicious) susp++;
                        if (DriverInventory.IsDeletable(d)) deletable++;
                    }
                    _driver.Value = susp.ToString();
                    _driver.Unit = "个";
                    _driver.Subtitle = "无签名/可疑 · 可归属 " + deletable + " 个";
                }
                else
                {
                    _driver.Value = "…";
                    _driver.Unit = "";
                    _driver.Subtitle = "正在校验驱动签名";
                }

                List<HistoryEntry> hist = HistoryStore.List();
                _history.Subtitle = hist.Count == 0 ? "暂无清理记录" : "最近 " + hist.Count + " 份清理报告";
                _status.Subtitle = "待重启 " + PendingDelete.CountPending() + " 项 · 备份 " +
                    Ui.FormatSize(BackupService.TotalBytes());
                _settings.Subtitle = "还原点" + (Config.CreateRestorePoint ? "开" : "关") +
                    " · 备份 " + Config.BackupDays + " 天";
                _white.Subtitle = SafetyGuard.UserWhitelist.Count + " 条自定义保护";
                _restore.Subtitle = BackupService.List().Count + " 份可恢复备份";
            }
            catch { }
        }
    }
}






