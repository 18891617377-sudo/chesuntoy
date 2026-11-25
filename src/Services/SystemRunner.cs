using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace MetroUninstaller
{
    /// <summary>
    /// 以 LocalSystem 身份执行单条命令：通过临时计划任务（/ru SYSTEM）拉起 cmd，
    /// 用于处理 TrustedInstaller / SYSTEM ACL 保护的驱动文件与服务键。
    /// 说明：不伪造令牌，走的是 Windows 自带的任务计划 SYSTEM 账户，属于系统管理常规手段。
    /// </summary>
    public static class SystemRunner
    {
        public static bool Run(string command, int timeoutMs, out string output)
        {
            output = "";
            string id = Guid.NewGuid().ToString("N").Substring(0, 10);
            string dir = Path.GetTempPath();
            string bat = Path.Combine(dir, "chesun_sys_" + id + ".cmd");
            string log = Path.Combine(dir, "chesun_sys_" + id + ".log");
            string done = Path.Combine(dir, "chesun_sys_" + id + ".done");
            string task = "CHESUN_SysHelper_" + id;

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("chcp 65001 >nul 2>&1");
                sb.AppendLine(command + " >> \"" + log + "\" 2>&1");
                sb.AppendLine("echo %ERRORLEVEL% > \"" + done + "\"");
                File.WriteAllText(bat, sb.ToString(), new UTF8Encoding(false));

                ExecResult create = Executor.Run("schtasks.exe", "/create /tn \"" + task + "\" /tr \"cmd.exe /c " + bat +
                    "\" /sc once /st 00:00 /ru SYSTEM /rl HIGHEST /f", 60000, null);
                if (create.ExitCode != 0)
                {
                    Log.Warn("创建 SYSTEM 任务失败：" + create.Output);
                    return false;
                }

                Executor.Run("schtasks.exe", "/run /tn \"" + task + "\"", 60000, null);

                DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
                while (DateTime.Now < deadline)
                {
                    if (File.Exists(done)) break;
                    Thread.Sleep(500);
                }

                try
                {
                    if (File.Exists(log)) output = File.ReadAllText(log, Encoding.UTF8).Trim();
                }
                catch { }
                bool ok = File.Exists(done);
                Log.Info("SYSTEM 执行：" + (command.Length > 120 ? command.Substring(0, 120) + "…" : command) +
                    " → " + (ok ? "完成" : "超时"));
                return ok;
            }
            catch (Exception ex)
            {
                Log.Warn("SYSTEM 执行失败：" + ex.Message);
                return false;
            }
            finally
            {
                Executor.Run("schtasks.exe", "/delete /tn \"" + task + "\" /f", 30000, null);
                try { File.Delete(bat); } catch { }
                try { File.Delete(done); } catch { }
                try { File.Delete(log); } catch { }
            }
        }

        /// <summary>以 SYSTEM 接管所有权并强制删除文件或目录。</summary>
        public static bool ForceDelete(string path, bool directory)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = "\"" + path + "\"";
            string cmd = "takeown /f " + p + (directory ? " /r /d y" : " /a") +
                         " & icacls " + p + " /grant *S-1-5-32-544:F" + (directory ? " /t /c /q" : " /c /q") +
                         (directory ? " & rmdir /s /q " + p : " & del /f /q /a " + p);
            string output;
            Run(cmd, 180000, out output);
            return directory ? !Directory.Exists(path) : !File.Exists(path);
        }

        /// <summary>以 SYSTEM 删除服务（含 ACL 拒绝的受保护服务）。</summary>
        public static bool DeleteServiceAsSystem(string name, out string output)
        {
            string cmd = "sc stop \"" + name + "\" & sc delete \"" + name + "\"";
            return Run(cmd, 60000, out output);
        }

        /// <summary>以 SYSTEM 删除 DriverStore 中的驱动包（pnputil /delete-driver /uninstall /force）。</summary>
        public static bool RemoveDriverPackage(string infName, out string output)
        {
            if (string.IsNullOrEmpty(infName)) { output = ""; return false; }
            return Run("pnputil /delete-driver " + infName + " /uninstall /force", 180000, out output);
        }
    }
}
