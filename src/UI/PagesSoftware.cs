using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MetroUninstaller
{
    /// <summary>软件列表页：搜索、深度扫描、侧滑详情、一键卸载。</summary>
    public class SoftwarePage : MetroPage
    {
        private class AppRow
        {
            public InstalledApp App;
            public MetroRow Row;
        }

        private MetroSearch _search;
        private MetroButton _scanTop, _refresh;
        private MetroButton _btnUninstall, _btnScan;
        private ScrollPanel _list;
        private Label _empty, _countLabel, _footerNote;
        private SidePanel _detail;
        private List<AppRow> _rows = new List<AppRow>();
        private InstalledApp _selected;
        private List<ResidueItem> _items;
        private List<DriverInfo> _matched;
        private bool _busy;
        private int _dy;

        public SoftwarePage(MainForm main) : base(main)
        {
            PageTitle = "软件卸载";
            PageSubtitle = "选中软件 → 一键终止进程、执行卸载、深度清理残留、清理归属驱动";

            _search = new MetroSearch();
            _search.SetPlaceholder("搜索软件名称或发布者…");
            _search.QueryChanged += delegate { ApplyFilter(); };
            Controls.Add(_search);

            _scanTop = new MetroButton();
            _scanTop.Text = "一键深度扫描";
            _scanTop.Accent = true;
            _scanTop.FontSize = 10f;
            _scanTop.Click += delegate { BeginUninstall(); };
            Controls.Add(_scanTop);

            _refresh = new MetroButton();
            _refresh.Text = "刷新";
            _refresh.FontSize = 10f;
            _refresh.Click += delegate { RefreshInventory(); };
            Controls.Add(_refresh);

            _countLabel = new Label();
            _countLabel.AutoSize = false;
            _countLabel.BackColor = Theme.Bg;
            _countLabel.ForeColor = Theme.InkSoft;
            _countLabel.Font = Theme.Text(9f);
            _countLabel.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_countLabel);

            _list = new ScrollPanel();
            _list.Resize += delegate { LayoutRows(); };
            Controls.Add(_list);

            _empty = new Label();
            _empty.AutoSize = false;
            _empty.BackColor = Theme.Bg;
            _empty.ForeColor = Theme.InkSoft;
            _empty.Font = Theme.Display(18f);
            _empty.TextAlign = ContentAlignment.MiddleCenter;
            _empty.Text = "正在读取已安装程序清单…";
            Controls.Add(_empty);

            _detail = new SidePanel(430);
            _detail.CloseRequested += delegate { _detail.HideSlide(); ClearSelection(); };
            Controls.Add(_detail);

            _btnUninstall = new MetroButton();
            _btnUninstall.Text = "一键卸载";
            _btnUninstall.Accent = true;
            _btnUninstall.FontSize = 12f;
            _btnUninstall.Click += delegate { BeginUninstall(); };
            _detail.Footer.Controls.Add(_btnUninstall);

            _btnScan = new MetroButton();
            _btnScan.Text = "深度扫描";
            _btnScan.Outline = true;
            _btnScan.FontSize = 10f;
            _btnScan.Click += delegate { if (_selected != null) StartScan(_selected, false); };
            _detail.Footer.Controls.Add(_btnScan);

            _footerNote = new Label();
            _footerNote.AutoSize = false;
            _footerNote.BackColor = Color.White;
            _footerNote.ForeColor = Theme.InkSoft;
            _footerNote.Font = Theme.Text(8.5f);
            _footerNote.TextAlign = ContentAlignment.MiddleLeft;
            _footerNote.Text = "自动备份 · 保留 " + Config.BackupDays + " 天";
            _detail.Footer.Controls.Add(_footerNote);

            Main.DataReady += delegate
            {
                if (!IsDisposed) BeginInvoke((MethodInvoker)delegate { BuildList(); });
            };

            LoadInventoryView();
        }

        private void LoadInventoryView()
        {
            if (Main.Apps == null)
            {
                _empty.Visible = true;
                _empty.Text = "正在读取已安装程序清单…";
                _countLabel.Text = "读取中";
                return;
            }
            BuildList();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_search == null || _detail == null || _list == null || _countLabel == null || _empty == null) return;
            int pad = Theme.S(24);
            int top = Theme.S(8);
            int h = Theme.S(42);

            _search.SetBounds(pad, top, Math.Min(Theme.S(420), Math.Max(Theme.S(200), Width / 2)), h);
            _scanTop.SetBounds(_search.Right + Theme.S(12), top, Theme.S(170), h);
            _refresh.SetBounds(_scanTop.Right + Theme.S(10), top, Theme.S(96), h);
            _countLabel.SetBounds(Width - pad - Theme.S(240), top, Theme.S(240), h);
            _list.SetBounds(0, Theme.S(62), Width, Math.Max(0, Height - Theme.S(68)));
            _empty.SetBounds(0, Theme.S(120), Width, Theme.S(120));

            _detail.SetBounds(_detail.Left, 0, _detail.Width, Height);
            if (!_detail.Visible) _detail.Left = Width;
            LayoutFooter();
            LayoutRows();
        }

        private void LayoutFooter()
        {
            if (_btnUninstall == null) return;
            int fw = _detail.Footer.Width;
            _btnUninstall.Size = new Size(Theme.S(206), Theme.S(58));
            _btnUninstall.Location = new Point(fw - Theme.S(24) - _btnUninstall.Width, Theme.S(22));
            _btnScan.Size = new Size(Theme.S(146), Theme.S(56));
            _btnScan.Location = new Point(_btnUninstall.Left - Theme.S(10) - _btnScan.Width, Theme.S(23));
            _footerNote.SetBounds(Theme.S(24), Theme.S(26), Math.Max(Theme.S(40), _btnScan.Left - Theme.S(40)), Theme.S(50));
        }

        private void BuildList()
        {
            if (Main.Apps == null) return;
            _list.SuspendLayout();
            _list.Controls.Clear();
            _rows.Clear();
            _selected = null;

            int max = 500;
            foreach (InstalledApp app in Main.Apps)
            {
                if (_rows.Count >= max) break;
                AppRow ar = new AppRow();
                ar.App = app;
                ar.Row = new MetroRow();
                ar.Row.Title = app.Name;
                ar.Row.Subtitle = BuildSub(app);
                ar.Row.RightText = app.EstimatedBytes >= 0 ? Ui.FormatSize(app.EstimatedBytes) : "";
                ar.Row.IconColor = Theme.ColorFor(app.Name);
                ar.Row.Initial = app.Name.Length > 0 ? app.Name.Substring(0, 1) : "?";
                ar.Row.Height = Theme.S(72);
                ar.Row.Data = app;
                ar.Row.Click += delegate (object s, EventArgs e) { SelectApp(ar); };
                _list.Controls.Add(ar.Row);
                _rows.Add(ar);
            }
            _list.ResumeLayout();
            ApplyFilter();

            _empty.Visible = _rows.Count == 0;
            if (_rows.Count == 0) _empty.Text = "未发现已安装程序";
            LayoutRows();
        }

        private string BuildSub(InstalledApp app)
        {
            string pub = app.Publisher.Length > 0 ? app.Publisher : "未知发布者";
            string ver = app.Version.Length > 0 ? app.Version : "版本未知";
            string date = app.InstallDate.Length > 0 ? "安装于 " + Ui.FormatDate(app.InstallDate) : "安装日期未知";
            return pub + "  ·  " + ver + "  ·  " + date + (app.InstallLocation.Length > 0 ? "  ·  " + app.InstallLocation : "");
        }

        private void ApplyFilter()
        {
            string q = _search.Query.ToLowerInvariant();
            int shown = 0;
            foreach (AppRow ar in _rows)
            {
                bool match = q.Length == 0 ||
                    ar.App.Name.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0 ||
                    ar.App.Publisher.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0 ||
                    ar.App.InstallLocation.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0;
                ar.Row.Visible = match;
                if (match) shown++;
            }
            _countLabel.Text = shown + " / " + _rows.Count + " 个程序";
            LayoutRows();
        }

        private void LayoutRows()
        {
            if (_list == null) return;
            int w = Math.Max(Theme.S(320), _list.ClientSize.Width - Theme.S(18));
            int y = Theme.S(4);
            foreach (AppRow ar in _rows)
            {
                if (!ar.Row.Visible) continue;
                ar.Row.SetBounds(Theme.S(8), y, w - Theme.S(8), ar.Row.Height);
                y += ar.Row.Height + 1;
            }
            _list.AutoScrollMinSize = new Size(0, y + Theme.S(6));
        }

        private void SelectApp(AppRow ar)
        {
            foreach (AppRow other in _rows)
            {
                other.Row.Selected = (other == ar);
                other.Row.Invalidate();
            }
            _selected = ar.App;
            _items = null;
            _matched = null;
            ShowDetail(ar.App);
            UpdateButtons();
        }

        private void ClearSelection()
        {
            foreach (AppRow ar in _rows) { ar.Row.Selected = false; ar.Row.Invalidate(); }
            _selected = null;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool has = _selected != null && !_busy;
            _btnUninstall.Enabled = has;
            _btnScan.Enabled = has;
            _scanTop.Enabled = has;
            _refresh.Enabled = !_busy;
            _btnUninstall.Text = _busy ? "执行中…" : "一键卸载";
        }

        // ------------------------------------------------------------ 详情

        private void ShowDetail(InstalledApp app)
        {
            _detail.Title = app.Name;
            _detail.Subtitle = (app.Publisher.Length > 0 ? app.Publisher : "未知发布者") +
                (app.Version.Length > 0 ? "  ·  " + app.Version : "");
            BuildDetail(app);
            _detail.ShowIn(this);
            try { _detail.Body.VerticalScroll.Value = 0; } catch { }
            _detail.Invalidate();
            UpdateButtons();
        }

        private Label AddLabel(string text, float size, Color color, bool bold, bool wrap)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.MaximumSize = new Size(Math.Max(Theme.S(180), _detail.Width - Theme.S(72)), 0);
            l.BackColor = Color.Transparent;
            l.ForeColor = color;
            l.Font = bold ? Theme.TextBold(size) : Theme.Text(size);
            l.Text = text;
            l.Location = new Point(Theme.S(28), _dy);
            _detail.Body.Controls.Add(l);
            _dy += l.PreferredHeight + Theme.S(4);
            return l;
        }

        private void Section(string title)
        {
            _dy += Theme.S(14);
            AddLabel(title, 8.5f, Theme.InkSoft, true, false);
            _dy += Theme.S(2);
        }

        private void KV(string key, string value)
        {
            AddLabel(key, 8.5f, Theme.InkSoft, false, false);
            AddLabel(value, 10f, Theme.Ink, false, true);
            _dy += Theme.S(12);
        }

        private void BuildDetail(InstalledApp app)
        {
            ScrollPanel body = _detail.Body;
            body.SuspendLayout();
            body.Controls.Clear();
            _dy = Theme.S(2);

            KV("发布者", app.Publisher.Length > 0 ? app.Publisher : "未知");
            KV("版本", app.Version.Length > 0 ? app.Version : "未知");
            KV("估计大小", app.EstimatedBytes >= 0 ? Ui.FormatSize(app.EstimatedBytes) : "注册表未记录");
            KV("安装日期", Ui.FormatDate(app.InstallDate));
            KV("安装位置", app.InstallLocation.Length > 0 ? app.InstallLocation : "注册表未记录（将按名称匹配扫描）");
            KV("卸载信息键", app.RegistryPath);

            Section("清理范围");
            if (_items == null)
            {
                AddLabel("尚未扫描。点击「一键卸载」将自动扫描并清理，或先执行「深度扫描」预览清单。",
                    9.5f, Theme.InkSoft, false, true);
                _dy += Theme.S(8);
            }
            else
            {
                int files = 0, dirs = 0, regs = 0, svc = 0, tasks = 0, others = 0;
                long bytes = 0;
                foreach (ResidueItem it in _items)
                {
                    if (it.Kind == "file" || it.Kind == "shortcut") { files++; bytes += (it.SizeBytes > 0 ? it.SizeBytes : 0); }
                    else if (it.Kind == "dir") dirs++;
                    else if (it.Kind == "regkey" || it.Kind == "regvalue" || it.Kind == "com" || it.Kind == "env") regs++;
                    else if (it.Kind == "service") svc++;
                    else if (it.Kind == "task") tasks++;
                    else others++;
                }
                AddLabel("残留项 " + _items.Count + " 个（" + Ui.FormatSize(bytes) + "）", 11f, Theme.Ink, true, true);
                AddLabel("文件 " + files + " · 目录 " + dirs + " · 注册表 " + regs + " · 服务 " + svc +
                    " · 计划任务 " + tasks + (others > 0 ? " · 其他 " + others : ""), 9.5f, Theme.InkSoft, false, true);
                int prot = 0;
                foreach (ResidueItem it in _items) if (it.Protected) prot++;
                if (prot > 0) AddLabel("其中 " + prot + " 项命中系统保护/白名单，将被跳过。", 9.5f, Theme.Orange, false, true);
                _dy += Theme.S(6);

                int i = 0;
                foreach (ResidueItem it in _items)
                {
                    if (i >= 9) break;
                    AddLabel("· " + it.KindText + "  " + Shorten(it.Target, 58), 8.5f, Theme.InkSoft, false, true);
                    i++;
                }
                if (_items.Count > 9) AddLabel("… 其余 " + (_items.Count - 9) + " 项在清理报告中完整列出", 8.5f, Theme.InkFaint, false, true);
            }

            Section("关联内核驱动");
            if (_matched == null)
            {
                AddLabel("尚未扫描。系统原生、WHQL 与其他软件驱动默认锁定。", 9.5f, Theme.InkSoft, false, true);
            }
            else if (_matched.Count == 0)
            {
                AddLabel("未发现明确归属的可清理驱动。", 10f, Theme.Green, true, true);
            }
            else
            {
                AddLabel(_matched.Count + " 个驱动与服务将与软件一并卸载", 10f, Theme.Red, true, true);
                foreach (DriverInfo d in _matched)
                {
                    AddLabel("· " + d.ServiceName + "  ·  " + d.SignatureText + "  ·  归属 " + d.Attribution + "%",
                        8.5f, Theme.InkSoft, false, true);
                }
            }
            _dy += Theme.S(20);

            body.ResumeLayout();
            _detail.Invalidate();
        }

        private string Shorten(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        // ------------------------------------------------------------ 扫描与清理

        private Overlay NewBusy(string title, string sub)
        {
            Overlay ov = new Overlay();
            ov.Title = title;
            ov.Subtitle = sub;
            ov.Busy = true;
            ov.SecondaryText = "";
            ov.Items = new List<string>();
            ov.Attach(this);
            return ov;
        }

        private void CloseOverlay(Overlay ov)
        {
            if (ov == null) return;
            try { if (!ov.IsDisposed) { Controls.Remove(ov); ov.Dispose(); } } catch { }
        }

        private void AppendLog(Overlay ov, string msg)
        {
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    if (ov == null || ov.IsDisposed) return;
                    ov.Items.Add(msg.Length > 110 ? msg.Substring(0, 110) + "…" : msg);
                    while (ov.Items.Count > 40) ov.Items.RemoveAt(0);
                    ov.Invalidate();
                });
            }
            catch { }
        }

        private void BeginUninstall()
        {
            if (_selected == null) { ShowMessage("请先选择软件", "在左侧列表中选中一个软件，或在搜索框中定位目标。"); return; }
            if (_busy) return;
            StartScan(_selected, true);
        }

        private void StartScan(InstalledApp app, bool autoConfirm)
        {
            if (_busy) return;
            _busy = true;
            UpdateButtons();

            Overlay busy = NewBusy("正在生成清理清单", app.Name + "\n扫描文件、注册表、服务、计划任务、快捷方式、COM 与内核驱动…");

            Thread t = new Thread(delegate ()
            {
                List<ResidueItem> items = null;
                List<DriverInfo> drivers = null;
                string error = "";
                try
                {
                    items = ResidueScanner.Scan(app, delegate (string s) { AppendLog(busy, s); });
                    drivers = MatchDrivers(app, delegate (string s) { AppendLog(busy, s); });
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    AppendLog(busy, "错误：" + ex.Message);
                }

                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        CloseOverlay(busy);
                        _busy = false;
                        UpdateButtons();
                        if (items == null)
                        {
                            ShowMessage("扫描失败", error);
                            return;
                        }
                        _items = items;
                        _matched = drivers;
                        BuildDetail(app);
                        if (autoConfirm) ShowCleanupConfirm(app, items, drivers);
                        else ShowScanResult(app, items, drivers);
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Name = "deep-scan";
            t.Start();
        }

        private List<DriverInfo> MatchDrivers(InstalledApp app, Action<string> log)
        {
            List<DriverInfo> source = Main.Drivers;
            if (source == null)
            {
                log("驱动清单尚未就绪，正在枚举内核驱动…");
                source = DriverInventory.LoadAll(delegate (string s) { log(s); });
                Main.Drivers = source;
            }

            List<DriverInfo> matched = new List<DriverInfo>();
            foreach (DriverInfo d in source)
            {
                if (d.IsProtected) continue;
                int score = DriverInventory.Attribute(d, app, null);
                if (score >= 45)
                {
                    d.Attribution = score;
                    d.OwnerSoftware = app.Name;
                    matched.Add(d);
                }
            }
            if (matched.Count > 0)
                log("匹配到 " + matched.Count + " 个明确归属该软件的内核驱动。");
            else
                log("未匹配到可清理的内核驱动（系统原生驱动保持锁定）。");
            return matched;
        }

        private void ShowCleanupConfirm(InstalledApp app, List<ResidueItem> items, List<DriverInfo> drivers)
        {
            int files = 0, regs = 0;
            foreach (ResidueItem it in items)
            {
                if (it.Kind == "file") files++;
                else if (it.Kind == "regkey" || it.Kind == "regvalue") regs++;
            }

            List<string> lines = new List<string>();
            lines.Add("!将终止该软件全部关联进程，并执行其卸载程序");
            lines.Add("清理残留项 " + items.Count + " 个（文件 " + files + " · 注册表 " + regs + "）");
            lines.Add("卸载内核驱动与关联服务 " + drivers.Count + " 个");
            foreach (DriverInfo d in drivers)
            {
                if (lines.Count > 12) break;
                lines.Add("!" + d.ServiceName + "  ·  " + d.SignatureText + "  ·  归属 " + d.Attribution + "%");
            }
            lines.Add("~系统原生 / WHQL / 其他软件驱动已锁定，不会出现在删除列表");
            lines.Add("删除前自动备份，保留 " + Config.BackupDays + " 天；可随时撤销");
            if (Config.CreateRestorePoint) lines.Add("将尝试创建系统还原点（需系统保护开启）");

            Overlay ov = new Overlay();
            ov.Title = "确认清理清单";
            ov.Subtitle = app.Name + " · 以上为将要删除的全部内容";
            ov.Items = lines;
            ov.PrimaryText = "开始卸载";
            ov.SecondaryText = "取消";
            ov.TwoStep = drivers.Count > 0;
            ov.Primary += delegate
            {
                CloseOverlay(ov);
                StartPipeline(app, items, drivers, Config.RunUninstallerFirst);
            };
            ov.Secondary += delegate { CloseOverlay(ov); };
            ov.Attach(this);
        }

        private void ShowScanResult(InstalledApp app, List<ResidueItem> items, List<DriverInfo> drivers)
        {
            List<string> lines = new List<string>();
            lines.Add("残留项 " + items.Count + " 个，可清理驱动 " + drivers.Count + " 个");
            int shown = 0;
            foreach (ResidueItem it in items)
            {
                if (shown >= 8) break;
                lines.Add("· " + it.KindText + "  " + Shorten(it.Target, 82));
                shown++;
            }
            foreach (DriverInfo d in drivers)
            {
                lines.Add("!" + d.ServiceName + "  ·  " + d.SignatureText + "  ·  归属 " + d.Attribution + "%");
            }

            Overlay ov = new Overlay();
            ov.Title = "扫描完成";
            ov.Subtitle = app.Name + " · 预览模式，尚未删除任何内容";
            ov.Items = lines;
            ov.PrimaryText = "一键卸载并清理";
            ov.SecondaryText = "仅清理残留";
            ov.TwoStep = drivers.Count > 0;
            ov.Primary += delegate
            {
                CloseOverlay(ov);
                StartPipeline(app, items, drivers, Config.RunUninstallerFirst);
            };
            ov.Secondary += delegate
            {
                CloseOverlay(ov);
                ConfirmResidueOnly(app, items, drivers);
            };
            ov.Attach(this);
        }

        private void ConfirmResidueOnly(InstalledApp app, List<ResidueItem> items, List<DriverInfo> drivers)
        {
            Overlay ov = new Overlay();
            ov.Title = "仅清理残留";
            ov.Subtitle = "不执行软件自带卸载程序，只删除已扫描的残留项与归属驱动";
            List<string> lines = new List<string>();
            lines.Add("残留项 " + items.Count + " 个");
            lines.Add("归属驱动 " + drivers.Count + " 个");
            lines.Add("~系统原生 / WHQL 驱动保持锁定");
            ov.Items = lines;
            ov.PrimaryText = "开始清理";
            ov.SecondaryText = "取消";
            ov.TwoStep = drivers.Count > 0;
            ov.Danger = true;
            ov.Primary += delegate
            {
                CloseOverlay(ov);
                StartPipeline(app, items, drivers, false);
            };
            ov.Secondary += delegate { CloseOverlay(ov); };
            ov.Attach(this);
        }

        private void StartPipeline(InstalledApp app, List<ResidueItem> items, List<DriverInfo> drivers, bool runUninstaller)
        {
            _busy = true;
            UpdateButtons();
            Overlay busy = NewBusy("正在一键清理", app.Name + " · 请勿关闭窗口");
            if (runUninstaller) busy.Items.Add("流程：终止进程 → 卸载主程序 → 备份 → 清理残留 → 清理驱动 → 报告");
            else busy.Items.Add("流程：备份 → 清理残留与驱动 → 报告（跳过主程序卸载）");

            Thread t = new Thread(delegate ()
            {
                CleanReport report = null;
                string error = "";
                try
                {
                    report = UninstallPipeline.Run(app, items, drivers, runUninstaller,
                        delegate (string m) { AppendLog(busy, m); });
                }
                catch (Exception ex) { error = ex.Message; AppendLog(busy, "错误：" + ex.Message); }

                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        CloseOverlay(busy);
                        _busy = false;
                        UpdateButtons();
                        if (report == null) { ShowMessage("清理失败", error); return; }
                        BuildList();
                        ShowResult(report);
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Name = "cleanup";
            t.Start();
        }

        private void ShowResult(CleanReport report)
        {
            List<string> lines = new List<string>();
            lines.Add("删除文件 " + report.DeletedFiles + " · 注册表 " + report.DeletedRegistry +
                " · 服务 " + report.DeletedServices + " · 计划任务 " + report.DeletedTasks);
            lines.Add("待重启删除 " + report.PendingRestart + " 项 · 失败 " + report.Failed + " 项");
            if (report.BackupPath.Length > 0) lines.Add("备份：" + report.BackupPath);
            if (report.RestorePoint.Length > 0) lines.Add("还原点：" + report.RestorePoint);
            lines.Add("报告：" + report.ReportPath);

            Overlay ov = new Overlay();
            ov.Title = report.Success ? "清理完成" : "清理结束（存在失败项）";
            ov.Subtitle = report.AppName + " · " + report.Finished.ToString("HH:mm:ss");
            ov.Items = lines;
            ov.PrimaryText = "打开报告";
            ov.SecondaryText = "关闭";
            ov.Primary += delegate
            {
                CloseOverlay(ov);
                MainForm.OpenPath(report.ReportPath);
            };
            ov.Secondary += delegate { CloseOverlay(ov); };
            ov.Attach(this);
        }

        private void ShowMessage(string title, string message)
        {
            Overlay ov = new Overlay();
            ov.Title = title;
            ov.Subtitle = message;
            ov.Items = new List<string>();
            ov.PrimaryText = "关闭";
            ov.SecondaryText = "";
            ov.Primary += delegate { CloseOverlay(ov); };
            ov.Attach(this);
        }

        private void RefreshInventory()
        {
            if (_busy) return;
            _busy = true;
            UpdateButtons();
            Overlay busy = NewBusy("正在刷新", "重新读取本机已安装程序清单…");
            Thread t = new Thread(delegate ()
            {
                List<InstalledApp> apps = SoftwareInventory.Load(delegate (string s) { AppendLog(busy, s); });
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        Main.Apps = apps;
                        CloseOverlay(busy);
                        _busy = false;
                        BuildList();
                        UpdateButtons();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}






