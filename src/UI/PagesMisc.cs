using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MetroUninstaller
{
    /// <summary>扫描历史：清理报告归档。</summary>
    public class HistoryPage : MetroPage
    {
        private ScrollPanel _list;
        private Label _empty, _count;
        private MetroButton _openDir, _refresh;

        public HistoryPage(MainForm main) : base(main)
        {
            PageTitle = "扫描历史";
            PageSubtitle = "每次清理都会生成 HTML 报告，归档在本机数据目录";

            _openDir = new MetroButton();
            _openDir.Text = "打开报告目录";
            _openDir.FontSize = 9.5f;
            _openDir.Click += delegate { MainForm.OpenPath(AppPaths.Reports); };
            Controls.Add(_openDir);

            _refresh = new MetroButton();
            _refresh.Text = "刷新";
            _refresh.FontSize = 9.5f;
            _refresh.Click += delegate { BuildList(); };
            Controls.Add(_refresh);

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
            _empty.Text = "还没有清理记录。完成一次一键卸载后，报告会出现在这里。";
            Controls.Add(_empty);

            BuildList();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_list == null || _empty == null || _count == null) return;
            int pad = Theme.S(24);
            _openDir.SetBounds(pad, Theme.S(8), Theme.S(160), Theme.S(42));
            _refresh.SetBounds(pad + Theme.S(170), Theme.S(8), Theme.S(96), Theme.S(42));
            _count.SetBounds(Width - pad - Theme.S(260), Theme.S(8), Theme.S(260), Theme.S(42));
            _list.SetBounds(0, Theme.S(62), Width, Math.Max(0, Height - Theme.S(68)));
            _empty.SetBounds(0, Theme.S(140), Width, Theme.S(120));
            LayoutRows();
        }

        private void BuildList()
        {
            _list.SuspendLayout();
            _list.Controls.Clear();
            List<HistoryEntry> hist = HistoryStore.List();
            foreach (HistoryEntry h in hist)
            {
                MetroRow row = new MetroRow();
                row.Title = h.App.Length > 0 ? h.App : "未知目标";
                row.Subtitle = h.Time.ToString("yyyy-MM-dd HH:mm") + "  ·  删除 " + h.Deleted + " 项  ·  驱动 " + h.Drivers +
                    "  ·  待重启 " + h.Pending;
                row.RightText = h.Result == "OK" ? "已完成" : "有失败";
                row.IconColor = h.Result == "OK" ? Theme.Green : Theme.Orange;
                row.Height = Theme.S(76);
                HistoryEntry cap = h;
                row.Click += delegate
                {
                    if (!string.IsNullOrEmpty(cap.ReportPath) && File.Exists(cap.ReportPath)) MainForm.OpenPath(cap.ReportPath);
                    else MainForm.OpenPath(AppPaths.Reports);
                };
                _list.Controls.Add(row);
            }
            _list.ResumeLayout();
            _count.Text = hist.Count + " 份报告";
            _empty.Visible = hist.Count == 0;
            LayoutRows();
        }

        private void LayoutRows()
        {
            if (_list == null) return;
            int w = Math.Max(Theme.S(320), _list.ClientSize.Width - Theme.S(18));
            int y = Theme.S(4);
            foreach (Control c in _list.Controls)
            {
                if (c is MetroRow)
                {
                    c.SetBounds(Theme.S(8), y, w - Theme.S(8), c.Height);
                    y += c.Height + 1;
                }
            }
            _list.AutoScrollMinSize = new Size(0, y + Theme.S(6));
        }
    }

    /// <summary>系统状态：权限、还原点、备份、待重启项。</summary>
    public class StatusPage : MetroPage
    {
        private ScrollPanel _list;
        private MetroButton _restore, _purge, _open, _logs;

        public StatusPage(MainForm main) : base(main)
        {
            PageTitle = "系统状态";
            PageSubtitle = "还原点、备份占用、待重启删除项与白名单总览";

            _restore = new MetroButton();
            _restore.Text = "创建系统还原点";
            _restore.Accent = true;
            _restore.FontSize = 9.5f;
            _restore.Click += delegate { CreateRestore(); };
            Controls.Add(_restore);

            _purge = new MetroButton();
            _purge.Text = "清理过期备份";
            _purge.FontSize = 9.5f;
            _purge.Click += delegate { PurgeBackups(); };
            Controls.Add(_purge);

            _open = new MetroButton();
            _open.Text = "数据目录";
            _open.FontSize = 9.5f;
            _open.Click += delegate { MainForm.OpenPath(AppPaths.Root); };
            Controls.Add(_open);

            _logs = new MetroButton();
            _logs.Text = "运行日志";
            _logs.FontSize = 9.5f;
            _logs.Click += delegate { MainForm.OpenPath(AppPaths.Logs); };
            Controls.Add(_logs);

            _list = new ScrollPanel();
            _list.Resize += delegate { LayoutRows(); };
            Controls.Add(_list);

            BuildList();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_list == null) return;
            int pad = Theme.S(24);
            _restore.SetBounds(pad, Theme.S(8), Theme.S(180), Theme.S(42));
            _purge.SetBounds(pad + Theme.S(190), Theme.S(8), Theme.S(150), Theme.S(42));
            _open.SetBounds(pad + Theme.S(350), Theme.S(8), Theme.S(110), Theme.S(42));
            _logs.SetBounds(pad + Theme.S(470), Theme.S(8), Theme.S(110), Theme.S(42));
            _list.SetBounds(0, Theme.S(62), Width, Math.Max(0, Height - Theme.S(68)));
            LayoutRows();
        }

        private void BuildList()
        {
            _list.SuspendLayout();
            _list.Controls.Clear();

            string os = "";
            try
            {
                os = Convert.ToString(Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", ""));
                string build = Convert.ToString(Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild", ""));
                if (build.Length > 0) os += "  Build " + build;
            }
            catch { }
            if (os.Length == 0) os = Environment.OSVersion.VersionString;

            Add("操作系统", os, Theme.Slate, GlyphKind.Status);
            Add("当前权限", Admin.IsAdmin ? "管理员（可清理驱动 / 创建还原点）" : "标准权限（建议以管理员重启）",
                Admin.IsAdmin ? Theme.Green : Theme.Orange, GlyphKind.Shield);
            Add("已安装程序", Main.Apps == null ? "读取中…" : Main.Apps.Count + " 个", Theme.Accent, GlyphKind.Uninstall);

            string drv = "读取中…";
            if (Main.Drivers != null)
            {
                int susp = 0, del = 0;
                foreach (DriverInfo d in Main.Drivers) { if (d.Suspicious) susp++; if (DriverInventory.IsDeletable(d)) del++; }
                drv = Main.Drivers.Count + " 个内核驱动 · 可疑 " + susp + " · 可归属 " + del;
            }
            Add("内核驱动", drv, Theme.Red, GlyphKind.Driver);

            Add("系统还原点", RestorePointService.IsEnabled() ? "系统保护可用，卸载前自动创建" : "系统保护未开启或 VSS 不可用",
                RestorePointService.IsEnabled() ? Theme.Green : Theme.Orange, GlyphKind.Restore);

            List<BackupSession> backups = BackupService.List();
            Add("备份", backups.Count + " 份 · " + Ui.FormatSize(BackupService.TotalBytes()) + " · 保留 " + Config.BackupDays + " 天",
                Theme.Teal, GlyphKind.Report);
            Add("待重启删除", PendingDelete.CountPending() + " 项", Theme.Orange, GlyphKind.Trash);
            Add("白名单", SafetyGuard.UserWhitelist.Count + " 条自定义 · 内置系统驱动黑名单已启用", Theme.Gray, GlyphKind.Whitelist);
            Add("数据目录", AppPaths.Root, Theme.Gray, GlyphKind.Report);

            _list.ResumeLayout();
            LayoutRows();
        }

        private void Add(string key, string value, Color color, GlyphKind kind)
        {
            MetroRow row = new MetroRow();
            row.Title = key;
            row.Subtitle = value;
            row.IconColor = color;
            row.Initial = "·";
            row.Height = Theme.S(78);
            row.Cursor = Cursors.Default;
            _list.Controls.Add(row);
        }

        private void LayoutRows()
        {
            if (_list == null) return;
            int w = Math.Max(Theme.S(320), _list.ClientSize.Width - Theme.S(18));
            int y = Theme.S(4);
            foreach (Control c in _list.Controls)
            {
                if (c is MetroRow)
                {
                    c.SetBounds(Theme.S(8), y, w - Theme.S(8), c.Height);
                    y += c.Height + 1;
                }
            }
            _list.AutoScrollMinSize = new Size(0, y + Theme.S(6));
        }

        private void CreateRestore()
        {
            if (!Admin.IsAdmin)
            {
                Message("需要管理员权限", "创建系统还原点需要以管理员身份运行。可点击顶栏「以管理员身份重启」。");
                return;
            }
            Overlay busy = new Overlay();
            busy.Title = "正在创建还原点";
            busy.Subtitle = "请稍候，系统还原点创建通常需要几秒到数十秒…";
            busy.Busy = true;
            busy.Items = new List<string>();
            busy.Attach(this);

            Thread t = new Thread(delegate ()
            {
                string err = RestorePointService.Create("CHESUN Uninstaller: 手动创建");
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        try { Controls.Remove(busy); busy.Dispose(); } catch { }
                        BuildList();
                        if (err.Length == 0) Message("还原点创建成功", "已创建系统还原点，可在系统还原界面看到。");
                        else Message("还原点创建失败", err);
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void PurgeBackups()
        {
            int removed = BackupService.Purge(Config.BackupDays);
            BuildList();
            Message("清理完成", "已删除 " + removed + " 份超过 " + Config.BackupDays + " 天的备份。");
        }

        private void Message(string title, string body)
        {
            Overlay ov = new Overlay();
            ov.Title = title;
            ov.Subtitle = body;
            ov.Items = new List<string>();
            ov.PrimaryText = "关闭";
            ov.SecondaryText = "";
            ov.Primary += delegate { try { Controls.Remove(ov); ov.Dispose(); } catch { } };
            ov.Attach(this);
        }
    }

    /// <summary>设置：还原点、备份策略、扫描深度。</summary>
    public class SettingsPage : MetroPage
    {

        private ScrollPanel _list;
        private Label _path;

        public SettingsPage(MainForm main) : base(main)
        {
            PageTitle = "设置";
            PageSubtitle = "所有删除动作都受备份与白名单约束；点击条目即可切换";

            _list = new ScrollPanel();
            _list.Resize += delegate { LayoutRows(); };
            Controls.Add(_list);

            _path = new Label();
            _path.AutoSize = false;
            _path.BackColor = Theme.Bg;
            _path.ForeColor = Theme.InkFaint;
            _path.Font = Theme.Text(8.5f);
            _path.TextAlign = ContentAlignment.MiddleLeft;
            _path.Text = "配置文件：" + AppPaths.SettingsFile;
            Controls.Add(_path);

            BuildList();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_list == null || _path == null) return;
            _list.SetBounds(0, Theme.S(8), Width, Math.Max(0, Height - Theme.S(48)));
            _path.SetBounds(Theme.S(24), Height - Theme.S(36), Width - Theme.S(48), Theme.S(30));
            LayoutRows();
        }

        private void BuildList()
        {
            _list.SuspendLayout();
            _list.Controls.Clear();

            Add("自动创建系统还原点", delegate { return Config.CreateRestorePoint ? "开启" : "关闭"; },
                delegate { Config.CreateRestorePoint = !Config.CreateRestorePoint; Config.Save(); },
                GlyphKind.Restore, Theme.Green);

            Add("删除前自动备份（保留 " + Config.BackupDays + " 天）",
                delegate { return Config.BackupBeforeDelete ? "开启" : "关闭"; },
                delegate { Config.BackupBeforeDelete = !Config.BackupBeforeDelete; Config.Save(); },
                GlyphKind.Report, Theme.Teal);

            Add("备份保留天数",
                delegate { return Config.BackupDays + " 天"; },
                delegate
                {
                    int[] steps = new int[] { 3, 7, 14, 30 };
                    int idx = 0;
                    for (int i = 0; i < steps.Length; i++) if (steps[i] == Config.BackupDays) idx = i;
                    Config.BackupDays = steps[(idx + 1) % steps.Length];
                    Config.Save();
                    BackupService.Purge(Config.BackupDays);
                },
                GlyphKind.Status, Theme.Slate);

            Add("深度扫描 COM 组件与右键菜单扩展",
                delegate { return Config.DeepRegistryScan ? "开启" : "关闭"; },
                delegate { Config.DeepRegistryScan = !Config.DeepRegistryScan; Config.Save(); },
                GlyphKind.Scan, Theme.Accent);

            Add("一键卸载时先执行软件自带卸载程序",
                delegate { return Config.RunUninstallerFirst ? "开启" : "关闭"; },
                delegate { Config.RunUninstallerFirst = !Config.RunUninstallerFirst; Config.Save(); },
                GlyphKind.Uninstall, Theme.Orange);

            Add("卸载程序超时",
                delegate { return (Config.UninstallerTimeoutSec / 60) + " 分钟"; },
                delegate
                {
                    int[] steps = new int[] { 120, 300, 600, 900 };
                    int idx = 0;
                    for (int i = 0; i < steps.Length; i++) if (steps[i] == Config.UninstallerTimeoutSec) idx = i;
                    Config.UninstallerTimeoutSec = steps[(idx + 1) % steps.Length];
                    Config.Save();
                },
                GlyphKind.History, Theme.Gray);

            Add("扫描文件数量上限",
                delegate { return Config.MaxFileItems + " 项"; },
                delegate
                {
                    int[] steps = new int[] { 5000, 20000, 50000, 100000 };
                    int idx = 0;
                    for (int i = 0; i < steps.Length; i++) if (steps[i] == Config.MaxFileItems) idx = i;
                    Config.MaxFileItems = steps[(idx + 1) % steps.Length];
                    Config.Save();
                },
                GlyphKind.Driver, Theme.Red);

            _list.ResumeLayout();
            LayoutRows();
        }

        private void Add(string title, Func<string> value, Action toggle, GlyphKind kind, Color color)
        {
            MetroRow row = new MetroRow();
            row.Title = title;
            row.Subtitle = "点击切换 / 调整";
            row.RightText = value();
            row.IconColor = color;
            row.Initial = "S";
            row.Height = Theme.S(80);

            Action refresh = delegate
            {
                row.RightText = value();
                row.Invalidate();
            };
            row.Click += delegate
            {
                toggle();
                refresh();
            };
            _list.Controls.Add(row);
        }

        private void LayoutRows()
        {
            if (_list == null) return;
            int w = Math.Max(Theme.S(320), _list.ClientSize.Width - Theme.S(18));
            int y = Theme.S(4);
            foreach (Control c in _list.Controls)
            {
                if (c is MetroRow)
                {
                    c.SetBounds(Theme.S(8), y, w - Theme.S(8), c.Height);
                    y += c.Height + 1;
                }
            }
            _list.AutoScrollMinSize = new Size(0, y + Theme.S(6));
        }
    }

    /// <summary>还原点与备份恢复。</summary>
    public class RestorePage : MetroPage
    {
        private ScrollPanel _list;
        private Label _empty, _info;
        private MetroButton _create, _purge, _refresh;

        public RestorePage(MainForm main) : base(main)
        {
            PageTitle = "还原点管理";
            PageSubtitle = "系统还原点 + 7 天删除备份，两种回滚通道";

            _create = new MetroButton();
            _create.Text = "创建系统还原点";
            _create.Accent = true;
            _create.FontSize = 9.5f;
            _create.Click += delegate { CreateRestore(); };
            Controls.Add(_create);

            _purge = new MetroButton();
            _purge.Text = "清理过期备份";
            _purge.FontSize = 9.5f;
            _purge.Click += delegate { Purge(); };
            Controls.Add(_purge);

            _refresh = new MetroButton();
            _refresh.Text = "刷新";
            _refresh.FontSize = 9.5f;
            _refresh.Click += delegate { BuildList(); };
            Controls.Add(_refresh);

            _info = new Label();
            _info.AutoSize = false;
            _info.BackColor = Theme.Bg;
            _info.ForeColor = Theme.InkSoft;
            _info.Font = Theme.Text(9f);
            _info.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_info);

            _list = new ScrollPanel();
            _list.Resize += delegate { LayoutRows(); };
            Controls.Add(_list);

            _empty = new Label();
            _empty.AutoSize = false;
            _empty.BackColor = Theme.Bg;
            _empty.ForeColor = Theme.InkSoft;
            _empty.Font = Theme.Display(17f);
            _empty.TextAlign = ContentAlignment.MiddleCenter;
            _empty.Text = "没有可用备份。每次卸载前会自动创建。";
            Controls.Add(_empty);

            BuildList();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_list == null || _empty == null || _info == null) return;
            int pad = Theme.S(24);
            _create.SetBounds(pad, Theme.S(8), Theme.S(180), Theme.S(42));
            _purge.SetBounds(pad + Theme.S(190), Theme.S(8), Theme.S(150), Theme.S(42));
            _refresh.SetBounds(pad + Theme.S(350), Theme.S(8), Theme.S(96), Theme.S(42));
            _info.SetBounds(Width - pad - Theme.S(300), Theme.S(8), Theme.S(300), Theme.S(42));
            _list.SetBounds(0, Theme.S(62), Width, Math.Max(0, Height - Theme.S(68)));
            _empty.SetBounds(0, Theme.S(150), Width, Theme.S(120));
            LayoutRows();
        }

        private void BuildList()
        {
            _list.SuspendLayout();
            _list.Controls.Clear();
            List<BackupSession> backups = BackupService.List();
            foreach (BackupSession s in backups)
            {
                MetroRow row = new MetroRow();
                row.Title = s.AppName;
                row.Subtitle = s.Created.ToString("yyyy-MM-dd HH:mm") + "  ·  " + s.Entries.Count + " 条记录  ·  " + Ui.FormatSize(BackupService.DirSizeOf(s.Directory));
                row.RightText = "恢复";
                row.IconColor = Theme.Green;
                row.Height = Theme.S(76);
                BackupSession cap = s;
                row.Click += delegate { ConfirmRestore(cap); };
                _list.Controls.Add(row);
            }
            _list.ResumeLayout();

            _info.Text = backups.Count + " 份备份 · " + Ui.FormatSize(BackupService.TotalBytes());
            _empty.Visible = backups.Count == 0;
            LayoutRows();
        }

        private void LayoutRows()
        {
            if (_list == null) return;
            int w = Math.Max(Theme.S(320), _list.ClientSize.Width - Theme.S(18));
            int y = Theme.S(4);
            foreach (Control c in _list.Controls)
            {
                if (c is MetroRow)
                {
                    c.SetBounds(Theme.S(8), y, w - Theme.S(8), c.Height);
                    y += c.Height + 1;
                }
            }
            _list.AutoScrollMinSize = new Size(0, y + Theme.S(6));
        }

        private void ConfirmRestore(BackupSession s)
        {
            Overlay ov = new Overlay();
            ov.Title = "恢复备份";
            ov.Subtitle = s.AppName + " · " + s.Created.ToString("yyyy-MM-dd HH:mm");
            ov.Items = new List<string>
            {
                "将把 " + s.Entries.Count + " 条记录覆盖回原始位置",
                "已存在的文件会被覆盖，注册表项会重新导入",
                "~恢复后仍建议重启一次，确保被占用的文件生效"
            };
            ov.PrimaryText = "开始恢复";
            ov.SecondaryText = "取消";
            ov.Primary += delegate
            {
                try { Controls.Remove(ov); ov.Dispose(); } catch { }
                DoRestore(s);
            };
            ov.Secondary += delegate { try { Controls.Remove(ov); ov.Dispose(); } catch { } };
            ov.Attach(this);
        }

        private void DoRestore(BackupSession s)
        {
            Overlay busy = new Overlay();
            busy.Title = "正在恢复";
            busy.Subtitle = s.AppName;
            busy.Busy = true;
            busy.Items = new List<string>();
            busy.Attach(this);

            Thread t = new Thread(delegate ()
            {
                int n = 0;
                try
                {
                    n = BackupService.Restore(s.Directory, delegate (string m)
                    {
                        try
                        {
                            if (!IsHandleCreated || IsDisposed) return;
                            BeginInvoke((MethodInvoker)delegate
                            {
                                if (busy.IsDisposed) return;
                                busy.Items.Add(m);
                                while (busy.Items.Count > 30) busy.Items.RemoveAt(0);
                                busy.Invalidate();
                            });
                        }
                        catch { }
                    });
                }
                catch { n = -1; }

                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        try { Controls.Remove(busy); busy.Dispose(); } catch { }
                        Overlay done = new Overlay();
                        done.Title = n >= 0 ? "恢复完成" : "恢复失败";
                        done.Subtitle = "已恢复 " + Math.Max(0, n) + " 项。部分文件可能需要重启后生效。";
                        done.Items = new List<string>();
                        done.PrimaryText = "关闭";
                        done.SecondaryText = "";
                        done.Primary += delegate { try { Controls.Remove(done); done.Dispose(); } catch { } };
                        done.Attach(this);
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void CreateRestore()
        {
            if (!Admin.IsAdmin)
            {
                Msg("需要管理员权限", "创建系统还原点需要以管理员身份运行。");
                return;
            }
            Thread t = new Thread(delegate ()
            {
                string err = RestorePointService.Create("CHESUN Uninstaller: 手动创建");
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (err.Length == 0) Msg("还原点创建成功", "已创建系统还原点。");
                        else Msg("还原点创建失败", err);
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Purge()
        {
            int removed = BackupService.Purge(Config.BackupDays);
            BuildList();
            Msg("清理完成", "已删除 " + removed + " 份过期备份。");
        }

        private void Msg(string title, string body)
        {
            Overlay ov = new Overlay();
            ov.Title = title;
            ov.Subtitle = body;
            ov.Items = new List<string>();
            ov.PrimaryText = "关闭";
            ov.SecondaryText = "";
            ov.Primary += delegate { try { Controls.Remove(ov); ov.Dispose(); } catch { } };
            ov.Attach(this);
        }
    }

    /// <summary>白名单：用户自定义永久保护项。</summary>
    public class WhitelistPage : MetroPage
    {
        private MetroSearch _search;
        private MetroButton _add, _openFile;
        private ScrollPanel _list;
        private Label _empty;

        public WhitelistPage(MainForm main) : base(main)
        {
            PageTitle = "白名单";
            PageSubtitle = "命中白名单的路径、服务或注册表项永不删除；内置系统保护始终生效";

            _search = new MetroSearch();
            _search.SetPlaceholder("输入路径、服务名或关键词，例如 D:\\Tools\\keep");
            Controls.Add(_search);

            _add = new MetroButton();
            _add.Text = "加入白名单";
            _add.Accent = true;
            _add.FontSize = 10f;
            _add.Click += delegate { AddEntry(); };
            Controls.Add(_add);

            _openFile = new MetroButton();
            _openFile.Text = "打开文件";
            _openFile.FontSize = 9.5f;
            _openFile.Click += delegate { MainForm.OpenPath(AppPaths.WhitelistFile); };
            Controls.Add(_openFile);

            _list = new ScrollPanel();
            _list.Resize += delegate { LayoutRows(); };
            Controls.Add(_list);

            _empty = new Label();
            _empty.AutoSize = false;
            _empty.BackColor = Theme.Bg;
            _empty.ForeColor = Theme.InkSoft;
            _empty.Font = Theme.Display(17f);
            _empty.TextAlign = ContentAlignment.MiddleCenter;
            _empty.Text = "还没有自定义白名单。\n内置保护：Windows 目录、System32\\drivers、Microsoft 签名、Boot/System 级驱动、关键系统服务名。";
            Controls.Add(_empty);

            BuildList();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_search == null || _list == null || _empty == null) return;
            int pad = Theme.S(24);
            _search.SetBounds(pad, Theme.S(8), Math.Min(Theme.S(460), Width / 2), Theme.S(42));
            _add.SetBounds(_search.Right + Theme.S(10), Theme.S(8), Theme.S(150), Theme.S(42));
            _openFile.SetBounds(_add.Right + Theme.S(10), Theme.S(8), Theme.S(110), Theme.S(42));
            _list.SetBounds(0, Theme.S(62), Width, Math.Max(0, Height - Theme.S(68)));
            _empty.SetBounds(0, Theme.S(150), Width, Theme.S(160));
            LayoutRows();
        }

        private void BuildList()
        {
            _list.SuspendLayout();
            _list.Controls.Clear();
            List<string> items = SafetyGuard.UserWhitelist;
            foreach (string entry in items)
            {
                MetroRow row = new MetroRow();
                row.Title = entry;
                row.Subtitle = "点击移除该保护项（移除后相关项将可被清理）";
                row.RightText = "移除";
                row.IconColor = Theme.Green;
                row.Height = Theme.S(72);
                string cap = entry;
                row.Click += delegate { RemoveEntry(cap); };
                _list.Controls.Add(row);
            }
            _list.ResumeLayout();
            _empty.Visible = items.Count == 0;
            LayoutRows();
        }

        private void LayoutRows()
        {
            if (_list == null) return;
            int w = Math.Max(Theme.S(320), _list.ClientSize.Width - Theme.S(18));
            int y = Theme.S(4);
            foreach (Control c in _list.Controls)
            {
                if (c is MetroRow)
                {
                    c.SetBounds(Theme.S(8), y, w - Theme.S(8), c.Height);
                    y += c.Height + 1;
                }
            }
            _list.AutoScrollMinSize = new Size(0, y + Theme.S(6));
        }

        private void AddEntry()
        {
            string v = _search.Query.Trim();
            if (v.Length < 3)
            {
                Msg("内容太短", "至少输入 3 个字符，建议使用完整路径或驱动服务名。");
                return;
            }
            SafetyGuard.AddWhitelist(v);
            _search.Query = "";
            BuildList();
            Msg("已加入白名单", v + "\n后续扫描不会再删除命中该项的内容。");
        }

        private void RemoveEntry(string entry)
        {
            Overlay ov = new Overlay();
            ov.Title = "移除白名单";
            ov.Subtitle = entry;
            ov.Items = new List<string> { "移除后，该路径/服务不再享受永久保护，可能被后续扫描清理。" };
            ov.PrimaryText = "移除";
            ov.SecondaryText = "取消";
            ov.Primary += delegate
            {
                try { Controls.Remove(ov); ov.Dispose(); } catch { }
                SafetyGuard.RemoveWhitelist(entry);
                BuildList();
            };
            ov.Secondary += delegate { try { Controls.Remove(ov); ov.Dispose(); } catch { } };
            ov.Attach(this);
        }

        private void Msg(string title, string body)
        {
            Overlay ov = new Overlay();
            ov.Title = title;
            ov.Subtitle = body;
            ov.Items = new List<string>();
            ov.PrimaryText = "关闭";
            ov.SecondaryText = "";
            ov.Primary += delegate { try { Controls.Remove(ov); ov.Dispose(); } catch { } };
            ov.Attach(this);
        }
    }
}








