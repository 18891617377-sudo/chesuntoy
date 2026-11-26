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
        private bool _signaturePrompted;

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
            if (_signaturePrompted) return;
            _signaturePrompted = true;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    SignatureInfo sig = SignatureVerifier.Check(Application.ExecutablePath);
                    if (!SigningHelper.IsTrusted(sig)) ShowSigningDialog(_start);
                });
            }
            catch { }
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

        /// <summary>未签名时的签名 / 测试签名说明与工具包入口。</summary>
        public void ShowSigningDialog(MetroPage host)
        {
            SignatureInfo sig = null;
            string exe = "";
            try
            {
                exe = Application.ExecutablePath;
                sig = SignatureVerifier.Check(exe);
            }
            catch { }
            bool trusted = SigningHelper.IsTrusted(sig);

            Overlay ov = new Overlay();
            ov.Title = trusted ? "代码签名状态" : "当前版本未签名";
            ov.Subtitle = trusted
                ? "WinVerifyTrust 校验通过；若仍被拦截，多半是 WDAC 策略只允许特定发布者。"
                : "Windows 可能直接拦截未签名程序。下面给本机自签名、正式签名和测试签名的区别。";
            ov.Items = new List<string>
            {
                "当前状态：" + SigningHelper.Describe(sig),
                "程序文件：" + exe,
                "方式 A（推荐）：生成工具包，运行 make-test-sign.ps1",
                "→ 自动签出 CHESUNUninstaller-signed.exe，双击 run-signed.cmd",
                "方式 B（正式发布）：购买 OV/EV 证书，用 signtool 签原 exe",
                "方式 C（测试签名）：只放宽内核驱动校验；设置页可开关，不能放行本 exe",
                "方式 D（沙盒）：run-in-sandbox.wsb 内不需要签名",
                "方式 E（独立驱动）：设置页“反 Rootkit 驱动签名”，只给 chesunark.sys 签名，不给本程序签名",
                "自签名证书只在本机可信，不要拿去给公开发行版签名",
                "工具包需要 signtool.exe（Windows SDK 的 Code Signing Tools），缺失时脚本会给下载地址",
                "详细步骤与边界见工具包内 README.txt"
            };
            ov.PrimaryText = "生成签名工具包";
            ov.SecondaryText = "稍后";
            ov.Primary += delegate
            {
                host.Controls.Remove(ov);
                ov.Dispose();
                try
                {
                    string dir;
                    SigningHelper.BuildKit(Application.ExecutablePath, out dir);
                    ShowKitReady(host, dir);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("生成签名工具包失败：\n" + ex.Message, "CHESUN Uninstaller",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            ov.Secondary += delegate { host.Controls.Remove(ov); ov.Dispose(); };
            ov.Attach(host);
        }

        private void ShowKitReady(MetroPage host, string dir)
        {
            Overlay ov = new Overlay();
            ov.Title = "签名工具包已生成";
            ov.Subtitle = "按 README.txt 三步走；证书只在本机可信";
            ov.Items = new List<string>
            {
                "目录：" + dir,
                "1. 右键 make-test-sign.ps1 → 使用 PowerShell 运行",
                "   若提示禁止脚本：Set-ExecutionPolicy -Scope Process Bypass",
                "2. 脚本自动创建证书、信任、签出 CHESUNUninstaller-signed.exe 并 verify",
                "3. 双击 run-signed.cmd 启动签名副本",
                "",
                "没有 signtool：脚本会提示安装 Windows SDK 的 Code Signing Tools。",
                "testsigning 只对内核驱动有效，本 exe 仍需要 Authenticode 签名或 WDAC 放行。"
            };
            ov.PrimaryText = "打开目录";
            ov.SecondaryText = "关闭";
            ov.Primary += delegate { host.Controls.Remove(ov); ov.Dispose(); MainForm.OpenPath(dir); };
            ov.Secondary += delegate { host.Controls.Remove(ov); ov.Dispose(); };
            ov.Attach(host);
        }

        /// <summary>独立反 Rootkit 内核驱动的签名 / 安装入口。只给 .sys 签名，不碰本程序 exe。</summary>
        public void ShowDriverSigningDialog(MetroPage host)
        {
            Overlay ov = new Overlay();
            ov.Title = "反 Rootkit 内核驱动签名";
            ov.Subtitle = "这不是给 CHESUNUninstaller.exe 签名，是给你自己编译的 chesunark.sys 签名。";
            ov.Items = new List<string>
            {
                "驱动能力：ZwUnloadDriver 热卸载、删 Services 键、删驱动文件",
                "不做：摘内核对象、改 SSDT/inline hook/回调、绕 PatchGuard",
                "当前驱动设备：" + (KernelDriverClient.IsAvailable ? "已安装，可优先热卸载" : "未安装（仍可用服务停止 + 重启删除）"),
                "",
                "1. 选择 chesunark.sys（或你编译出的同名驱动）",
                "2. 生成工具包 → 运行 make-test-sign-driver.ps1（自动建证书、签名、verify）",
                "3. 运行 enable-testsigning.cmd → 重启（桌面会显示测试模式水印）",
                "4. 运行 install-driver.cmd 安装驱动",
                "",
                "没有 DriverUnload 的 rootkit 驱动无法热卸载；本工具会转禁用服务 + 重启删除。",
                "EFI 分区里的 bootkit 仍用 WinPE 离线包 clean-efi.cmd。"
            };
            ov.PrimaryText = "选择 .sys 生成工具包";
            ov.SecondaryText = "取消";
            ov.Primary += delegate
            {
                host.Controls.Remove(ov);
                ov.Dispose();
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Title = "选择反 Rootkit 驱动（.sys）";
                    dlg.Filter = "内核驱动 (*.sys)|*.sys|所有文件 (*.*)|*.*";
                    dlg.CheckFileExists = true;
                    if (dlg.ShowDialog(host) != DialogResult.OK) return;
                    try
                    {
                        string dir;
                        SigningHelper.BuildDriverKit(dlg.FileName, out dir);
                        ShowDriverKitReady(host, dir, dlg.FileName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("生成驱动签名工具包失败：\n" + ex.Message, "CHESUN Uninstaller",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            };
            ov.Secondary += delegate { host.Controls.Remove(ov); ov.Dispose(); };
            ov.Attach(host);
        }

        private void ShowDriverKitReady(MetroPage host, string dir, string sourceSys)
        {
            Overlay ov = new Overlay();
            ov.Title = "驱动签名工具包已生成";
            ov.Subtitle = "先签名，再开 testsigning，重启后安装；证书只在本机有效";
            ov.Items = new List<string>
            {
                "目录：" + dir,
                "源驱动：" + sourceSys,
                "1. 右键 make-test-sign-driver.ps1 → 使用 PowerShell 运行（自动 UAC）",
                "2. 右键 enable-testsigning.cmd → 管理员运行，重启电脑",
                "3. 右键 install-driver.cmd → 管理员运行",
                "4. 回到驱动页，扫描可疑驱动后点“卸载该驱动”",
                "没有 signtool：脚本会提示安装 Windows SDK Code Signing Tools",
                "只卸载有 DriverUnload 的驱动；没有的走重启删除/离线包"
            };
            ov.PrimaryText = "打开目录";
            ov.SecondaryText = "关闭";
            ov.Primary += delegate { host.Controls.Remove(ov); ov.Dispose(); MainForm.OpenPath(dir); };
            ov.Secondary += delegate { host.Controls.Remove(ov); ov.Dispose(); };
            ov.Attach(host);
        }

        /// <summary>测试签名开关。只影响内核驱动，必须让用户明确知道副作用。</summary>
        public void ShowTestSigningDialog(MetroPage host)
        {
            string state = SigningHelper.TestSigningState();
            bool on = state == "已启用";
            Overlay ov = new Overlay();
            ov.Title = "测试签名（仅内核驱动）";
            ov.Subtitle = "testsigning 会降低内核安全性，并让桌面显示“测试模式”水印；重启后生效。";
            ov.Items = new List<string>
            {
                "当前状态：" + state,
                "",
                "作用：允许加载“测试签名”的内核驱动，用于驱动开发/调试。",
                "不作用：不会让未签名的用户态 exe 绕过 WDAC / SmartScreen；",
                "本工具的 exe 仍然需要 Authenticode 签名，或 WDAC 放行、沙盒运行。",
                "",
                "启用后 Windows 内核不再强制要求正式驱动签名，恶意驱动也更容易加载；",
                "仅在自己的测试机、短期调试时使用，用完点“关闭 testsigning”并重启。",
                "",
                "需要管理员权限；本程序已提权时可直接执行 bcdedit。"
            };
            ov.PrimaryText = on ? "关闭 testsigning" : "启用 testsigning";
            ov.Danger = !on;
            ov.SecondaryText = "取消";
            ov.Primary += delegate
            {
                string err = SigningHelper.SetTestSigning(!on);
                host.Controls.Remove(ov);
                ov.Dispose();
                if (err == null)
                    MessageBox.Show((on ? "已关闭 testsigning。" : "已启用 testsigning。") + "\n重启后生效。",
                        "CHESUN Uninstaller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show("设置失败：\n" + err, "CHESUN Uninstaller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            ov.Secondary += delegate { host.Controls.Remove(ov); ov.Dispose(); };
            ov.Attach(host);
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






