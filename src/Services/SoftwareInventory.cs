using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace MetroUninstaller
{
    /// <summary>从三处 Uninstall 注册表位置汇总已安装软件。</summary>
    public static class SoftwareInventory
    {
        private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string UninstallPath32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<InstalledApp> Load(Action<string> progress)
        {
            List<InstalledApp> list = new List<InstalledApp>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddFromHive(Registry.LocalMachine, UninstallPath, "HKLM", list, seen, progress);
            AddFromHive(Registry.LocalMachine, UninstallPath32, "HKLM", list, seen, progress);
            AddFromHive(Registry.CurrentUser, UninstallPath, "HKCU", list, seen, progress);

            MarkSystemComponents(list);

            list.Sort(delegate (InstalledApp a, InstalledApp b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        /// <summary>
        /// Edge 不是普通软件：它和 EdgeUpdate / WebView2 一起被 Windows 当系统组件维护。
        /// 强行删 Program Files 只会产生大量“待重启删除”，重启后仍会被更新器修回来，还会破坏 WebView2。
        /// 这里只做标记，真正清理必须走“仅清缓存”，不能走整包删除。
        /// </summary>
        private static void MarkSystemComponents(List<InstalledApp> list)
        {
            foreach (InstalledApp app in list)
            {
                string n = app.Normalized;
                string p = (app.InstallLocation + " " + app.UninstallString + " " + app.DisplayIcon).ToLowerInvariant();
                bool edgePath = p.IndexOf("\\microsoft\\edge", StringComparison.Ordinal) >= 0 ||
                    p.IndexOf("\\microsoft\\edgeupdate", StringComparison.Ordinal) >= 0 ||
                    p.IndexOf("\\microsoft\\edgewebview", StringComparison.Ordinal) >= 0;
                bool edgeName = n.IndexOf("microsoftedge", StringComparison.Ordinal) >= 0 ||
                    n == "edge" || n.IndexOf("edgeupdate", StringComparison.Ordinal) >= 0 ||
                    n.IndexOf("edgewebview", StringComparison.Ordinal) >= 0;
                if (!edgePath && !edgeName) continue;

                app.SystemComponent = true;
                if (n.IndexOf("edgeupdate", StringComparison.Ordinal) >= 0)
                    app.SystemReason = "Microsoft Edge 更新器，负责把 Edge 装回并修复，不能按普通软件删除";
                else if (n.IndexOf("edgewebview", StringComparison.Ordinal) >= 0)
                    app.SystemReason = "WebView2 系统运行库，大量桌面应用依赖它，删除会连带破坏其他软件";
                else
                    app.SystemReason = "Microsoft Edge 是 Windows 系统组件，安装目录受系统维护，不能整包删除";
            }
        }

        private static void AddFromHive(RegistryKey hive, string subPath, string hiveName,
            List<InstalledApp> list, HashSet<string> seen, Action<string> progress)
        {
            try
            {
                using (RegistryKey root = hive.OpenSubKey(subPath))
                {
                    if (root == null) return;
                    string[] names = root.GetSubKeyNames();
                    foreach (string keyName in names)
                    {
                        InstalledApp app = ReadApp(root, keyName, hiveName, subPath);
                        if (app == null) continue;
                        string id = app.Name + "|" + app.Version + "|" + app.InstallLocation;
                        if (!seen.Add(id)) continue;
                        list.Add(app);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取卸载信息失败（" + subPath + "）：" + ex.Message);
            }
            if (progress != null) progress(list.Count + " 个程序");
        }

        private static InstalledApp ReadApp(RegistryKey root, string keyName, string hiveName, string parentPath)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(keyName))
                {
                    if (k == null) return null;
                    string name = AsString(k.GetValue("DisplayName"));
                    if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) return null;

                    string sysComponent = AsString(k.GetValue("SystemComponent"));
                    if (sysComponent == "1") return null;
                    string parent = AsString(k.GetValue("ParentKeyName"));
                    if (!string.IsNullOrEmpty(parent)) return null;
                    if (name.IndexOf("Update for", StringComparison.OrdinalIgnoreCase) >= 0) return null;
                    if (name.IndexOf("Security Update", StringComparison.OrdinalIgnoreCase) >= 0) return null;

                    InstalledApp app = new InstalledApp();
                    app.Name = name.Trim();
                    app.Version = AsString(k.GetValue("DisplayVersion"));
                    app.Publisher = AsString(k.GetValue("Publisher"));
                    app.InstallLocation = CleanPath(AsString(k.GetValue("InstallLocation")));
                    app.UninstallString = AsString(k.GetValue("UninstallString"));
                    app.QuietUninstallString = AsString(k.GetValue("QuietUninstallString"));
                    app.DisplayIcon = ExpandIcon(AsString(k.GetValue("DisplayIcon")));
                    if (app.InstallLocation.Length == 0 && app.DisplayIcon.Length > 0 && File.Exists(app.DisplayIcon))
                    {
                        try
                        {
                            string dir = Path.GetDirectoryName(app.DisplayIcon);
                            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                            if (!string.IsNullOrEmpty(dir) && !dir.StartsWith(win, StringComparison.OrdinalIgnoreCase))
                                app.InstallLocation = dir;
                        }
                        catch { }
                    }
                    app.InstallDate = AsString(k.GetValue("InstallDate"));
                    app.KeyName = keyName;
                    app.Hive = hiveName;
                    app.RegistryPath = BuildKeyPath(hiveName, parentPath + "\\" + keyName);

                    object size = k.GetValue("EstimatedSize");
                    if (size != null)
                    {
                        try { app.EstimatedSizeKB = Convert.ToInt64(size); }
                        catch { app.EstimatedSizeKB = -1; }
                    }

                    if (keyName.StartsWith("{") && keyName.EndsWith("}")) app.ProductCode = keyName;
                    if (AsString(k.GetValue("WindowsInstaller")) == "1" && app.ProductCode.Length == 0)
                        app.ProductCode = keyName;

                    app.Normalized = Normalize(app.Name);
                    return app;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取软件项失败：" + keyName + " — " + ex.Message);
                return null;
            }
        }

        private static string BuildKeyPath(string hive, string sub)
        {
            return (hive == "HKLM" ? "HKEY_LOCAL_MACHINE\\" : "HKEY_CURRENT_USER\\") + sub;
        }

        public static string AsString(object o)
        {
            if (o == null) return "";
            return o.ToString().Trim();
        }

        private static string CleanPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            string t = p.Trim().Trim('"');
            if (t.EndsWith("\\") && t.Length > 3) t = t.Substring(0, t.Length - 1);
            return t;
        }

        private static string ExpandIcon(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            string t = v.Trim();
            t = t.Trim('"');
            int comma = t.LastIndexOf(',');
            if (comma > 2) t = t.Substring(0, comma);
            t = t.Trim('"');
            return Environment.ExpandEnvironmentVariables(t);
        }

        /// <summary>把软件名压成用于匹配目录/驱动名的形式。</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>名称拆词，供目录名模糊匹配使用。</summary>
        public static List<string> Tokens(string name)
        {
            List<string> toks = new List<string>();
            if (string.IsNullOrEmpty(name)) return toks;
            string cleaned = name;
            foreach (char sep in new char[] { '-', '_', '.', ',', '(', ')', '[', ']', '/', '\\', '+', '&', ':' })
                cleaned = cleaned.Replace(sep, ' ');
            string[] parts = cleaned.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in parts)
            {
                string t = p.Trim();
                if (t.Length < 3) continue;
                if (IsNoise(t)) continue;
                toks.Add(t);
            }
            return toks;
        }

        private static bool IsNoise(string t)
        {
            string low = t.ToLowerInvariant();
            string[] noise = new string[] {
                "the","and","for","with","pro","plus","free","setup","install","installer",
                "version","win","windows","x64","x86","64","32","bit","edition","update",
                "software","studio","app","application","suite","professional","standard",
                "microsoft","google","mozilla","apple","adobe","nvidia","intel","amd","realtek",
                "files","data","cache","temp","bin","lib","common","documents","windows"
            };
            foreach (string n in noise) if (low == n) return true;
            return false;
        }
    }
}



