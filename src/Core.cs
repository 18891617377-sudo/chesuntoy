using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace MetroUninstaller
{
    public static class AppPaths
    {
        public static readonly string Root =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CHESUN Uninstaller");
        public static readonly string Backups = Path.Combine(Root, "Backups");
        public static readonly string Reports = Path.Combine(Root, "Reports");
        public static readonly string Logs = Path.Combine(Root, "Logs");
        public static readonly string SettingsFile = Path.Combine(Root, "settings.ini");
        public static readonly string WhitelistFile = Path.Combine(Root, "whitelist.txt");

        public static void EnsureDirs()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Reports);
            Directory.CreateDirectory(Logs);
        }

        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == ' ') sb.Append(c);
                else sb.Append('_');
            }
            string s = sb.ToString().Trim();
            if (s.Length > 60) s = s.Substring(0, 60);
            return s.Length == 0 ? "unnamed" : s;
        }
    }

    public static class Config
    {
        public static bool CreateRestorePoint = true;
        public static bool BackupBeforeDelete = true;
        public static int BackupDays = 7;
        public static bool DeepRegistryScan = true;
        public static int MaxFileItems = 20000;
        public static bool RunUninstallerFirst = true;
        public static int UninstallerTimeoutSec = 600;

        public static void Load()
        {
            if (!File.Exists(AppPaths.SettingsFile)) return;
            foreach (string line in File.ReadAllLines(AppPaths.SettingsFile))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                int i = t.IndexOf('=');
                if (i <= 0) continue;
                string k = t.Substring(0, i).Trim().ToLowerInvariant();
                string v = t.Substring(i + 1).Trim();
                try
                {
                    switch (k)
                    {
                        case "createrestorepoint": CreateRestorePoint = v == "1"; break;
                        case "backupbeforedelete": BackupBeforeDelete = v == "1"; break;
                        case "backupdays": BackupDays = Math.Max(0, int.Parse(v)); break;
                        case "deepregistryscan": DeepRegistryScan = v == "1"; break;
                        case "maxfileitems": MaxFileItems = Math.Max(100, int.Parse(v)); break;
                        case "rununinstallerfirst": RunUninstallerFirst = v == "1"; break;
                        case "uninstallertimeout": UninstallerTimeoutSec = Math.Max(30, int.Parse(v)); break;
                    }
                }
                catch { }
            }
        }

        public static void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# CHESUN Uninstaller settings");
            sb.AppendLine("CreateRestorePoint=" + (CreateRestorePoint ? "1" : "0"));
            sb.AppendLine("BackupBeforeDelete=" + (BackupBeforeDelete ? "1" : "0"));
            sb.AppendLine("BackupDays=" + BackupDays.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("DeepRegistryScan=" + (DeepRegistryScan ? "1" : "0"));
            sb.AppendLine("MaxFileItems=" + MaxFileItems.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("RunUninstallerFirst=" + (RunUninstallerFirst ? "1" : "0"));
            sb.AppendLine("UninstallerTimeout=" + UninstallerTimeoutSec.ToString(CultureInfo.InvariantCulture));
            AppPaths.EnsureDirs();
            File.WriteAllText(AppPaths.SettingsFile, sb.ToString(), Encoding.UTF8);
        }
    }

    public class LogEntry
    {
        public DateTime Time;
        public string Level;
        public string Message;
        public override string ToString()
        {
            return Time.ToString("HH:mm:ss") + "  " + Level + "  " + Message;
        }
    }

    public static class Log
    {
        public static event Action<LogEntry> Entry;
        private static readonly object Gate = new object();

        public static void Info(string msg) { Add("信息", msg); }
        public static void Warn(string msg) { Add("警告", msg); }
        public static void Error(string msg) { Add("错误", msg); }
        public static void Step(string msg) { Add("步骤", msg); }

        private static void Add(string level, string msg)
        {
            LogEntry e = new LogEntry();
            e.Time = DateTime.Now;
            e.Level = level;
            e.Message = msg;
            try
            {
                AppPaths.EnsureDirs();
                string f = Path.Combine(AppPaths.Logs, "run-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                lock (Gate) File.AppendAllText(f, e.ToString() + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
            Action<LogEntry> h = Entry;
            if (h != null)
            {
                try { h(e); } catch { }
            }
        }
    }

    public class InstalledApp
    {
        public string Name = "";
        public string Version = "";
        public string Publisher = "";
        public string InstallLocation = "";
        public string UninstallString = "";
        public string QuietUninstallString = "";
        public string DisplayIcon = "";
        public long EstimatedSizeKB = -1;
        public string InstallDate = "";
        public string RegistryPath = "";
        public string KeyName = "";
        public string Hive = "";
        public string ProductCode = "";   // MSI GUID（若可识别）
        public string Normalized = "";
        public bool SystemComponent;      // Edge / WebView2 / 更新器等 Windows 组件
        public string SystemReason = "";  // 为什么不允许整包删除

        public long EstimatedBytes { get { return EstimatedSizeKB > 0 ? EstimatedSizeKB * 1024L : -1; } }

        public override string ToString() { return Name; }
    }

    public class ResidueItem
    {
        public string Kind = "file";     // file / dir / regkey / regvalue / service / task / shortcut / env / com
        public string Target = "";       // 文件路径或注册表完整路径
        public string ValueName = "";    // regvalue / env 项的值名
        public string PathFragment = ""; // env 项要移除的路径片段
        public string Detail = "";       // 展示用说明
        public long SizeBytes = -1;
        public bool RestartRequired;
        public bool Protected;           // 命中白名单，永不删除
        public bool BestEffort;          // 缓存类：删不掉就跳过，不登记重启删除
        public bool Selected = true;
        public bool IsBackup { get; set; }
        public string Evidence = "";     // 归属证据

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case "dir": return "目录";
                    case "regkey": return "注册表项";
                    case "regvalue": return "注册表值";
                    case "service": return "系统服务";
                    case "task": return "计划任务";
                    case "shortcut": return "快捷方式";
                    case "env": return "环境变量";
                    case "com": return "COM 组件";
                    default: return "文件";
                }
            }
        }
    }

    public class DriverInfo
    {
        public string ServiceName = "";
        public string DisplayName = "";
        public string Description = "";
        public string ImagePathRaw = "";
        public string ResolvedPath = "";
        public string ParametersText = "";
        public int StartType = 4;
        public int Type = 1;
        public string State = "unknown";
        public string SignatureState = "unknown";   // valid / unsigned / invalid / missing
        public string Signer = "";
        public bool MicrosoftSigned;
        public bool IsProtected;      // 系统原生 / WHQL / 白名单
        public int Attribution;       // 0-100
        public List<string> Evidence = new List<string>();
        public string OwnerSoftware = "";
        public bool Suspicious;

        public string StartText
        {
            get
            {
                switch (StartType)
                {
                    case 0: return "Boot 引导";
                    case 1: return "System 系统";
                    case 2: return "Auto 自动";
                    case 3: return "Manual 手动";
                    case 4: return "Disabled 已禁用";
                    default: return "未知";
                }
            }
        }

        public string TypeText
        {
            get
            {
                if (Type == 1) return "内核驱动";
                if (Type == 2) return "文件系统驱动";
                if (Type == 4) return "设备驱动";
                return "其他";
            }
        }

        public string SignatureText
        {
            get
            {
                switch (SignatureState)
                {
                    case "valid": return MicrosoftSigned ? "Microsoft 签名" : "已签名";
                    case "unsigned": return "无签名";
                    case "invalid": return "签名无效";
                    case "missing": return "文件缺失";
                    default: return "未校验";
                }
            }
        }

        /// <summary>详情面板用的一行签名说明。</summary>
        public string Resolution()
        {
            string s = SignatureText;
            if (!string.IsNullOrEmpty(Signer)) s = s + " · " + Signer;
            return s;
        }
    }

    public class CleanReport
    {
        public string AppName = "";
        public string AppPublisher = "";
        public DateTime Started = DateTime.Now;
        public DateTime Finished = DateTime.Now;
        public bool Success;
        public bool Elevated;
        public bool CacheOnly;       // 系统组件只清缓存，不是整包卸载
        public string RestorePoint = "";
        public string BackupPath = "";
        public string ReportPath = "";
        public List<string> Steps = new List<string>();
        public List<ResidueItem> Items = new List<ResidueItem>();
        public List<DriverInfo> Drivers = new List<DriverInfo>();
        public int DeletedFiles;
        public int DeletedRegistry;
        public int DeletedServices;
        public int DeletedTasks;
        public int PendingRestart;
        public int Failed;
    }

    public static class Admin
    {
        public static bool IsAdmin
        {
            get
            {
                try
                {
                    WindowsIdentity id = WindowsIdentity.GetCurrent();
                    WindowsPrincipal p = new WindowsPrincipal(id);
                    return p.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }

        public static void RelaunchElevated()
        {
            System.Diagnostics.ProcessStartInfo si = new System.Diagnostics.ProcessStartInfo();
            si.FileName = System.Windows.Forms.Application.ExecutablePath;
            si.UseShellExecute = true;
            si.Verb = "runas";
            try { System.Diagnostics.Process.Start(si); Environment.Exit(0); }
            catch (Exception ex) { Log.Warn("放弃提权：" + ex.Message); }
        }
    }
}



