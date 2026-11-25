using System;
using System.Drawing;
using System.Runtime.InteropServices;
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
        private static void Main()
        {
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
    }
}



