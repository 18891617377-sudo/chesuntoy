using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MetroUninstaller
{
    /// <summary>驱动管理页：清单、签名状态、四维归属、二次确认后卸载。</summary>
    public class DriverPage : MetroPage
    {
        private class DriverRow
        {
            public DriverInfo Info;
            public MetroRow Row;
        }

        private MetroButton _filterAll, _filterSusp, _filterOwned, _refresh, _cleanAll;
        private ScrollPanel _list;
        private Label _count, _empty;
        private SidePanel _detail;
        private MetroButton _btnDel;
        private List<DriverRow> _rows = new List<DriverRow>();
        private DriverInfo _selected;
        private string _filter = "all";
        private bool _busy;
        private int _dy;

        public DriverPage(MainForm main) : base(main)
        {
            PageTitle = "驱动管理";
            PageSubtitle = "四维归属：安装路径 / 数字签名 / 服务名 / 注册表链；系统原生驱动锁定保护";

            _filterAll = MakeFilter("全部驱动", delegate { SetFilter("all"); });
            _filterSusp = MakeFilter("可疑无签名", delegate { SetFilter("susp"); });
            _filterOwned = MakeFilter("可归属", delegate { SetFilter("owned"); });

            _refresh = new MetroButton();
            _refresh.Text = "重新校验";
            _refresh.FontSize = 9.5f;
            _refresh.Click += delegate { ReloadDrivers(); };
            Controls.Add(_refresh);

            _cleanAll = new MetroButton();
            _cleanAll.Text = "清理可归属驱动";
            _cleanAll.Danger = true;
            _cleanAll.FontSize = 9.5f;
            _cleanAll.Click += delegate { CleanAllDeletable(); };
            Controls.Add(_cleanAll);

            _count = new Label();
            _count.AutoSize = false;
            _count.BackColor = Theme.Bg;
            _count.ForeColor = Theme.InkSoft;
            _count.Font = Theme.Text(9f);
            _count.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_count);

            _list = new ScrollPanel();
            _list.Resize += delegate { LayoutRows(); };
            Controls.Add(_list);

            _empty = new Label();
            _empty.AutoSize = false;
            _empty.BackColor = Theme.Bg;
            _empty.ForeColor = Theme.InkSoft;
            _empty.Font = Theme.Display(18f);
            _empty.TextAlign = ContentAlignment.MiddleCenter;
            _empty.Text = "正在校验驱动签名…";
            Controls.Add(_empty);

            _detail = new SidePanel(440);
            _detail.CloseRequested += delegate { _detail.HideSlide(); ClearSelection(); };
            Controls.Add(_detail);

            _btnDel = new MetroButton();
            _btnDel.Text = "卸载该驱动";
            _btnDel.Danger = true;
            _btnDel.FontSize = 11f;
            _btnDel.Click += delegate { DeleteSelected(); };
            _detail.Footer.Controls.Add(_btnDel);

            Main.DataReady += delegate
            {
                if (!IsDisposed) BeginInvoke((MethodInvoker)delegate { BuildList(); });
            };

            if (Main.Drivers != null) BuildList();
            else LoadDrivers();
        }

        private MetroButton MakeFilter(string text, EventHandler onClick)
        {
            MetroButton b = new MetroButton();
            b.Text = text;
            b.FontSize = 9.5f;
            b.Accent = false;
            b.Click += onClick;
            Controls.Add(b);
            return b;
        }

        private void SetFilter(string f)
        {
            _filter = f;
            _filterAll.Accent = (f == "all");
            _filterSusp.Accent = (f == "susp");
            _filterOwned.Accent = (f == "owned");
            _filterAll.Invalidate();
            _filterSusp.Invalidate();
            _filterOwned.Invalidate();
            BuildList();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_filterAll == null || _detail == null || _list == null || _count == null || _empty == null) return;
            int pad = Theme.S(24);
            int y = Theme.S(8);
            int h = Theme.S(42);
            int x = pad;
            _filterAll.SetBounds(x, y, Theme.S(112), h); x += Theme.S(122);
            _filterSusp.SetBounds(x, y, Theme.S(118), h); x += Theme.S(128);
            _filterOwned.SetBounds(x, y, Theme.S(96), h); x += Theme.S(106);
            _refresh.SetBounds(x, y, Theme.S(108), h);
            _cleanAll.SetBounds(Width - pad - Theme.S(184), y, Theme.S(184), h);
            _count.SetBounds(_cleanAll.Left - Theme.S(250), y, Theme.S(240), h);

            _list.SetBounds(0, Theme.S(62), Width, Math.Max(0, Height - Theme.S(68)));
            _empty.SetBounds(0, Theme.S(140), Width, Theme.S(120));
            _detail.SetBounds(_detail.Left, 0, _detail.Width, Height);
            if (!_detail.Visible) _detail.Left = Width;
            LayoutFooter();
            LayoutRows();
        }

        private void LayoutFooter()
        {
            if (_btnDel == null) return;
            int fw = _detail.Footer.Width;
            _btnDel.Size = new Size(Theme.S(210), Theme.S(58));
            _btnDel.Location = new Point(fw - Theme.S(24) - _btnDel.Width, Theme.S(24));
        }

        private bool Match(DriverInfo d)
        {
            if (_filter == "susp") return d.Suspicious;
            if (_filter == "owned") return d.Attribution >= 45 && !d.IsProtected;
            return true;
        }

        private void BuildList()
        {
            if (Main.Drivers == null) return;
            _list.SuspendLayout();
            _list.Controls.Clear();
            _rows.Clear();
            _selected = null;

            foreach (DriverInfo d in Main.Drivers)
            {
                if (!Match(d)) continue;
                DriverRow dr = new DriverRow();
                dr.Info = d;
                dr.Row = new MetroRow();
                dr.Row.Title = d.DisplayName + "  ·  " + d.ServiceName;
                dr.Row.Subtitle = (d.ResolvedPath.Length > 0 ? d.ResolvedPath : d.ImagePathRaw) +
                    "  ·  " + d.StartText + "  ·  " + d.State;
                dr.Row.RightText = d.SignatureText;
                dr.Row.IconColor = d.IsProtected ? Theme.Gray : (d.Suspicious ? Theme.Red : Theme.Slate);
                dr.Row.Initial = "D";
                dr.Row.Height = Theme.S(72);
                dr.Row.Danger = d.Suspicious && !d.IsProtected;

                if (d.IsProtected) { dr.Row.Chip = "受保护"; dr.Row.ChipColor = Theme.Gray; }
                else if (d.Suspicious) { dr.Row.Chip = "无签名"; dr.Row.ChipColor = Theme.Red; }
                else if (d.Attribution >= 45) { dr.Row.Chip = "归属 " + d.Attribution + "%"; dr.Row.ChipColor = Theme.Orange; }
                else if (!string.IsNullOrEmpty(d.Signer)) { dr.Row.Chip = "已签名"; dr.Row.ChipColor = Theme.Green; }

                dr.Row.Data = d;
                dr.Row.Click += delegate (object s, EventArgs e) { SelectDriver(dr); };
                _list.Controls.Add(dr.Row);
                _rows.Add(dr);
            }
            _list.ResumeLayout();

            int susp = 0, del = 0;
            foreach (DriverInfo d in Main.Drivers)
            {
                if (d.Suspicious) susp++;
                if (DriverInventory.IsDeletable(d)) del++;
            }
            _count.Text = "可疑 " + susp + " 个 · 可清理 " + del + " 个";
            _empty.Visible = _rows.Count == 0;
            if (_rows.Count == 0) _empty.Text = _filter == "all" ? "未读取到内核驱动" : "当前筛选下没有驱动";
            LayoutRows();
        }

        private void LayoutRows()
        {
            if (_list == null) return;
            int w = Math.Max(Theme.S(320), _list.ClientSize.Width - Theme.S(18));
            int y = Theme.S(4);
            foreach (DriverRow dr in _rows)
            {
                dr.Row.SetBounds(Theme.S(8), y, w - Theme.S(8), dr.Row.Height);
                y += dr.Row.Height + 1;
            }
            _list.AutoScrollMinSize = new Size(0, y + Theme.S(6));
        }

        private void SelectDriver(DriverRow dr)
        {
            foreach (DriverRow other in _rows)
            {
                other.Row.Selected = (other == dr);
                other.Row.Invalidate();
            }
            _selected = dr.Info;
            BuildDetail(dr.Info);
            _detail.Title = dr.Info.DisplayName;
            _detail.Subtitle = dr.Info.ServiceName + "  ·  " + dr.Info.TypeText;
            _detail.ShowIn(this);
            try { _detail.Body.VerticalScroll.Value = 0; } catch { }
            _detail.Invalidate();
            _btnDel.Enabled = DriverInventory.IsDeletable(dr.Info);
        }

        private void ClearSelection()
        {
            foreach (DriverRow dr in _rows) { dr.Row.Selected = false; dr.Row.Invalidate(); }
            _selected = null;
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

        private void KV(string key, string value)
        {
            AddLabel(key, 8.5f, Theme.InkSoft, false, false);
            AddLabel(value, 9.5f, Theme.Ink, false, true);
            _dy += Theme.S(10);
        }

        private void BuildDetail(DriverInfo d)
        {
            ScrollPanel body = _detail.Body;
            body.SuspendLayout();
            body.Controls.Clear();
            _dy = Theme.S(2);

            if (d.IsProtected)
            {
                AddLabel("已锁定保护 · 系统原生 / WHQL / 白名单驱动", 10f, Theme.Green, true, true);
                _dy += Theme.S(8);
            }
            else if (d.Suspicious)
            {
                AddLabel("可疑：无有效数字签名，且不在系统受保护目录", 10f, Theme.Red, true, true);
                _dy += Theme.S(8);
            }

            KV("服务名", d.ServiceName);
            KV("显示名", d.DisplayName);
            KV("驱动文件", d.ResolvedPath.Length > 0 ? d.ResolvedPath : d.ImagePathRaw);
            KV("加载级别", d.StartText + "（Start=" + d.StartType + "）");
            KV("运行状态", d.State);
            KV("类型", d.TypeText);
            KV("签名状态", d.SignatureText);
            KV("签名主体", d.Signer.Length > 0 ? d.Signer : "无（或未嵌入签名）");
            KV("归属软件", d.OwnerSoftware.Length > 0 ? d.OwnerSoftware : "未匹配到已安装软件");
            KV("归属得分", d.Attribution + " / 100");

            _dy += Theme.S(8);
            AddLabel("归属证据", 8.5f, Theme.InkSoft, true, false);
            if (d.Evidence == null || d.Evidence.Count == 0)
                AddLabel("· 无直接证据，默认不进入清理列表", 9f, Theme.InkFaint, false, true);
            else
                foreach (string ev in d.Evidence) AddLabel("· " + ev, 9f, Theme.InkSoft, false, true);

            _dy += Theme.S(16);
            if (d.IsProtected)
                AddLabel("按安全红线，此驱动不会出现在待删除列表；如需放行，请在白名单页显式登记并自行承担风险。",
                    8.5f, Theme.InkSoft, false, true);
            else if (!DriverInventory.IsDeletable(d))
                AddLabel("归属不明确（低于 45 分），默认不提供删除操作，避免误删其他软件驱动。",
                    8.5f, Theme.Orange, false, true);

            body.ResumeLayout();
            _detail.Invalidate();
        }

        private void DeleteSelected()
        {
            if (_selected == null || _busy) return;
            DriverInfo d = _selected;
            List<string> lines = new List<string>();
            lines.Add("!驱动服务：" + d.ServiceName);
            lines.Add("文件：" + d.ResolvedPath);
            lines.Add("签名：" + d.Resolution());
            lines.Add("归属：" + (d.OwnerSoftware.Length > 0 ? d.OwnerSoftware : "未匹配") + "（" + d.Attribution + "%）");
            lines.Add("~删除前会备份 .sys 文件与 Services 注册表项；文件占用时登记重启删除");

            Overlay ov = new Overlay();
            ov.Title = "卸载内核驱动";
            ov.Subtitle = "该操作会停止并删除驱动服务，然后删除驱动文件";
            ov.Items = lines;
            ov.PrimaryText = "卸载驱动";
            ov.SecondaryText = "取消";
            ov.Danger = true;
            ov.TwoStep = true;
            ov.Primary += delegate
            {
                CloseOverlay(ov);
                RunClean(new List<DriverInfo> { d });
            };
            ov.Secondary += delegate { CloseOverlay(ov); };
            ov.Attach(this);
        }

        private void CleanAllDeletable()
        {
            if (Main.Drivers == null || _busy) return;
            List<DriverInfo> list = new List<DriverInfo>();
            foreach (DriverInfo d in Main.Drivers) if (DriverInventory.IsDeletable(d)) list.Add(d);
            if (list.Count == 0)
            {
                ShowMessage("没有可清理的驱动", "当前没有明确归属且不受保护的驱动。系统原生与 WHQL 驱动始终锁定。");
                return;
            }

            List<string> lines = new List<string>();
            lines.Add("!" + list.Count + " 个明确归属的驱动将被停止、删除服务并删除文件");
            foreach (DriverInfo d in list)
            {
                if (lines.Count > 12) break;
                lines.Add(d.ServiceName + "  ·  " + (d.OwnerSoftware.Length > 0 ? d.OwnerSoftware : "归属 " + d.Attribution + "%"));
            }
            lines.Add("~系统原生 / WHQL 驱动不在列表中");

            Overlay ov = new Overlay();
            ov.Title = "批量清理驱动";
            ov.Subtitle = "仅处理明确归属其他软件的内核驱动";
            ov.Items = lines;
            ov.PrimaryText = "开始清理";
            ov.SecondaryText = "取消";
            ov.Danger = true;
            ov.TwoStep = true;
            ov.Primary += delegate
            {
                CloseOverlay(ov);
                RunClean(list);
            };
            ov.Secondary += delegate { CloseOverlay(ov); };
            ov.Attach(this);
        }

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
            try { if (ov != null && !ov.IsDisposed) { Controls.Remove(ov); ov.Dispose(); } } catch { }
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

        private void RunClean(List<DriverInfo> drivers)
        {
            _busy = true;
            Overlay busy = NewBusy("正在卸载驱动", drivers.Count + " 个驱动 · 请勿关闭窗口");
            Thread t = new Thread(delegate ()
            {
                CleanReport report = null;
                List<DriverInfo> refreshed = null;
                try
                {
                    report = UninstallPipeline.CleanDrivers(drivers, delegate (string m) { AppendLog(busy, m); });
                    refreshed = DriverInventory.LoadAll(null);
                    if (Main.Apps != null && refreshed != null) DriverInventory.AttributeAll(refreshed, Main.Apps);
                }
                catch (Exception ex) { AppendLog(busy, "错误：" + ex.Message); }

                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        CloseOverlay(busy);
                        _busy = false;
                        if (refreshed != null) Main.Drivers = refreshed;
                        BuildList();
                        if (report != null)
                        {
                            Overlay ov = new Overlay();
                            ov.Title = report.Success ? "驱动清理完成" : "驱动清理结束（存在失败项）";
                            ov.Subtitle = "服务 " + report.DeletedServices + " 个 · 待重启 " + report.PendingRestart + " 项";
                            ov.Items = new List<string> { "报告：" + report.ReportPath };
                            ov.PrimaryText = "打开报告";
                            ov.SecondaryText = "关闭";
                            ov.Primary += delegate { CloseOverlay(ov); MainForm.OpenPath(report.ReportPath); };
                            ov.Secondary += delegate { CloseOverlay(ov); };
                            ov.Attach(this);
                        }
                        else ShowMessage("驱动清理失败", "未获得执行结果，请查看日志目录。");
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ReloadDrivers()
        {
            if (_busy) return;
            _busy = true;
            Overlay busy = NewBusy("正在重新校验", "枚举内核驱动并校验数字签名…");
            Thread t = new Thread(delegate ()
            {
                List<DriverInfo> ds = DriverInventory.LoadAll(delegate (string s) { AppendLog(busy, s); });
                if (Main.Apps != null) DriverInventory.AttributeAll(ds, Main.Apps);
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        Main.Drivers = ds;
                        CloseOverlay(busy);
                        _busy = false;
                        BuildList();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void LoadDrivers()
        {
            Thread t = new Thread(delegate ()
            {
                List<DriverInfo> ds = DriverInventory.LoadAll(null);
                if (Main.Apps != null) DriverInventory.AttributeAll(ds, Main.Apps);
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (Main.Drivers == null) Main.Drivers = ds;
                        BuildList();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
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
    }
}





