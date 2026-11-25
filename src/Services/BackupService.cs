using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace MetroUninstaller
{
    public class BackupEntry
    {
        public string Kind;        // F / R / D
        public string Original;
        public string Stored;
        public string Note = "";
    }

    public class BackupSession
    {
        public string Directory = "";
        public string AppName = "";
        public DateTime Created = DateTime.Now;
        public List<BackupEntry> Entries = new List<BackupEntry>();
        public long Bytes;
        public int Skipped;

        public string ManifestPath { get { return Path.Combine(Directory, "manifest.tsv"); } }
    }

    /// <summary>删除前备份：文件复制 + reg export，7 天可撤销。</summary>
    public static class BackupService
    {
        private const long MaxFileBytes = 256L * 1024 * 1024;
        private const long MaxTotalBytes = 2L * 1024 * 1024 * 1024;

        public static BackupSession Begin(string appName)
        {
            AppPaths.EnsureDirs();
            BackupSession s = new BackupSession();
            s.AppName = appName;
            s.Directory = Path.Combine(AppPaths.Backups,
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + AppPaths.Sanitize(appName));
            Directory.CreateDirectory(s.Directory);
            Directory.CreateDirectory(Path.Combine(s.Directory, "files"));
            Directory.CreateDirectory(Path.Combine(s.Directory, "reg"));
            WriteManifest(s);
            return s;
        }

        public static bool BackupItem(BackupSession s, ResidueItem item, Action<string> log)
        {
            if (s == null || item == null) return false;
            try
            {
                if (item.Kind == "file" || item.Kind == "shortcut")
                    return BackupFile(s, item.Target, item.Kind, log);
                if (item.Kind == "dir")
                {
                    BackupEntry e = new BackupEntry();
                    e.Kind = "D";
                    e.Original = item.Target;
                    e.Stored = "";
                    e.Note = "目录（文件单独备份）";
                    s.Entries.Add(e);
                    WriteManifest(s);
                    return true;
                }
                if (item.Kind == "regkey" || item.Kind == "com")
                    return BackupRegKey(s, item.Target, log);
                if (item.Kind == "regvalue" || item.Kind == "env")
                    return BackupRegKey(s, item.Target, log);
                if (item.Kind == "service")
                    return BackupRegKey(s, @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" + item.Target, log);
                if (item.Kind == "task")
                {
                    BackupEntry e = new BackupEntry();
                    e.Kind = "T";
                    e.Original = item.Target;
                    e.Stored = "";
                    e.Note = "计划任务（由系统任务库整体管理）";
                    s.Entries.Add(e);
                    WriteManifest(s);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("备份失败：" + item.Target + " — " + ex.Message);
                s.Skipped++;
            }
            return false;
        }

        private static bool BackupFile(BackupSession s, string path, string kind, Action<string> log)
        {
            if (!File.Exists(path)) return false;
            FileInfo fi = new FileInfo(path);
            if (fi.Length > MaxFileBytes)
            {
                s.Skipped++;
                Log.Warn("文件过大，未备份：" + path);
                return false;
            }
            if (s.Bytes + fi.Length > MaxTotalBytes)
            {
                s.Skipped++;
                Log.Warn("备份容量达到上限，未备份：" + path);
                return false;
            }

            string rel = path.Replace(':', '_').TrimStart('\\');
            string dest = Path.Combine(Path.Combine(s.Directory, "files"), rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(path, dest, true);
            s.Bytes += fi.Length;

            BackupEntry e = new BackupEntry();
            e.Kind = "F";
            e.Original = path;
            e.Stored = dest;
            e.Note = kind;
            s.Entries.Add(e);
            WriteManifest(s);
            return true;
        }

        private static bool BackupRegKey(BackupSession s, string keyPath, Action<string> log)
        {
            string normalized = Reg.Normalize(keyPath);
            string dest = Path.Combine(Path.Combine(s.Directory, "reg"),
                AppPaths.Sanitize(normalized) + ".reg");
            dest = dest.Replace(" ", "_");
            ExecResult r = Executor.Run("reg.exe", "export \"" + normalized + "\" \"" + dest + "\" /y", 60000, null);
            if (r.ExitCode != 0 || !File.Exists(dest))
            {
                s.Skipped++;
                Log.Warn("导出注册表失败：" + normalized);
                return false;
            }
            BackupEntry e = new BackupEntry();
            e.Kind = "R";
            e.Original = normalized;
            e.Stored = dest;
            s.Entries.Add(e);
            WriteManifest(s);
            return true;
        }

        public static void WriteManifest(BackupSession s)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# CHESUN Uninstaller backup manifest");
                sb.AppendLine("# app: " + s.AppName);
                sb.AppendLine("# created: " + s.Created.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("kind\toriginal\tstored\tnote");
                foreach (BackupEntry e in s.Entries)
                    sb.AppendLine(e.Kind + "\t" + e.Original + "\t" + e.Stored + "\t" + e.Note);
                File.WriteAllText(s.ManifestPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static List<BackupSession> List()
        {
            List<BackupSession> list = new List<BackupSession>();
            try
            {
                if (!Directory.Exists(AppPaths.Backups)) return list;
                foreach (string dir in Directory.GetDirectories(AppPaths.Backups))
                {
                    string manifest = Path.Combine(dir, "manifest.tsv");
                    if (!File.Exists(manifest)) continue;
                    BackupSession s = new BackupSession();
                    s.Directory = dir;
                    s.AppName = Path.GetFileName(dir);
                    s.Created = Directory.GetCreationTime(dir);
                    foreach (string line in File.ReadAllLines(manifest))
                    {
                        if (line.StartsWith("# app:")) s.AppName = line.Substring(6).Trim();
                        if (line.StartsWith("#") || line.StartsWith("kind\t")) continue;
                        string[] p = line.Split('\t');
                        if (p.Length < 4) continue;
                        BackupEntry e = new BackupEntry();
                        e.Kind = p[0];
                        e.Original = p[1];
                        e.Stored = p[2];
                        e.Note = p[3];
                        s.Entries.Add(e);
                    }
                    list.Add(s);
                }
            }
            catch (Exception ex) { Log.Warn("读取备份清单失败：" + ex.Message); }
            list.Sort(delegate (BackupSession a, BackupSession b) { return b.Created.CompareTo(a.Created); });
            return list;
        }

        public static int Restore(string backupDir, Action<string> log)
        {
            int restored = 0;
            string manifest = Path.Combine(backupDir, "manifest.tsv");
            if (!File.Exists(manifest)) return 0;
            foreach (string line in File.ReadAllLines(manifest))
            {
                if (line.StartsWith("#") || line.StartsWith("kind\t") || line.Length == 0) continue;
                string[] p = line.Split('\t');
                if (p.Length < 3) continue;
                try
                {
                    if (p[0] == "F" && File.Exists(p[2]))
                    {
                        string target = p[1];
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(p[2], target, true);
                        restored++;
                        if (log != null) log("已恢复文件 " + target);
                    }
                    else if (p[0] == "R" && File.Exists(p[2]))
                    {
                        ExecResult r = Executor.Run("reg.exe", "import \"" + p[2] + "\"", 60000, null);
                        if (r.ExitCode == 0)
                        {
                            restored++;
                            if (log != null) log("已恢复注册表 " + p[1]);
                        }
                        else if (log != null) log("恢复注册表失败 " + p[1]);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("恢复失败：" + p[1] + " — " + ex.Message);
                }
            }
            return restored;
        }

        /// <summary>按保留天数清理过期备份。</summary>
        public static int Purge(int days)
        {
            int removed = 0;
            if (days <= 0) return 0;
            try
            {
                if (!Directory.Exists(AppPaths.Backups)) return 0;
                DateTime limit = DateTime.Now.AddDays(-days);
                foreach (string dir in Directory.GetDirectories(AppPaths.Backups))
                {
                    if (Directory.GetCreationTime(dir) < limit)
                    {
                        Directory.Delete(dir, true);
                        removed++;
                    }
                }
            }
            catch (Exception ex) { Log.Warn("清理过期备份失败：" + ex.Message); }
            return removed;
        }

        public static long TotalBytes()
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(AppPaths.Backups)) return 0;
                foreach (string dir in Directory.GetDirectories(AppPaths.Backups))
                    total += DirSizeOf(dir);
            }
            catch { }
            return total;
        }

        public static long DirSizeOf(string dir)
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(dir)) return 0;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }
    }

    public static class RestorePointService
    {
        /// <summary>创建系统还原点，返回空串表示成功，否则返回错误说明。</summary>
        public static string Create(string description)
        {
            try
            {
                using (System.Management.ManagementClass mc =
                    new System.Management.ManagementClass("root\\default", "SystemRestore", null))
                using (System.Management.ManagementBaseObject inParams = mc.GetMethodParameters("CreateRestorePoint"))
                {
                    inParams["Description"] = description;
                    inParams["RestorePointType"] = 0;   // APPLICATION_INSTALL
                    inParams["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE
                    System.Management.ManagementBaseObject outParams =
                        mc.InvokeMethod("CreateRestorePoint", inParams, null);
                    uint ret = 0;
                    try { ret = Convert.ToUInt32(outParams["ReturnValue"]); } catch { }
                    if (ret == 0) { Log.Info("已创建系统还原点：" + description); return ""; }
                    string msg = "系统还原点创建被拒绝（返回 " + ret + "），请确认系统保护已开启。";
                    Log.Warn(msg);
                    return msg;
                }
            }
            catch (Exception ex)
            {
                string msg = "创建还原点失败：" + ex.Message;
                Log.Warn(msg);
                return msg;
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore"))
                {
                    if (k == null) return false;
                    object disable = k.GetValue("DisableSR");
                    if (disable != null && Convert.ToInt32(disable) == 1) return false;
                }
                using (System.Management.ManagementObjectSearcher s = new System.Management.ManagementObjectSearcher(
                    "SELECT State FROM Win32_Service WHERE Name='VSS'"))
                using (System.Management.ManagementObjectCollection c = s.Get())
                {
                    foreach (System.Management.ManagementObject mo in c)
                    {
                        string state = Convert.ToString(mo["State"]);
                        mo.Dispose();
                        return state == "Running" || state == "Stopped";
                    }
                }
                return true;
            }
            catch { return false; }
        }
    }
}


