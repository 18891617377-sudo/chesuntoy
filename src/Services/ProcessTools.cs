using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;

namespace MetroUninstaller
{
    public class ProcessHit
    {
        public int Pid;
        public string Name = "";
        public string Path = "";
        public string CommandLine = "";
        public string Reason = "";
        public override string ToString() { return Name + " (" + Pid + ")"; }
    }

    /// <summary>关联进程发现与强制终止（含子进程树）。</summary>
    public static class ProcessTools
    {
        public static List<ProcessHit> FindRelated(InstalledApp app)
        {
            List<ProcessHit> hits = new List<ProcessHit>();
            if (app == null) return hits;

            List<string> dirs = new List<string>();
            if (!string.IsNullOrEmpty(app.InstallLocation) && Directory.Exists(app.InstallLocation))
                dirs.Add(app.InstallLocation.TrimEnd('\\') + "\\");
            if (!string.IsNullOrEmpty(app.DisplayIcon) && File.Exists(app.DisplayIcon))
            {
                string d = Path.GetDirectoryName(app.DisplayIcon);
                if (!string.IsNullOrEmpty(d)) dirs.Add(d.TrimEnd('\\') + "\\");
            }

            string[] fields = new string[] { "ProcessId", "Name", "ExecutablePath", "CommandLine" };
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process"))
                using (ManagementObjectCollection rows = searcher.Get())
                {
                    foreach (ManagementObject mo in rows)
                    {
                        try
                        {
                            int pid = Convert.ToInt32(mo["ProcessId"]);
                            string name = Convert.ToString(mo["Name"]);
                            string path = Convert.ToString(mo["ExecutablePath"]);
                            string cmd = Convert.ToString(mo["CommandLine"]);
                            if (pid == Process.GetCurrentProcess().Id) continue;

                            string reason = MatchReason(path, cmd, dirs, app);
                            if (reason.Length == 0) continue;

                            ProcessHit hit = new ProcessHit();
                            hit.Pid = pid;
                            hit.Name = name;
                            hit.Path = path;
                            hit.CommandLine = cmd;
                            hit.Reason = reason;
                            hits.Add(hit);
                        }
                        catch { }
                        finally { mo.Dispose(); }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("枚举进程失败：" + ex.Message);
            }
            return hits;
        }

        private static string MatchReason(string path, string cmd, List<string> dirs, InstalledApp app)
        {
            if (!string.IsNullOrEmpty(path))
            {
                foreach (string d in dirs)
                {
                    if (path.StartsWith(d, StringComparison.OrdinalIgnoreCase)) return "安装目录内进程";
                }
                if (!string.IsNullOrEmpty(app.DisplayIcon) &&
                    string.Equals(path, app.DisplayIcon, StringComparison.OrdinalIgnoreCase))
                    return "主程序";
            }
            if (!string.IsNullOrEmpty(cmd) && !string.IsNullOrEmpty(app.InstallLocation))
            {
                if (cmd.IndexOf(app.InstallLocation, StringComparison.OrdinalIgnoreCase) >= 0)
                    return "命令行引用安装目录";
            }
            return "";
        }

        public static int KillAll(List<ProcessHit> hits, Action<string> log)
        {
            int killed = 0;
            List<int> pids = new List<int>();
            foreach (ProcessHit h in hits) pids.Add(h.Pid);

            foreach (ProcessHit h in hits)
            {
                try
                {
                    Process p = Process.GetProcessById(h.Pid);
                    bool closed = false;
                    try { closed = p.CloseMainWindow(); } catch { }
                    if (closed && p.WaitForExit(2500)) { killed++; if (log != null) log("已关闭 " + h.Name + " (" + h.Pid + ")"); continue; }
                }
                catch { }
            }

            // 再强杀剩余（含子进程）
            foreach (ProcessHit h in hits)
            {
                try
                {
                    Process p = Process.GetProcessById(h.Pid);
                    KillTree(p, pids, 0);
                    killed++;
                    if (log != null) log("已终止 " + h.Name + " (" + h.Pid + "，" + h.Reason + ")");
                }
                catch (ArgumentException) { }
                catch (Exception ex)
                {
                    if (log != null) log("终止失败 " + h.Name + " (" + h.Pid + ")：" + ex.Message);
                }
            }
            return killed;
        }

        private static void KillTree(Process p, List<int> guard, int depth)
        {
            if (depth > 6) return;
            try
            {
                foreach (ManagementObject child in FindChildren(p.Id))
                {
                    try
                    {
                        int cpid = Convert.ToInt32(child["ProcessId"]);
                        if (guard.Contains(cpid)) { child.Dispose(); continue; }
                        guard.Add(cpid);
                        KillTree(Process.GetProcessById(cpid), guard, depth + 1);
                    }
                    catch { }
                    finally { child.Dispose(); }
                }
            }
            catch { }
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch { }
        }

        private static ManagementObjectCollection FindChildren(int pid)
        {
            ManagementObjectSearcher s = new ManagementObjectSearcher(
                "SELECT ProcessId FROM Win32_Process WHERE ParentProcessId=" + pid);
            return s.Get();
        }

        /// <summary>识别正在运行的进程是否加载了指定驱动文件（用于归属佐证）。</summary>
        public static List<string> ProcessesWithModule(string modulePath)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(modulePath) || !File.Exists(modulePath)) return result;
            string fileName = Path.GetFileName(modulePath);
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT ProcessId, Name, ExecutablePath FROM Win32_Process"))
                using (ManagementObjectCollection rows = s.Get())
                {
                    foreach (ManagementObject mo in rows)
                    {
                        try
                        {
                            Process p = Process.GetProcessById(Convert.ToInt32(mo["ProcessId"]));
                            foreach (ProcessModule m in p.Modules)
                            {
                                if (string.Equals(m.FileName, modulePath, StringComparison.OrdinalIgnoreCase))
                                {
                                    result.Add(p.ProcessName + " (" + p.Id + ")");
                                    break;
                                }
                            }
                        }
                        catch { }
                        finally { mo.Dispose(); }
                    }
                }
            }
            catch { }
            return result;
        }
    }
}


