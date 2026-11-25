using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace MetroUninstaller
{
    public class ExecResult
    {
        public int ExitCode = -1;
        public string Output = "";
        public bool TimedOut;
        public bool Started;
    }

    public static class Executor
    {
        public static ExecResult Run(string exe, string args, int timeoutMs, Action<string> log)
        {
            ExecResult r = new ExecResult();
            try
            {
                ProcessStartInfo si = new ProcessStartInfo();
                si.FileName = exe;
                si.Arguments = args;
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                si.RedirectStandardOutput = true;
                si.RedirectStandardError = true;
                si.WindowStyle = ProcessWindowStyle.Hidden;

                StringBuilder sb = new StringBuilder();
                using (Process p = new Process())
                {
                    p.StartInfo = si;
                    p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e)
                    { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
                    { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.Start();
                    r.Started = true;
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        r.TimedOut = true;
                        try { p.Kill(); } catch { }
                        Log.Warn("命令超时：" + exe + " " + args);
                    }
                    try { p.WaitForExit(500); } catch { }
                    r.ExitCode = r.TimedOut ? -2 : p.ExitCode;
                    lock (sb) r.Output = sb.ToString().Trim();
                }
            }
            catch (Exception ex)
            {
                r.Output = ex.Message;
                Log.Warn("执行失败：" + exe + " " + args + " — " + ex.Message);
            }
            if (log != null && r.Output.Length > 0)
            {
                string o = r.Output;
                if (o.Length > 400) o = o.Substring(0, 400) + "…";
                log(exe + " → " + o);
            }
            return r;
        }
    }

    public static class Reg
    {
        public static RegistryKey Hive(string fullPath, out string subPath)
        {
            subPath = "";
            if (string.IsNullOrEmpty(fullPath)) return null;
            string p = fullPath.Trim();
            string low = p.ToLowerInvariant();

            if (low.StartsWith("hkey_local_machine\\"))
            {
                subPath = p.Substring("HKEY_LOCAL_MACHINE\\".Length);
                return Registry.LocalMachine;
            }
            if (low.StartsWith("hkey_current_user\\"))
            {
                subPath = p.Substring("HKEY_CURRENT_USER\\".Length);
                return Registry.CurrentUser;
            }
            if (low.StartsWith("hkey_classes_root\\"))
            {
                subPath = p.Substring("HKEY_CLASSES_ROOT\\".Length);
                return Registry.ClassesRoot;
            }
            if (low.StartsWith("hkey_users\\"))
            {
                subPath = p.Substring("HKEY_USERS\\".Length);
                return Registry.Users;
            }
            subPath = p;
            return Registry.LocalMachine;
        }

        public static string Normalize(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return "";
            string p = fullPath.Trim().TrimEnd('\\');
            string low = p.ToLowerInvariant();
            if (low.StartsWith("hkey_local_machine\\") || low.StartsWith("hkey_current_user\\") ||
                low.StartsWith("hkey_classes_root\\") || low.StartsWith("hkey_users\\"))
                return p;
            return "HKEY_LOCAL_MACHINE\\" + p;
        }

        public static bool DeleteKey(string fullPath, Action<string> log)
        {
            try
            {
                string sub;
                RegistryKey hive = Hive(fullPath, out sub);
                if (hive == null) return false;
                hive.DeleteSubKeyTree(sub, false);
                if (log != null) log("已删除注册表项 " + fullPath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("删除注册表项失败：" + fullPath + " — " + ex.Message);
                if (log != null) log("删除失败（注册表项）：" + fullPath + " — " + ex.Message);
                return false;
            }
        }

        public static bool DeleteValue(string fullPath, string valueName, Action<string> log)
        {
            try
            {
                string sub;
                RegistryKey hive = Hive(fullPath, out sub);
                if (hive == null) return false;
                using (RegistryKey k = hive.OpenSubKey(sub, true))
                {
                    if (k == null) return false;
                    k.DeleteValue(valueName, false);
                }
                if (log != null) log("已删除注册表值 " + fullPath + " → " + valueName);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("删除注册表值失败：" + fullPath + " — " + ex.Message);
                if (log != null) log("删除失败（注册表值）：" + fullPath + " — " + ex.Message);
                return false;
            }
        }

        public static bool Exists(string fullPath)
        {
            try
            {
                string sub;
                RegistryKey hive = Hive(fullPath, out sub);
                if (hive == null) return false;
                using (RegistryKey k = hive.OpenSubKey(sub, false)) return k != null;
            }
            catch { return false; }
        }
    }

    public static class PendingDelete
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool MoveFileEx(string existing, string newName, int flags);

        private const int MoveFileDelayUntilReboot = 4;

        public static bool Schedule(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                bool ok = MoveFileEx(path, null, MoveFileDelayUntilReboot);
                if (!ok)
                {
                    int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    Log.Warn("登记重启删除失败：" + path + "（Win32 " + err + "）");
                }
                else Log.Info("已登记重启后删除：" + path);
                return ok;
            }
            catch (Exception ex)
            {
                Log.Warn("登记重启删除异常：" + path + " — " + ex.Message);
                return false;
            }
        }

        /// <summary>读取系统 PendingFileRenameOperations，统计当前待重启删除项。</summary>
        public static int CountPending()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager"))
                {
                    if (k == null) return 0;
                    object o = k.GetValue("PendingFileRenameOperations");
                    if (o == null) return 0;
                    string[] arr = o as string[];
                    if (arr == null) return 1;
                    int n = 0;
                    for (int i = 0; i < arr.Length; i += 2) if (!string.IsNullOrEmpty(arr[i])) n++;
                    return n;
                }
            }
            catch { return 0; }
        }
    }
}


