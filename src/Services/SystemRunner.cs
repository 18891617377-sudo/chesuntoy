using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

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

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct Luaid { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TokenPrivileges
        {
            public uint PrivilegeCount;
            public Luaid Luid;
            public uint Attributes;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string system, string name, out Luaid luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state,
            uint bufferLength, IntPtr previous, IntPtr returnLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes,
            int impersonationLevel, int tokenType, out IntPtr newToken);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ImpersonateLoggedOnUser(IntPtr token);

        [DllImport("advapi32.dll")]
        private static extern bool RevertToSelf();

        private const uint TokenDuplicate = 0x0002;
        private const uint TokenImpersonate = 0x0004;
        private const uint TokenQuery = 0x0008;
        private const uint TokenAdjustPrivileges = 0x0020;
        private const uint MaximumAllowed = 0x02000000;
        private const uint SePrivilegeEnabled = 0x00000002;
        private const uint ProcessQueryInformation = 0x0400;
        private const uint ProcessQueryLimitedInformation = 0x1000;

        private static bool EnableDebugPrivilege()
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out token)) return false;
                bool ok = EnableOne(token, "SeDebugPrivilege");
                // 复制/冒充 TrustedInstaller 还需要这些；有就开，没有不致命。
                EnableOne(token, "SeImpersonatePrivilege");
                EnableOne(token, "SeTakeOwnershipPrivilege");
                EnableOne(token, "SeRestorePrivilege");
                EnableOne(token, "SeBackupPrivilege");
                return ok;
            }
            catch { return false; }
            finally { if (token != IntPtr.Zero) CloseHandle(token); }
        }

        private static bool EnableOne(IntPtr token, string name)
        {
            Luaid luid;
            if (!LookupPrivilegeValue(null, name, out luid)) return false;
            TokenPrivileges tp = new TokenPrivileges();
            tp.PrivilegeCount = 1;
            tp.Luid = luid;
            tp.Attributes = SePrivilegeEnabled;
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;
            return Marshal.GetLastWin32Error() == 0;
        }

        private static IntPtr OpenTrustedInstallerToken()
        {
            Process[] ps = Process.GetProcessesByName("TrustedInstaller");
            if (ps.Length == 0)
            {
                Executor.Run("sc.exe", "start TrustedInstaller", 30000, null);
                Thread.Sleep(1500);
                ps = Process.GetProcessesByName("TrustedInstaller");
            }
            foreach (Process p in ps)
            {
                IntPtr hProcess = IntPtr.Zero, hToken = IntPtr.Zero, dup = IntPtr.Zero;
                try
                {
                    hProcess = OpenProcess(ProcessQueryInformation, false, p.Id);
                    if (hProcess == IntPtr.Zero)
                        hProcess = OpenProcess(ProcessQueryLimitedInformation, false, p.Id);
                    if (hProcess == IntPtr.Zero) continue;
                    if (!OpenProcessToken(hProcess, TokenDuplicate | TokenQuery | TokenImpersonate, out hToken)) continue;
                    if (!DuplicateTokenEx(hToken, MaximumAllowed, IntPtr.Zero, 2, 2, out dup)) continue;
                    return dup;
                }
                finally
                {
                    if (hToken != IntPtr.Zero) CloseHandle(hToken);
                    if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 复制正在运行的 TrustedInstaller 进程令牌并冒充，用于删除 SYSTEM 也打不开的
        /// TrustedInstaller 保护文件。只对调用方传入的非保护路径生效，不做“凭空铸造”。
        /// </summary>
        public static bool ForceDeleteAsTrustedInstaller(string path, bool directory)
        {
            if (string.IsNullOrEmpty(path) || !Admin.IsAdmin) return false;
            if (SafetyGuard.IsProtectedPath(path)) return false;
            if (!EnableDebugPrivilege()) return false;
            IntPtr token = OpenTrustedInstallerToken();
            if (token == IntPtr.Zero) return false;
            try
            {
                if (!ImpersonateLoggedOnUser(token)) return false;
                try
                {
                    if (directory)
                    {
                        try { Directory.Delete(path, true); } catch { }
                        if (Directory.Exists(path))
                        {
                            try
                            {
                                foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                                {
                                    try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
                                }
                                foreach (string d in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
                                {
                                    try { if (Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d, false); } catch { }
                                }
                                Directory.Delete(path, false);
                            }
                            catch { }
                        }
                        return !Directory.Exists(path);
                    }
                    try
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                    }
                    catch { }
                    return !File.Exists(path);
                }
                finally { RevertToSelf(); }
            }
            finally { CloseHandle(token); }
        }

        /// <summary>在 TrustedInstaller 令牌下删除注册表项（用于 Services 键）。</summary>
        public static bool DeleteRegistryAsTrustedInstaller(string fullPath)
        {
            if (!Admin.IsAdmin || string.IsNullOrEmpty(fullPath)) return false;
            if (SafetyGuard.IsProtectedRegistry(fullPath)) return false;
            if (!EnableDebugPrivilege()) return false;
            IntPtr token = OpenTrustedInstallerToken();
            if (token == IntPtr.Zero) return false;
            try
            {
                if (!ImpersonateLoggedOnUser(token)) return false;
                try { return Reg.DeleteKey(fullPath, null); }
                finally { RevertToSelf(); }
            }
            finally { CloseHandle(token); }
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
