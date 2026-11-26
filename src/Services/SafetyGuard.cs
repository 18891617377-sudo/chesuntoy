using System;
using System.Collections.Generic;
using System.IO;

namespace MetroUninstaller
{
    /// <summary>安全红线：系统路径、系统驱动与用户白名单的最终拦截层。</summary>
    public static class SafetyGuard
    {
        private static readonly string[] ProtectedFragments = new string[]
        {
            @"\windows\system32", @"\windows\syswow64", @"\windows\winsxs", @"\windows\servicing",
            @"\windows\system32\drivers", @"\windows\system32\config", @"\windows\inf",
            @"\program files\common files\microsoft", @"\program files (x86)\common files\microsoft",
            @"\program files\microsoft\edge", @"\program files (x86)\microsoft\edge",
            @"\program files\microsoft\edgeupdate", @"\program files (x86)\microsoft\edgeupdate",
            @"\program files\microsoft\edgewebview", @"\program files (x86)\microsoft\edgewebview",
            @"\programdata\microsoft\edgeupdate", @"\programdata\microsoft\edgewebview",
            @"\programdata\microsoft\windows", @"\programdata\microsoft\crypto",
            @"\appdata\local\microsoft\windows", @"\appdata\roaming\microsoft\windows",
            @"\appdata\local\microsoft\windowsapps", @"\$recycle.bin", @"\system volume information",
            @"\windows\assembly", @"\windows\microsoft.net"
        };

        private static readonly string[] ProtectedRegistryRoots = new string[]
        {
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Hardware Profiles",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes",
            @"HKEY_CLASSES_ROOT\CLSID",
            @"HKEY_CLASSES_ROOT\Interface",
            @"HKEY_CLASSES_ROOT\TypeLib",
            @"HKEY_CLASSES_ROOT\Wow6432Node",
            @"HKEY_CURRENT_USER\Software\Classes",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies",
            @"HKEY_CURRENT_USER\Software\Policies",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node",
            @"HKEY_CURRENT_USER\Software\Microsoft",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft",
            @"HKEY_CURRENT_USER\Software\Google",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Google",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Google",
            @"HKEY_CURRENT_USER\Software\Mozilla",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Mozilla",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Mozilla",
            @"HKEY_CURRENT_USER\Software\Adobe",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Adobe",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Adobe",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\NVIDIA Corporation",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\NVIDIA Corporation",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Intel",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Intel",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\AMD",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\AMD",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Realtek",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Realtek",
            @"HKEY_CURRENT_USER\Software\Valve",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows",
            @"HKEY_CURRENT_USER\Software\Microsoft\Office",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Office",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Office",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKEY_CURRENT_USER\Environment"
        };

        /// <summary>厂商级公共根键/目录名：只允许向下匹配具体产品，绝不允许整根删除。</summary>
        private static readonly string[] VendorRoots = new string[]
        {
            "microsoft", "google", "mozilla", "appleinc", "adobe", "intel", "nvidia",
            "nvidfacorporation", "nvidia corporation", "amd", "advancedmicrodevices",
            "realtek", "valve", "oracle", "tencent", "huawei", "samsung",
            "epicgames", "electronicarts", "commonfiles", "windows", "programfiles"
        };

        /// <summary>硬编码的系统关键驱动服务名（即使签名/路径异常也不得删除）。</summary>
        private static readonly HashSet<string> SystemDrivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "acpi", "acpiex", "afd", "ahcache", "amdppm", "beep", "boootvid", "bowser", "bthport",
            "cdrom", "cdfs", "cflt", "clfs", "cng", "condrv", "crashdmp", "dfsc", "disk",
            "diskpart", "drmk", "dxgkrnl", "fastfat", "fileinfo", "fltmgr", "fs_rec", "fvevol",
            "gpuenergydrv", "hidclass", "hidparse", "hidusb", "http", "i8042prt", "iorate",
            "kbdclass", "kbdhid", "ks", "ksecdd", "ksecpkg", "ksthunk", "lltdio", "luafv",
            "mcupdate", "monitor", "mountmgr", "mouclass", "mouhid", "mpsdrv", "mrxsmb",
            "mrxsmb20", "msfs", "msisadrv", "msrpc", "mssmbios", "mup", "ndis", "ndistapi",
            "ndisuio", "ndiswan", "netbt", "netio", "netvsc", "npfs", "nsi", "ntfs", "null",
            "partmgr", "pci", "pdc", "peauth", "pnpmgr", "psched", "pvhk", "rasl2tp", "raspppoe",
            "raspptp", "rassstp", "rdbss", "rdpbus", "rdpdr", "rdyboost", "refs", "rsfilter",
            "sccfilter", "scfilter", "sdta", "secdrv", "sisraid", "spacedump", "srv", "srv2",
            "srvnet", "storahci", "storclass", "storport", "svchost", "swenum", "tcpip",
            "tdi", "termdd", "tflite", "tmfilter", "tmm", "tcpipreg", "umbus", "uaspstor",
            "udfs", "ufs", "umbus", "usbccgp", "usbhub", "usbport", "usbstor", "usbvideo",
            "vdrvroot", "vhdmp", "volmgr", "volmgrx", "volsnap", "volume", "vsmraid", "vwifibus",
            "vwififlt", "wdf01000", "wfplwfs", "winio", "wmiacpi", "wmiop", "ws2ifsl",
            "wudfrd", "wudfhost"
        };

        private static List<string> _userWhitelist;
        private static DateTime _whitelistStamp = DateTime.MinValue;
        private static readonly object Gate = new object();

        public static List<string> UserWhitelist
        {
            get
            {
                lock (Gate)
                {
                    if (_userWhitelist != null && _whitelistStamp > DateTime.Now.AddSeconds(-5))
                        return _userWhitelist;
                    List<string> list = new List<string>();
                    try
                    {
                        if (File.Exists(AppPaths.WhitelistFile))
                        {
                            foreach (string line in File.ReadAllLines(AppPaths.WhitelistFile))
                            {
                                string t = line.Trim();
                                if (t.Length == 0 || t.StartsWith("#")) continue;
                                list.Add(t);
                            }
                        }
                    }
                    catch { }
                    _userWhitelist = list;
                    _whitelistStamp = DateTime.Now;
                    return list;
                }
            }
        }

        public static void AddWhitelist(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return;
            AppPaths.EnsureDirs();
            try
            {
                using (StreamWriter w = new StreamWriter(AppPaths.WhitelistFile, true, System.Text.Encoding.UTF8))
                    w.WriteLine(entry.Trim());
                _whitelistStamp = DateTime.MinValue;
            }
            catch (Exception ex) { Log.Warn("写入白名单失败：" + ex.Message); }
        }

        public static void RemoveWhitelist(string entry)
        {
            try
            {
                if (!File.Exists(AppPaths.WhitelistFile)) return;
                List<string> lines = new List<string>(File.ReadAllLines(AppPaths.WhitelistFile));
                List<string> keep = new List<string>();
                foreach (string l in lines)
                    if (!string.Equals(l.Trim(), entry.Trim(), StringComparison.OrdinalIgnoreCase)) keep.Add(l);
                File.WriteAllLines(AppPaths.WhitelistFile, keep.ToArray(), System.Text.Encoding.UTF8);
                _whitelistStamp = DateTime.MinValue;
            }
            catch (Exception ex) { Log.Warn("更新白名单失败：" + ex.Message); }
        }

        /// <summary>厂商公共根目录名（只允许向下一层找具体产品）。</summary>
        public static bool IsVendorRootName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = SoftwareInventory.Normalize(name);
            foreach (string v in VendorRoots)
            {
                string vn = SoftwareInventory.Normalize(v);
                if (vn.Length > 0 && n == vn) return true;
            }
            return false;
        }

        /// <summary>安装目录是否足够具体：拒绝盘符根、Program Files / Windows / 用户目录等公共根。</summary>
        public static bool IsSafeInstallRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.Trim().Trim('"').TrimEnd('\\');
            string root = Path.GetPathRoot(p);
            if (string.IsNullOrEmpty(root)) return false;
            if (p.Length <= root.Length + 1) return false;

            string[] immune = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };
            foreach (string im in immune)
            {
                if (string.IsNullOrEmpty(im)) continue;
                if (string.Equals(p, im.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            }

            string rel = p.Substring(root.Length).Trim('\\');
            string[] seg = rel.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return seg.Length >= 2;
        }

        public static bool IsUserWhitelisted(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string low = text.ToLowerInvariant();
            foreach (string w in UserWhitelist)
                if (low.Contains(w.ToLowerInvariant())) return true;
            return false;
        }

        /// <summary>绝不允许删除的关键服务（即使名称偶合）。</summary>
        private static readonly HashSet<string> CriticalServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "wuauserv", "bits", "cryptsvc", "trustedinstaller", "windefend", "mpssvc", "eventlog",
            "schedule", "rpcss", "dcomlaunch", "lanmanserver", "lanmanworkstation", "dhcp",
            "dnscache", "nsi", "winmgmt", "themes", "audiosrv", "audioreg", "spooler", "seclogon",
            "appinfo", "tokenbroker", "sysmain", "diagtrack", "wscsvc", "wisvc", "bfe", "nlasvc",
            "profsvc", "samss", "lsm", "keyiso", "vaultsvc", "netman", "ncaSvc", "wudfsvc"
        };

        public static bool IsCriticalService(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            return SystemDrivers.Contains(name) || CriticalServices.Contains(name);
        }

        public static bool IsProtectedPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            string low = path.ToLowerInvariant();
            foreach (string frag in ProtectedFragments)
                if (low.Contains(frag)) return true;
            foreach (string w in UserWhitelist)
                if (low.Contains(w.ToLowerInvariant())) return true;
            return false;
        }

        public static bool IsProtectedRegistry(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return true;
            string p = fullPath.TrimEnd('\\');
            string low = p.ToLowerInvariant();
            foreach (string w in UserWhitelist)
                if (low.Contains(w.ToLowerInvariant())) return true;

            foreach (string root in ProtectedRegistryRoots)
            {
                string rl = root.ToLowerInvariant();
                if (low == rl) return true;
                // 保护的是根节点本身与其祖先，允许删除其下的具体条目
                if (rl.StartsWith(low + "\\")) return true;
            }
            return false;
        }

        public static bool IsSystemDriverName(string serviceName)
        {
            return SystemDrivers.Contains(serviceName);
        }

        /// <summary>驱动默认保护判定：系统关键服务 / Microsoft 签名 / 白名单始终保护；
        /// 无签名或签名无效的驱动即使位于 System32，也允许进入“可疑驱动”显式清理流程。</summary>
        public static bool IsDriverProtected(DriverInfo d)
        {
            if (IsSystemDriverName(d.ServiceName)) return true;
            if (d.MicrosoftSigned) return true;
            foreach (string w in UserWhitelist)
                if ((d.ResolvedPath ?? "").ToLowerInvariant().Contains(w.ToLowerInvariant()) ||
                    string.Equals(d.ServiceName, w, StringComparison.OrdinalIgnoreCase))
                    return true;

            // 信任链无法验证的可疑驱动不自动锁死，交给 UI 二次确认后清理。
            if (d.Suspicious) return false;

            string low = (d.ResolvedPath ?? "").ToLowerInvariant();
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
            if (low.StartsWith(win + "\\")) return true;
            if (d.StartType == 0 || d.StartType == 1) return true;
            return false;
        }
    }
}





