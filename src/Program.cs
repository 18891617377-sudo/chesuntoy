using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MetroUninstaller
{
    internal static class Program
    {
        private static Mutex _mutex;

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        [STAThread]
        private static void Main(string[] args)
        {
            // 无界面自检：给沙盒/CI 验证反 Rootkit 扫描用，不弹窗、不改系统
            if (args != null && args.Length > 0 && args[0].Equals("--rootkit-scan", StringComparison.OrdinalIgnoreCase))
            {
                RunRootkitScan(args);
                return;
            }

            // 驱动强制删除、SYSTEM 接管都需要管理员；启动时直接申请提权
            if (!Admin.IsAdmin) Admin.RelaunchElevated();

            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi >= 96) Theme.DpiFactor = dpi / 96f;
            }
            catch
            {
                try
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                        Theme.DpiFactor = g.DpiX / 96f;
                }
                catch { }
            }

            bool created = false;
            _mutex = new Mutex(true, "CHESUNUninstaller_SingleInstance_v1", out created);
            if (!created)
            {
                MessageBox.Show("CHESUN Uninstaller 已在运行。", "CHESUN Uninstaller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                Log.Error("界面异常：" + e.Exception.Message);
                MessageBox.Show("界面线程异常：\n" + e.Exception.Message, "CHESUN Uninstaller",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                Log.Error("未处理异常：" + (ex == null ? "未知" : ex.ToString()));
            };

            try
            {
                AppPaths.EnsureDirs();
                Config.Load();
                BackupService.Purge(Config.BackupDays);
            }
            catch (Exception ex) { Log.Warn("初始化失败：" + ex.Message); }

            Application.Run(new MainForm());
        }

        /// <summary>
        /// 无界面反 Rootkit 自检。输出到指定文件，供沙盒/CI 验证，不修改系统。
        /// 用法：CHESUNUninstaller.exe --rootkit-scan C:\path\report.txt
        /// </summary>
        private static void RunRootkitScan(string[] args)
        {
            string outPath = args.Length > 1 && args[1].Trim().Length > 0
                ? args[1].Trim()
                : Path.Combine(AppPaths.Root, "rootkit-scan.txt");
            StringBuilder sb = new StringBuilder();
            try
            {
                AppPaths.EnsureDirs();
                Config.Load();
                sb.AppendLine("CHESUN Uninstaller · 反 Rootkit / Bootkit 自检");
                sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("管理员：" + (Admin.IsAdmin ? "是" : "否"));
                sb.AppendLine("程序：" + Application.ExecutablePath);
                sb.AppendLine();

                List<DriverInfo> drivers = DriverInventory.LoadAll(null);
                sb.AppendLine("驱动服务数：" + drivers.Count);

                List<Finding> root = AntiRootkit.ScanRootkit(drivers);
                List<Finding> modules = AntiRootkit.ScanLoadedModules(drivers);
                List<Finding> boot = AntiRootkit.ScanBootkit();
                sb.AppendLine("Rootkit 启发式条目：" + root.Count);
                sb.AppendLine("已加载模块条目：" + modules.Count);
                sb.AppendLine("Bootkit 条目：" + boot.Count);
                sb.AppendLine();

                AppendFindings(sb, "ROOTKIT / 服务", root);
                AppendFindings(sb, "已加载模块", modules);
                AppendFindings(sb, "BOOTKIT / 引导", boot);

                int high = 0, mid = 0;
                foreach (Finding f in root) { if (f.Severity == "高") high++; else if (f.Severity == "中") mid++; }
                foreach (Finding f in modules) { if (f.Severity == "高") high++; else if (f.Severity == "中") mid++; }
                foreach (Finding f in boot) { if (f.Severity == "高") high++; else if (f.Severity == "中") mid++; }
                sb.AppendLine();
                sb.AppendLine("汇总：高危 " + high + " · 可疑 " + mid);
                sb.AppendLine("结束：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("自检异常：" + ex);
            }

            try
            {
                string dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        private static void AppendFindings(StringBuilder sb, string title, List<Finding> list)
        {
            sb.AppendLine("[" + title + "]");
            if (list == null || list.Count == 0)
            {
                sb.AppendLine("  无");
                sb.AppendLine();
                return;
            }
            foreach (Finding f in list)
                sb.AppendLine("  " + f.ToString());
            sb.AppendLine();
        }
    }
}



