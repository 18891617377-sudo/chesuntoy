using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace MetroUninstaller
{
    public class Finding
    {
        public string Severity;
        public string Title;
        public string Detail;
        public override string ToString()
        {
            return "[" + Severity + "] " + Title + (Detail.Length > 0 ? " — " + Detail : "");
        }
    }

    /// <summary>反 Rootkit / Bootkit 检测 + 离线清理包 + WDAC 拒绝策略生成。</summary>
    public static class AntiRootkit
    {
        public static List<Finding> ScanRootkit(List<DriverInfo> drivers)
        {
            List<Finding> list = new List<Finding>();
            if (drivers == null) return list;
            int n = 0;            foreach (DriverInfo d in drivers)
            {
                n++;
                if (!string.IsNullOrEmpty(d.ResolvedPath) && !File.Exists(d.ResolvedPath))
                {
                    Add(list, "高", "服务存在但驱动文件缺失", d.ServiceName + " → " + d.ResolvedPath);
                    continue;
                }
                if (d.Suspicious)
                    Add(list, "高", "无有效签名且不在系统目录", d.ServiceName + " → " + d.ResolvedPath);

                bool running = d.State.IndexOf("运行", StringComparison.Ordinal) >= 0 ||
                               d.State.IndexOf("RUN", StringComparison.OrdinalIgnoreCase) >= 0;
                if (running && d.StartType == 4)
                    Add(list, "中", "已禁用却仍在加载（隐藏服务特征）", d.ServiceName + " · " + d.State);

                string low = (d.ResolvedPath ?? "").ToLowerInvariant();
                if (low.IndexOf("\\temp\\", StringComparison.Ordinal) >= 0 ||
                    low.IndexOf("\\downloads\\", StringComparison.Ordinal) >= 0 ||
                    low.IndexOf("\\appdata\\", StringComparison.Ordinal) >= 0)
                    Add(list, "高", "驱动文件位于用户可写目录", d.ServiceName + " → " + d.ResolvedPath);

                if (d.StartType <= 1 && !d.MicrosoftSigned && low.IndexOf("\\driverstore\\", StringComparison.Ordinal) < 0)
                    Add(list, "中", "非微软签名的 Boot/System 级驱动", d.ServiceName + " · Start=" + d.StartType + " · " + d.Signer);
            }
            Add(list, "信息", "已枚举内核驱动服务", n + " 个；上述为启发式命中，需人工确认归属");
            return list;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RTL_PROCESS_MODULE_INFORMATION
        {
            public IntPtr Section;
            public IntPtr MappedBase;
            public IntPtr ImageBase;
            public uint ImageSize;
            public uint Flags;
            public ushort LoadOrderIndex;
            public ushort InitOrderIndex;
            public ushort LoadCount;
            public ushort OffsetToFileName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] FullPathName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RTL_PROCESS_MODULES
        {
            public uint NumberOfModules;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public RTL_PROCESS_MODULE_INFORMATION[] Modules;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);

        private static List<string> EnumKernelModulePaths()
        {
            List<string> list = new List<string>();
            int len = 0;
            int status = NtQuerySystemInformation(11, IntPtr.Zero, 0, out len);
            if (len <= 0) len = 16 * 1024;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr buf = Marshal.AllocHGlobal(len);
                try
                {
                    int needed = 0;
                    status = NtQuerySystemInformation(11, buf, len, out needed);
                    if (status == unchecked((int)0xC0000004))
                    {
                        len = needed + 4096;
                        continue;
                    }
                    if (status != 0)
                        throw new Exception("NtQuerySystemInformation 失败，状态 0x" + status.ToString("X8"));
                    int count = Marshal.ReadInt32(buf);
                    int offset = (int)Marshal.OffsetOf(typeof(RTL_PROCESS_MODULES), "Modules");
                    int size = Marshal.SizeOf(typeof(RTL_PROCESS_MODULE_INFORMATION));
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr p = IntPtr.Add(buf, offset + i * size);
                        RTL_PROCESS_MODULE_INFORMATION mi = (RTL_PROCESS_MODULE_INFORMATION)Marshal.PtrToStructure(p, typeof(RTL_PROCESS_MODULE_INFORMATION));
                        if (mi.FullPathName == null) continue;
                        string path = Encoding.ASCII.GetString(mi.FullPathName);
                        int nul = path.IndexOf('\0');
                        if (nul >= 0) path = path.Substring(0, nul);
                        if (path.Length > 0) list.Add(path);
                    }
                    return list;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            throw new Exception("NtQuerySystemInformation 缓冲区不足，状态 0x" + status.ToString("X8"));
        }

        /// <summary>枚举已加载内核模块，找“加载了但没有服务注册”的隐藏驱动。</summary>
        public static List<Finding> ScanLoadedModules(List<DriverInfo> drivers)
        {
            List<Finding> list = new List<Finding>();
            try
            {
                List<string> mods = EnumKernelModulePaths();
                Dictionary<string, string> services = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (drivers != null)
                {
                    foreach (DriverInfo d in drivers)
                    {
                        string p = (d.ResolvedPath ?? "").Trim();
                        if (p.Length > 0) services[p] = d.ServiceName;
                    }
                }

                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
                int total = 0, noService = 0, bad = 0;
                foreach (string raw in mods)
                {
                    if (raw.Length == 0) continue;
                    string path = DriverInventory.ResolveImagePath(raw);
                    if (path.Length == 0) continue;
                    total++;
                    bool registered = services.ContainsKey(path);
                    if (registered) continue;

                    noService++;
                    SignatureInfo sig = null;
                    try { sig = SignatureVerifier.Check(path); } catch { }
                    bool badSig = sig != null && (sig.State == "unsigned" || sig.State == "invalid");
                    bool outsideWindows = !path.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase);

                    if (badSig)
                    {
                        bad++;
                        Add(list, "高", "已加载但无服务注册的非可信驱动", path);
                    }
                    else if (outsideWindows)
                    {
                        bad++;
                        Add(list, "中", "Windows 目录外加载的驱动（需确认）", path + (sig == null ? " · 无法验证签名" : " · " + sig.State));
                    }
                }
                Add(list, "信息", "已枚举已加载内核模块",
                    total + " 个；无服务注册 " + noService + " 个，其中命中可疑 " + bad + " 个（需人工确认）");
            }
            catch (Exception ex)
            {
                Add(list, "信息", "内核模块枚举失败", ex.Message);
            }
            return list;
        }

        public static List<Finding> ScanBootkit()
        {
            List<Finding> list = new List<Finding>();
            int secure = 0;
            try
            {
                object o = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State") != null
                    ? Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State").GetValue("UEFISecureBootEnabled") : null;
                if (o != null) secure = Convert.ToInt32(o);
                Add(list, secure == 1 ? "信息" : "高", "Secure Boot",
                    secure == 1 ? "已启用（固件层会校验 bootloader 签名）" : "未启用或非 UEFI，bootkit 可持久化");
            }
            catch { }

            // BCD 启动项
            try
            {
                ExecResult bcd = Executor.Run("bcdedit.exe", "/enum all", 60000, null);
                string[] lines = (bcd.Output ?? "").Split('\n');
                string currentId = "";
                foreach (string raw in lines)
                {
                    string t = raw.Trim();
                    if (t.StartsWith("identifier", StringComparison.OrdinalIgnoreCase))
                    {
                        int sp = t.IndexOf(' ');
                        currentId = sp > 0 ? t.Substring(sp + 1).Trim() : "";
                    }
                    if (t.StartsWith("path", StringComparison.OrdinalIgnoreCase) && t.IndexOf(".efi", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bool standard = t.IndexOf("\\EFI\\Microsoft\\Boot\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        t.IndexOf("\\EFI\\Boot\\bootmgfw.efi", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!standard)
                            Add(list, "中", "非标准 BCD 引导路径",
                                "BCD:" + (currentId.Length == 0 ? "{unknown}" : currentId) + " " + t);
                    }
                }
            }
            catch (Exception ex) { Add(list, "信息", "BCD 读取失败", ex.Message); }

            // ESP 枚举
            string esp = MountEsp();
            if (esp.Length == 0)
            {
                Add(list, "信息", "EFI 系统分区未挂载", "需要管理员权限，或本机为 Legacy BIOS");
                return list;
            }
            try
            {
                if (!Directory.Exists(esp))
                {
                    Add(list, "信息", "ESP 未挂载", esp);
                    return list;
                }
                int files = 0, bad = 0;
                foreach (string f in Directory.GetFiles(esp, "*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".efi" && ext != ".exe" && ext != ".dll") continue;
                    files++;
                    SignatureInfo sig = SignatureVerifier.Check(f);
                    if (!sig.MicrosoftSigned && (sig.State == "unsigned" || sig.State == "invalid" || sig.State == "unknown"))
                    {
                        bad++;
                        Add(list, "高", "ESP 内非微软签名引导组件", f.Replace(esp, "ESP:") + " · " + sig.State + " · " + sig.Signer);
                    }
                }
                Add(list, "信息", "ESP 引导组件统计", files + " 个 PE 文件，其中非微软签名 " + bad + " 个");
            }
            catch (Exception ex) { Add(list, "信息", "ESP 扫描失败", ex.Message); }
            finally { UnmountEsp(esp); }
            return list;
        }

        private static void Add(List<Finding> list, string sev, string title, string detail)
        {
            Finding f = new Finding();
            f.Severity = sev;
            f.Title = title;
            f.Detail = detail ?? "";
            list.Add(f);
            Log.Info("[" + sev + "] " + title + " — " + f.Detail);
        }

        private static string EspPathFromDetail(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return "";
            int i = detail.IndexOf("ESP:", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            string s = detail.Substring(i);
            int sep = s.IndexOf(" · ", StringComparison.Ordinal);
            if (sep > 0) s = s.Substring(0, sep);
            return s.Trim();
        }

        private static string MountEsp()
        {
            for (char c = 'S'; c <= 'Z'; c++)
            {
                string drive = c + ":";
                if (Directory.Exists(drive + "\\")) continue;
                Executor.Run("mountvol.exe", drive + " /S", 30000, null);
                if (Directory.Exists(drive + "\\EFI")) return drive + "\\";
            }
            return "";
        }

        private static void UnmountEsp(string esp)
        {
            try { Executor.Run("mountvol.exe", esp.Substring(0, 2) + " /D", 30000, null); } catch { }
        }

        /// <summary>生成 WinPE 离线清理包：删服务、禁用启动、删 sys、删 DriverStore 包。</summary>
        public static string BuildOfflinePack(DriverInfo d, List<Finding> bootFindings, out string readme)
        {
            string dir = Path.Combine(AppPaths.Root, "OfflinePacks",
                AppPaths.Sanitize(d.ServiceName) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);

            string sysPath = d.ResolvedPath ?? "";
            string rel = sysPath;
            int wi = sysPath.IndexOf("\\Windows\\", StringComparison.OrdinalIgnoreCase);
            if (wi >= 0) rel = sysPath.Substring(wi); else rel = "\\Windows\\System32\\drivers\\" + Path.GetFileName(sysPath);

            string storeFolder = "";
            try
            {
                string low = sysPath.ToLowerInvariant();
                int idx = low.IndexOf("\\driverstore\\filerepository\\", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    string rest = sysPath.Substring(idx + "\\driverstore\\filerepository\\".Length);
                    int sep = rest.IndexOf('\\');
                    storeFolder = sep > 0 ? rest.Substring(0, sep) : rest;
                }
            }
            catch { }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal enabledelayedexpansion");
            sb.AppendLine("rem 用法：在 WinPE 中执行  clean-driver.cmd  D:\\   （D: 为离线系统盘）");
            sb.AppendLine("set ROOT=%~1");
            sb.AppendLine("if \"%ROOT%\"==\"\" (echo 用法：clean-driver.cmd ^<离线系统盘^> & exit /b 1)");
            sb.AppendLine("if not exist \"%ROOT%\\Windows\\System32\\config\\SYSTEM\" (echo 找不到离线 SYSTEM 注册表 & exit /b 1)");
            sb.AppendLine("set SVC=" + d.ServiceName);
            sb.AppendLine("set SYS=%ROOT%" + rel.Replace("/", "\\"));
            sb.AppendLine("set BK=%ROOT%\\CHESUN-Backup\\" + AppPaths.Sanitize(d.ServiceName) + "_%date:~0,4%%date:~5,2%%date:~8,2%_%time:~0,2%%time:~3,2%");
            sb.AppendLine("if not exist \"%BK%\" mkdir \"%BK%\"");
            sb.AppendLine("reg load HKLM\\CHESUN_OFF \"%ROOT%\\Windows\\System32\\config\\SYSTEM\" >nul 2>&1");
            sb.AppendLine("for /f \"tokens=3\" %%i in ('reg query HKLM\\CHESUN_OFF\\Select /v Current 2^>nul') do set CSN=%%i");
            sb.AppendLine("if not defined CSN set CSN=1");
            sb.AppendLine("set CS=ControlSet00%CSN%");
            sb.AppendLine("reg export HKLM\\CHESUN_OFF\\%CS%\\Services\\%SVC% \"%BK%\\service.reg\" /y >nul 2>&1");
            sb.AppendLine("reg add HKLM\\CHESUN_OFF\\%CS%\\Services\\%SVC% /v Start /t REG_DWORD /d 4 /f >nul 2>&1");
            sb.AppendLine("reg delete HKLM\\CHESUN_OFF\\%CS%\\Services\\%SVC% /f >nul 2>&1");
            sb.AppendLine("reg unload HKLM\\CHESUN_OFF >nul 2>&1");
            sb.AppendLine("if exist \"%SYS%\" copy /y \"%SYS%\" \"%BK%\\\" >nul 2>&1 & del /f /q \"%SYS%\"");
            if (storeFolder.Length > 0)
                sb.AppendLine("if exist \"%ROOT%\\Windows\\System32\\DriverStore\\FileRepository\\" + storeFolder + "\" rmdir /s /q \"%ROOT%\\Windows\\System32\\DriverStore\\FileRepository\\" + storeFolder + "\"");
            sb.AppendLine("echo [完成] 服务 %SVC% 已禁用并删除，驱动文件已清理。备份在 %BK%");
            sb.AppendLine("pause");
            string cmdPath = Path.Combine(dir, "clean-driver.cmd");
            File.WriteAllText(cmdPath, sb.ToString(), new UTF8Encoding(false));

            // EFI 可疑文件清单 + 备份删除脚本
            StringBuilder efi = new StringBuilder();
            efi.AppendLine("@echo off");
            efi.AppendLine("rem 在 WinPE 中挂载 ESP 后执行：mountvol S: /S，然后运行 clean-efi.cmd D:\\");
            efi.AppendLine("set ESP=S:\\");
            efi.AppendLine("set ROOT=%~1");
            efi.AppendLine("if \"%ROOT%\"==\"\" (echo 用法：clean-efi.cmd ^<离线系统盘^> [备份目录] & exit /b 1)");
            efi.AppendLine("set BK=%~2");
            efi.AppendLine("if \"%BK%\"==\"\" set BK=%ROOT%\\CHESUN-EFI-Backup");
            efi.AppendLine("if not exist \"%BK%\" mkdir \"%BK%\"");
            int count = 0;
            foreach (Finding f in bootFindings)
            {
                if (f.Severity != "高" && f.Severity != "中") continue;
                string p = EspPathFromDetail(f.Detail);
                if (p.Length == 0) continue;
                p = p.Replace("ESP:", "%ESP%");
                efi.AppendLine("if exist \"" + p + "\" (copy /y \"" + p + "\" \"%BK%\\\" >nul 2>&1 & del /f /q \"" + p + "\")");
                count++;
            }
            efi.AppendLine("echo [提示] 删除 bootmgfw.efi 后需执行：bcdboot %ROOT%\\Windows /s S: /f UEFI 修复引导");
            efi.AppendLine("echo 已处理 " + count + " 个可疑 EFI 文件，备份在 %BK%");
            efi.AppendLine("pause");
            File.WriteAllText(Path.Combine(dir, "clean-efi.cmd"), efi.ToString(), new UTF8Encoding(false));

            // 非标准 BCD 启动项：单独脚本，先导出备份再删除
            List<string> bcdIds = new List<string>();
            foreach (Finding f in bootFindings)
            {
                if (f.Severity != "高" && f.Severity != "中") continue;
                if (!f.Detail.StartsWith("BCD:", StringComparison.OrdinalIgnoreCase)) continue;
                string rest = f.Detail.Substring(4).Trim();
                int sp = rest.IndexOf(' ');
                string id = sp > 0 ? rest.Substring(0, sp) : rest;
                if (id.StartsWith("{") && id.EndsWith("}") && id != "{unknown}" && !bcdIds.Contains(id)) bcdIds.Add(id);
            }
            if (bcdIds.Count > 0)
            {
                StringBuilder bcd = new StringBuilder();
                bcd.AppendLine("@echo off");
                bcd.AppendLine("setlocal");
                bcd.AppendLine("rem 用法：clean-bcd.cmd  D:\\   （D: 为离线系统盘；会先导出 BCD 备份）");
                bcd.AppendLine("set ROOT=%~1");
                bcd.AppendLine("if \"%ROOT%\"==\"\" (echo 用法：clean-bcd.cmd ^<离线系统盘^> & exit /b 1)");
                bcd.AppendLine("if not exist \"%ROOT%\\Boot\\BCD\" (echo 找不到 %ROOT%\\Boot\\BCD & pause & exit /b 1)");
                bcd.AppendLine("set BK=%ROOT%\\CHESUN-Backup\\BCD_%date:~0,4%%date:~5,2%%date:~8,2%_%time:~0,2%%time:~3,2%");
                bcd.AppendLine("if not exist \"%BK%\" mkdir \"%BK%\"");
                bcd.AppendLine("bcdedit /store \"%ROOT%\\Boot\\BCD\" /export \"%BK%\\BCD-backup\" >nul 2>&1");
                bcd.AppendLine("echo 将删除以下非标准启动项：");
                foreach (string id in bcdIds) bcd.AppendLine("echo   " + id);
                bcd.AppendLine("choice /c YN /m \"确认删除？可能影响引导，已先导出 BCD 备份\"");
                bcd.AppendLine("if errorlevel 2 exit /b 0");
                foreach (string id in bcdIds)
                    bcd.AppendLine("bcdedit /store \"%ROOT%\\Boot\\BCD\" /delete " + id);
                bcd.AppendLine("echo [完成] 已删除 " + bcdIds.Count + " 个非标准启动项。");
                bcd.AppendLine("echo 若无法启动：bcdboot %ROOT%\\Windows /s S: /f UEFI");
                bcd.AppendLine("pause");
                File.WriteAllText(Path.Combine(dir, "clean-bcd.cmd"), bcd.ToString(), new UTF8Encoding(false));
            }

            StringBuilder rd = new StringBuilder();
            rd.AppendLine("CHESUN 离线清理包 — " + d.ServiceName);
            rd.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            rd.AppendLine("");
            rd.AppendLine("1. 用微 PE / WinRE 启动，确认离线系统盘符（例如 D:）。");
            rd.AppendLine("2. 管理员命令行执行：clean-driver.cmd D:\\");
            rd.AppendLine("3. EFI 可疑项：在 PE 中先 mountvol S: /S，再执行 clean-efi.cmd，脚本会先备份再删除。");
            rd.AppendLine("4. 若删除了 bootmgfw.efi，执行 bcdboot D:\\Windows /s S: /f UEFI 重建引导。");
            rd.AppendLine("");
            rd.AppendLine("局限：ESP 文件删除只能清掉磁盘上的 bootkit；SPI 固件/EFI 变量里的植入不在此范围。");
            string readmePath = Path.Combine(dir, "README.txt");
            File.WriteAllText(readmePath, rd.ToString(), new UTF8Encoding(false));
            readme = readmePath;
            return dir;
        }

        /// <summary>用官方 ConfigCI 生成按哈希拒绝的 WDAC 策略。</summary>
        public static string BuildWadcDeny(DriverInfo d, out string deployPath)
        {
            string dir = Path.Combine(AppPaths.Root, "WdacPolicies",
                AppPaths.Sanitize(d.ServiceName) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            string sys = d.ResolvedPath ?? "";
            if (!File.Exists(sys)) throw new Exception("驱动文件不存在，无法生成哈希规则：" + sys);

            string ps1 = Path.Combine(dir, "build-policy.ps1");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference='Stop'");
            sb.AppendLine("$sys='" + sys.Replace("'", "''") + "'");
            sb.AppendLine("$dir='" + dir.Replace("'", "''") + "'");
            sb.AppendLine("$Deny = New-CIPolicyRule -Level Hash -DriverFilePath $sys -Deny");
            sb.AppendLine("$AllowAll = Join-Path $dir 'AllowAll.xml'");
            sb.AppendLine("Copy-Item (Join-Path $env:windir 'schemas\\CodeIntegrity\\ExamplePolicies\\AllowAll.xml') $AllowAll -Force");
            sb.AppendLine("$xml = Join-Path $dir 'policy.xml'");
            sb.AppendLine("Merge-CIPolicy -PolicyPaths $AllowAll -OutputFilePath $xml -Rules $Deny");
            sb.AppendLine("Set-CiPolicyIdInfo -FilePath $xml -PolicyName 'CHESUN Deny " + d.ServiceName.Replace("'", "''") + "' -ResetPolicyID");
            sb.AppendLine("ConvertFrom-CIPolicy $xml (Join-Path $dir 'policy.cip')");
            sb.AppendLine("Write-Output 'OK'");
            File.WriteAllText(ps1, sb.ToString(), new UTF8Encoding(false));

            ExecResult r = Executor.Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\"", 180000, null);
            string cip = Path.Combine(dir, "policy.cip");
            if (!File.Exists(cip))
                throw new Exception("策略生成失败：" + (r.Output ?? "").Trim());

            string guid = "";
            try
            {
                string xml = File.ReadAllText(Path.Combine(dir, "policy.xml"));
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(xml, "<PolicyID>\\{([0-9A-Fa-f-]+)\\}</PolicyID>");
                if (m.Success) guid = m.Groups[1].Value;
            }
            catch { }
            if (guid.Length == 0) guid = Guid.NewGuid().ToString();

            string deploy = Path.Combine(dir, "deploy-wdac.cmd");
            StringBuilder dp = new StringBuilder();
            dp.AppendLine("@echo off");
            dp.AppendLine("rem 部署后需重启；启用前请先确认策略在测试机通过");
            dp.AppendLine("net session >nul 2>&1 || (echo 需要管理员权限 & pause & exit /b 1)");
            dp.AppendLine("set DEST=%WINDIR%\\System32\\CodeIntegrity\\CiPolicies\\Active\\" + guid + ".cip");
            dp.AppendLine("if exist \"%DEST%\" copy /y \"%DEST%\" \"%DEST%.bak\" >nul 2>&1");
            dp.AppendLine("copy /y \"" + cip + "\" \"%DEST%\"");
            dp.AppendLine("echo 已写入 %DEST%，重启后生效。");
            dp.AppendLine("echo 回滚：删除该 .cip 或还原 .bak 后重启。");
            dp.AppendLine("pause");
            File.WriteAllText(deploy, dp.ToString(), new UTF8Encoding(false));
            deployPath = deploy;
            return dir;
        }

        public static string Sha256(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in h) sb.Append(b.ToString("X2"));
                return sb.ToString();
            }
        }
    }
}
