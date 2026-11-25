using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace MetroUninstaller
{
    /// <summary>深度残留扫描：文件系统、注册表全路径、服务、计划任务、快捷方式、COM。</summary>
    public static class ResidueScanner
    {
        public static List<ResidueItem> Scan(InstalledApp app, Action<string> progress)
        {
            List<ResidueItem> items = new List<ResidueItem>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Report(progress, "扫描安装目录…");
            ScanInstallDir(app, items, seen, progress);

            Report(progress, "扫描用户配置与公共数据目录…");
            ScanDataDirs(app, items, seen);

            Report(progress, "扫描注册表…");
            ScanRegistry(app, items, seen);

            Report(progress, "扫描服务与计划任务…");
            ScanServices(app, items, seen);
            ScanScheduledTasks(app, items, seen);

            Report(progress, "扫描自启动与环境变量…");
            ScanRunKeys(app, items, seen);
            ScanEnvironment(app, items, seen);
            ScanSharedDlls(app, items, seen);

            Report(progress, "扫描快捷方式…");
            ScanShortcuts(app, items, seen);

            if (Config.DeepRegistryScan)
            {
                Report(progress, "扫描 COM 组件与右键菜单扩展…");
                ScanCom(app, items, seen, progress);
            }

            Report(progress, "扫描完成，共 " + items.Count + " 项");
            return items;
        }

        private static void Report(Action<string> p, string s)
        {
            if (p != null) p(s);
        }

        private static void Add(List<ResidueItem> items, HashSet<string> seen, string kind, string target,
            string detail, long size, string evidence)
        {
            if (string.IsNullOrEmpty(target)) return;
            string key = kind + "|" + target;
            if (!seen.Add(key)) return;

            ResidueItem it = new ResidueItem();
            it.Kind = kind;
            it.Target = target;
            it.Detail = detail;
            it.SizeBytes = size;
            it.Evidence = evidence;

            if (kind == "file" || kind == "dir" || kind == "shortcut")
                it.Protected = SafetyGuard.IsProtectedPath(target);
            else if (kind == "regkey" || kind == "com")
                it.Protected = SafetyGuard.IsProtectedRegistry(target);
            else if (kind == "regvalue" || kind == "env")
                it.Protected = SafetyGuard.IsUserWhitelisted(target);
            else if (kind == "service")
                it.Protected = SafetyGuard.IsCriticalService(target) || SafetyGuard.IsUserWhitelisted(target);
            items.Add(it);
        }

        // ---------------------------------------------------------------- 文件系统

        private static void ScanInstallDir(InstalledApp app, List<ResidueItem> items,
            HashSet<string> seen, Action<string> progress)
        {
            string loc = app.InstallLocation;
            if (string.IsNullOrEmpty(loc) || !Directory.Exists(loc)) return;
            if (!SafetyGuard.IsSafeInstallRoot(loc))
            {
                Log.Warn("安装目录过于宽泛，已跳过文件扫描：" + loc);
                return;
            }

            int count = 0;
            int max = Config.MaxFileItems;
            bool stop = false;
            Stack<string> stack = new Stack<string>();
            stack.Push(loc);

            while (stack.Count > 0 && !stop)
            {
                string dir = stack.Pop();
                try
                {
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        long size = 0;
                        try { size = new FileInfo(f).Length; } catch { }
                        Add(items, seen, "file", f, Relative(loc, f), size, "位于安装目录");
                        count++;
                        if (count >= max) { stop = true; break; }
                    }
                    if (stop) break;
                    foreach (string d in Directory.GetDirectories(dir)) stack.Push(d);
                }
                catch (Exception ex)
                {
                    Log.Warn("无法读取目录：" + dir + " — " + ex.Message);
                }
            }
            if (stop) Log.Warn("安装目录文件数超过上限 " + max + "，已截断。");

            Add(items, seen, "dir", loc, "安装目录（文件清理后整体删除）", 0, "InstallLocation");
            if (progress != null) progress("安装目录内 " + count + " 个文件");
        }

        private static string Relative(string root, string full)
        {
            try
            {
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return full.Substring(root.Length).TrimStart('\\');
            }
            catch { }
            return full;
        }

        private static void ScanDataDirs(InstalledApp app, List<ResidueItem> items, HashSet<string> seen)
        {
            string[] roots = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            string publisherNorm = SoftwareInventory.Normalize(app.Publisher);
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                try
                {
                    foreach (string dir in Directory.GetDirectories(root))
                    {
                        string name = Path.GetFileName(dir);
                        if (SafetyGuard.IsVendorRootName(name))
                        {
                            foreach (string sub in Directory.GetDirectories(dir))
                            {
                                string why2 = MatchName(Path.GetFileName(sub), app);
                                if (why2.Length > 0) AddDataDir(items, seen, sub, "厂商目录下的 " + why2);
                            }
                            continue;
                        }
                        string why = MatchName(name, app);
                        if (why.Length > 0)
                        {
                            AddDataDir(items, seen, dir, why);
                            continue;
                        }
                        if (publisherNorm.Length > 3 && SoftwareInventory.Normalize(name) == publisherNorm)
                        {
                            foreach (string sub in Directory.GetDirectories(dir))
                            {
                                string why2 = MatchName(Path.GetFileName(sub), app);
                                if (why2.Length > 0) AddDataDir(items, seen, sub, "发布者目录下的 " + why2);
                            }
                            foreach (string f in Directory.GetFiles(dir))
                            {
                                if (MatchName(Path.GetFileNameWithoutExtension(f), app).Length > 0)
                                {
                                    long size = 0;
                                    try { size = new FileInfo(f).Length; } catch { }
                                    Add(items, seen, "file", f, "发布者目录内的同名文件", size, "发布者目录");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Warn("扫描配置目录失败：" + root + " — " + ex.Message); }
            }
        }

        private static void AddDataDir(List<ResidueItem> items, HashSet<string> seen, string dir, string why)
        {
            long size = DirSize(dir, 30000);
            Add(items, seen, "dir", dir, "用户配置/数据目录", size, why);
        }

        private static long DirSize(string dir, int limit)
        {
            long total = 0; int n = 0;
            Stack<string> st = new Stack<string>();
            st.Push(dir);
            while (st.Count > 0 && n < limit)
            {
                string d = st.Pop();
                try
                {
                    foreach (string f in Directory.GetFiles(d))
                    {
                        try { total += new FileInfo(f).Length; } catch { }
                        n++;
                        if (n >= limit) break;
                    }
                    foreach (string s in Directory.GetDirectories(d)) st.Push(s);
                }
                catch { }
            }
            return total;
        }

        /// <summary>目录/键名与软件名的匹配，返回命中原因，空串表示不匹配。</summary>
        private static string MatchName(string name, InstalledApp app)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string n = SoftwareInventory.Normalize(name);
            string an = app.Normalized;
            if (n.Length == 0) return "";

            if (an.Length >= 4 && n == an) return "名称完全一致";
            if (an.Length >= 7 && n.Length >= 7 && n.IndexOf(an, StringComparison.Ordinal) >= 0) return "名称包含软件名";

            foreach (string token in SoftwareInventory.Tokens(app.Name))
            {
                string tn = SoftwareInventory.Normalize(token);
                if (tn.Length < 4) continue;
                if (n == tn) return "名称包含关键词 " + token;
                if (n.Length >= tn.Length + 2 && n.IndexOf(tn, StringComparison.Ordinal) >= 0 && n.Length <= tn.Length + 8)
                    return "名称包含关键词 " + token;
            }
            return "";
        }

        // ---------------------------------------------------------------- 注册表

        private static void ScanRegistry(InstalledApp app, List<ResidueItem> items, HashSet<string> seen)
        {
            if (!string.IsNullOrEmpty(app.RegistryPath))
                Add(items, seen, "regkey", app.RegistryPath, "卸载信息注册表项（" + app.Hive + "）", 0, "Uninstall 键");

            ScanSoftwareHive(Registry.CurrentUser, "HKEY_CURRENT_USER\\Software", "Software", app, items, seen);
            ScanSoftwareHive(Registry.LocalMachine, "HKEY_LOCAL_MACHINE\\SOFTWARE", "Software", app, items, seen);
            ScanSoftwareHive(Registry.LocalMachine, "HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node",
                @"Software\WOW6432Node", app, items, seen);
        }

        private static void ScanSoftwareHive(RegistryKey hive, string displayRoot, string openPath, InstalledApp app,
            List<ResidueItem> items, HashSet<string> seen)
        {
            try
            {
                using (RegistryKey root = hive.OpenSubKey(openPath))
                {
                    if (root == null) return;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        string full = displayRoot + "\\" + name;
                        if (SafetyGuard.IsVendorRootName(name))
                        {
                            using (RegistryKey child = root.OpenSubKey(name))
                            {
                                if (child == null) continue;
                                foreach (string subName in child.GetSubKeyNames())
                                {
                                    string why2 = MatchName(subName, app);
                                    if (why2.Length > 0)
                                        Add(items, seen, "regkey", full + "\\" + subName,
                                            "厂商目录下的产品注册表键（" + why2 + "）", 0, why2);
                                }
                            }
                            continue;
                        }
                        string why = MatchName(name, app);
                        if (why.Length == 0) continue;
                        Add(items, seen, "regkey", full, "软件注册表主键（" + why + "）", 0, why);
                    }
                }
            }
            catch (Exception ex) { Log.Warn("扫描注册表 Software 失败：" + ex.Message); }
        }

        private static void ScanServices(InstalledApp app, List<ResidueItem> items, HashSet<string> seen)
        {
            try
            {
                using (RegistryKey services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                {
                    if (services == null) return;
                    foreach (string name in services.GetSubKeyNames())
                    {
                        using (RegistryKey k = services.OpenSubKey(name))
                        {
                            if (k == null) continue;
                            string image = SoftwareInventory.AsString(k.GetValue("ImagePath"));
                            if (image.Length == 0) continue;
                            string resolved = DriverInventory.ResolveImagePath(image);
                            string why = "";
                            if (SafetyGuard.IsSafeInstallRoot(app.InstallLocation) && !string.IsNullOrEmpty(resolved) &&
                                resolved.StartsWith(app.InstallLocation.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                                why = "服务程序位于安装目录";
                            else
                            {
                                string dn = SoftwareInventory.AsString(k.GetValue("DisplayName"));
                                if (dn.Length > 0) why = MatchName(dn, app);
                                if (why.Length > 0) why = "服务显示名匹配：" + why;
                            }
                            if (why.Length == 0) continue;

                            object typeObj = k.GetValue("Type");
                            int type = 0;
                            try { type = Convert.ToInt32(typeObj); } catch { }
                            string detail = type == 1 || type == 2 ? "内核驱动服务" : "系统服务";
                            Add(items, seen, "service", name, detail + " · " + resolved, 0, why);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn("扫描服务失败：" + ex.Message); }
        }

        private static void ScanScheduledTasks(InstalledApp app, List<ResidueItem> items, HashSet<string> seen)
        {
            string tasks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
            if (!Directory.Exists(tasks)) return;
            string loc = (app.InstallLocation ?? "").ToLowerInvariant();
            string exe = "";
            if (!string.IsNullOrEmpty(app.DisplayIcon)) exe = Path.GetFileName(app.DisplayIcon).ToLowerInvariant();

            WalkTasks(tasks, tasks, app, loc, exe, items, seen, 0);
        }

        private static void WalkTasks(string root, string dir, InstalledApp app, string loc, string exe,
            List<ResidueItem> items, HashSet<string> seen, int depth)
        {
            if (depth > 4) return;
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    string text = "";
                    try
                    {
                        using (StreamReader r = new StreamReader(f, Encoding.Unicode, true))
                            text = r.ReadToEnd();
                    }
                    catch { continue; }
                    if (text.Length == 0) continue;
                    string low = text.ToLowerInvariant();
                    bool hit = (!string.IsNullOrEmpty(loc) && low.IndexOf(loc, StringComparison.Ordinal) >= 0)
                        || (!string.IsNullOrEmpty(exe) && low.IndexOf(exe, StringComparison.Ordinal) >= 0
                            && low.IndexOf(SoftwareInventory.Normalize(app.Name), StringComparison.Ordinal) >= 0);
                    if (!hit) continue;

                    string taskName = f.Substring(root.Length).TrimStart('\\');
                    Add(items, seen, "task", taskName, "计划任务 XML 引用了该软件", 0, "任务定义匹配");
                }
                foreach (string d in Directory.GetDirectories(dir))
                    WalkTasks(root, d, app, loc, exe, items, seen, depth + 1);
            }
            catch (Exception ex) { Log.Warn("扫描计划任务失败：" + dir + " — " + ex.Message); }
        }

        private static void ScanRunKeys(InstalledApp app, List<ResidueItem> items, HashSet<string> seen)
        {
            string[] keys = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
            };
            foreach (RegistryKey hive in new RegistryKey[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (string key in keys)
                {
                    try
                    {
                        using (RegistryKey k = hive.OpenSubKey(key))
                        {
                            if (k == null) continue;
                            foreach (string v in k.GetValueNames())
                            {
                                string data = SoftwareInventory.AsString(k.GetValue(v));
                                if (data.Length == 0) continue;
                                bool hit = (!string.IsNullOrEmpty(app.InstallLocation) &&
                                    data.IndexOf(app.InstallLocation, StringComparison.OrdinalIgnoreCase) >= 0);
                                if (!hit && !string.IsNullOrEmpty(app.DisplayIcon))
                                    hit = data.IndexOf(app.DisplayIcon, StringComparison.OrdinalIgnoreCase) >= 0;
                                if (!hit) continue;

                                string full = (hive == Registry.CurrentUser ? "HKEY_CURRENT_USER\\" : "HKEY_LOCAL_MACHINE\\") + key;
                                ResidueItem it = new ResidueItem();
                                it.Kind = "regvalue";
                                it.Target = full;
                                it.ValueName = v;
                                it.Detail = "自启动项：" + v + " = " + data;
                                it.Evidence = "启动项指向安装路径";
                                it.Protected = SafetyGuard.IsUserWhitelisted(full);
                                string dedup = "regvalue|" + full + "|" + v;
                                if (seen.Add(dedup)) items.Add(it);
                            }
                        }
                    }
                    catch (Exception ex) { Log.Warn("扫描启动项失败：" + key + " — " + ex.Message); }
                }
            }
        }

        private static void ScanEnvironment(InstalledApp app, List<ResidueItem> items, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(app.InstallLocation)) return;
            string[] targets = new string[]
            {
                @"HKEY_CURRENT_USER\Environment",
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
            };
            foreach (string t in targets)
            {
                try
                {
                    RegistryKey hive = t.StartsWith("HKEY_CURRENT_USER") ? Registry.CurrentUser : Registry.LocalMachine;
                    string sub = t.StartsWith("HKEY_CURRENT_USER")
                        ? t.Substring("HKEY_CURRENT_USER\\".Length)
                        : t.Substring("HKEY_LOCAL_MACHINE\\".Length);
                    using (RegistryKey k = hive.OpenSubKey(sub))
                    {
                        if (k == null) continue;
                        foreach (string v in k.GetValueNames())
                        {
                            string data = SoftwareInventory.AsString(k.GetValue(v));
                            if (data.IndexOf(app.InstallLocation, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            ResidueItem it = new ResidueItem();
                            it.Kind = "env";
                            it.Target = t;
                            it.ValueName = v;
                            it.PathFragment = app.InstallLocation;
                            it.Detail = "环境变量 " + v + " = " + data;
                            it.Evidence = "PATH 类变量引用安装目录";
                            it.Protected = SafetyGuard.IsUserWhitelisted(t);
                            string dedup = "env|" + t + "|" + v;
                            if (seen.Add(dedup)) items.Add(it);
                        }
                    }
                }
                catch (Exception ex) { Log.Warn("扫描环境变量失败：" + ex.Message); }
            }
        }

        private static void ScanSharedDlls(InstalledApp app, List<ResidueItem> items, HashSet<string> seen)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs"))
                {
                    if (k == null || string.IsNullOrEmpty(app.InstallLocation)) return;
                    foreach (string v in k.GetValueNames())
                    {
                        if (v.StartsWith(app.InstallLocation + "\\", StringComparison.OrdinalIgnoreCase))
                        {
                            ResidueItem it = new ResidueItem();
                            it.Kind = "regvalue";
                            it.Target = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
                            it.ValueName = v;
                            it.Detail = "共享 DLL 计数： " + Path.GetFileName(v);
                            it.Evidence = "SharedDLLs 引用安装目录文件";
                            string dedup = "regvalue|shareddlls|" + v;
                            if (seen.Add(dedup)) items.Add(it);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn("扫描 SharedDLLs 失败：" + ex.Message); }
        }

        // ---------------------------------------------------------------- 快捷方式

        private static void ScanShortcuts(InstalledApp app, List<ResidueItem> items, HashSet<string> seen)
        {
            List<string> folders = new List<string>();
            folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
            folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
            folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch"));

            object shell = null;
            try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); }
            catch { }

            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                WalkShortcuts(app, folder, shell, items, seen, 0);
            }
        }

        private static void WalkShortcuts(InstalledApp app, string dir, object shell,
            List<ResidueItem> items, HashSet<string> seen, int depth)
        {
            if (depth > 3) return;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.lnk"))
                {
                    string target = ResolveShortcut(shell, f);
                    if (string.IsNullOrEmpty(target)) continue;
                    bool hit = (!string.IsNullOrEmpty(app.InstallLocation) &&
                        target.StartsWith(app.InstallLocation.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
                    if (!hit && !string.IsNullOrEmpty(app.DisplayIcon))
                        hit = target.Equals(app.DisplayIcon, StringComparison.OrdinalIgnoreCase);
                    if (!hit) continue;

                    long size = 0;
                    try { size = new FileInfo(f).Length; } catch { }
                    Add(items, seen, "shortcut", f, "快捷方式 → " + target, size, "快捷方式目标位于安装目录");
                }
                foreach (string d in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(d);
                    if (string.Equals(name, "Roaming", StringComparison.OrdinalIgnoreCase) && depth > 0) continue;
                    WalkShortcuts(app, d, shell, items, seen, depth + 1);
                }
            }
            catch (Exception ex) { Log.Warn("扫描快捷方式失败：" + dir + " — " + ex.Message); }
        }

        private static string ResolveShortcut(object shell, string lnk)
        {
            if (shell == null) return "";
            try
            {
                Type t = shell.GetType();
                object shortcut = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type st = shortcut.GetType();
                return (string)st.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
            }
            catch { return ""; }
        }

        // ---------------------------------------------------------------- COM

        private static void ScanCom(InstalledApp app, List<ResidueItem> items, HashSet<string> seen,
            Action<string> progress)
        {
            if (string.IsNullOrEmpty(app.InstallLocation)) return;
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                using (RegistryKey clsid = Registry.ClassesRoot.OpenSubKey("CLSID"))
                {
                    if (clsid != null)
                    {
                        foreach (string guid in clsid.GetSubKeyNames())
                        {
                            if (sw.ElapsedMilliseconds > 12000) break;
                            string path = ReadComServer(clsid, guid);
                            if (path.Length == 0) continue;
                            if (path.IndexOf(app.InstallLocation, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            string full = @"HKEY_CLASSES_ROOT\CLSID\" + guid;
                            Add(items, seen, "com", full, "COM 组件服务器：" + path, 0, "InprocServer32 指向安装目录");
                            if (progress != null && items.Count % 20 == 0) progress("已发现 " + items.Count + " 项…");
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn("扫描 COM CLSID 失败：" + ex.Message); }

            // 右键菜单扩展（*\shellex、Directory\shellex 等）
            string[] shellRoots = new string[]
            {
                @"*\shellex\ContextMenuHandlers",
                @"AllFilesystemObjects\shellex\ContextMenuHandlers",
                @"Directory\shellex\ContextMenuHandlers",
                @"Directory\Background\shellex\ContextMenuHandlers",
                @"Folder\shellex\ContextMenuHandlers",
                @"Drive\shellex\ContextMenuHandlers"
            };
            foreach (string sr in shellRoots)
            {
                try
                {
                    using (RegistryKey h = Registry.ClassesRoot.OpenSubKey(sr))
                    {
                        if (h == null) continue;
                        foreach (string handler in h.GetSubKeyNames())
                        {
                            using (RegistryKey hk = h.OpenSubKey(handler))
                            {
                                if (hk == null) continue;
                                string guid = SoftwareInventory.AsString(hk.GetValue(""));
                                if (guid.Length == 0) continue;
                                string path = ReadComServerGuarded(guid);
                                if (path.Length == 0) continue;
                                if (path.IndexOf(app.InstallLocation, StringComparison.OrdinalIgnoreCase) < 0) continue;
                                string full = @"HKEY_CLASSES_ROOT\" + sr + @"\" + handler;
                                Add(items, seen, "com", full, "右键菜单扩展：" + handler + " → " + path, 0, "Shell 扩展指向安装目录");
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static string ReadComServer(RegistryKey clsidRoot, string guid)
        {
            try
            {
                using (RegistryKey k = clsidRoot.OpenSubKey(guid))
                {
                    if (k == null) return "";
                    string v = ReadDefault(k, "InprocServer32");
                    if (v.Length > 0) return Environment.ExpandEnvironmentVariables(v.Trim('"'));
                    v = ReadDefault(k, "LocalServer32");
                    if (v.Length > 0) return Environment.ExpandEnvironmentVariables(v.Trim('"'));
                }
            }
            catch { }
            return "";
        }

        private static string ReadComServerGuarded(string guid)
        {
            try
            {
                using (RegistryKey k = Registry.ClassesRoot.OpenSubKey("CLSID\\" + guid))
                {
                    if (k == null) return "";
                    string v = ReadDefault(k, "InprocServer32");
                    if (v.Length > 0) return Environment.ExpandEnvironmentVariables(v.Trim('"'));
                    v = ReadDefault(k, "LocalServer32");
                    if (v.Length > 0) return Environment.ExpandEnvironmentVariables(v.Trim('"'));
                }
            }
            catch { }
            return "";
        }

        private static string ReadDefault(RegistryKey parent, string sub)
        {
            using (RegistryKey k = parent.OpenSubKey(sub))
            {
                if (k == null) return "";
                return SoftwareInventory.AsString(k.GetValue(""));
            }
        }
    }
}




