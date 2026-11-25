using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MetroUninstaller
{
    /// <summary>内核驱动清单、签名校验与四维归属匹配。</summary>
    public static class DriverInventory
    {
        public static List<DriverInfo> LoadAll(Action<string> progress)
        {
            List<DriverInfo> list = new List<DriverInfo>();
            Dictionary<string, string> states = LoadStates();

            try
            {
                using (RegistryKey services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                {
                    if (services == null) return list;
                    foreach (string name in services.GetSubKeyNames())
                    {
                        using (RegistryKey k = services.OpenSubKey(name))
                        {
                            if (k == null) continue;
                            int type = ReadInt(k, "Type", -1);
                            if (type != 1 && type != 2) continue;

                            DriverInfo d = new DriverInfo();
                            d.ServiceName = name;
                            d.Type = type;
                            d.StartType = ReadInt(k, "Start", 4);
                            d.DisplayName = SoftwareInventory.AsString(k.GetValue("DisplayName"));
                            d.Description = SoftwareInventory.AsString(k.GetValue("Description"));
                            d.ImagePathRaw = SoftwareInventory.AsString(k.GetValue("ImagePath"));
                            d.ResolvedPath = ResolveImagePath(d.ImagePathRaw);
                            d.State = states.ContainsKey(name.ToLowerInvariant()) ? states[name.ToLowerInvariant()] : "stopped";
                            d.ParametersText = ReadParameters(k);

                            if (!string.IsNullOrEmpty(d.DisplayName) && d.DisplayName.StartsWith("@", StringComparison.Ordinal))
                                d.DisplayName = ""; // 资源字符串，未能解析时留给服务名
                            if (d.DisplayName.Length == 0) d.DisplayName = name;

                            list.Add(d);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取 Services 失败：" + ex.Message);
            }

            if (progress != null) progress("已枚举 " + list.Count + " 个内核驱动服务，校验签名…");

            ParallelOptions po = new ParallelOptions();
            po.MaxDegreeOfParallelism = 4;
            Parallel.For(0, list.Count, po, delegate (int i)
            {
                DriverInfo d = list[i];
                SignatureInfo sig = SignatureVerifier.Check(d.ResolvedPath);
                d.SignatureState = sig.State;
                d.Signer = sig.Signer;
                d.MicrosoftSigned = sig.MicrosoftSigned;
                d.IsProtected = SafetyGuard.IsDriverProtected(d);
                d.Suspicious = (sig.State == "unsigned" || sig.State == "invalid")
                    && !IsTrustedLocation(d.ResolvedPath);
            });

            if (progress != null) progress("驱动清单就绪：" + list.Count + " 项");
            return list;
        }

        private static bool IsTrustedLocation(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            string low = path.ToLowerInvariant();
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
            return low.StartsWith(win + "\\") || low.IndexOf(@"\driverstore\", StringComparison.Ordinal) >= 0;
        }

        private static int ReadInt(RegistryKey k, string name, int def)
        {
            try
            {
                object o = k.GetValue(name);
                if (o == null) return def;
                return Convert.ToInt32(o);
            }
            catch { return def; }
        }

        private static string ReadParameters(RegistryKey k)
        {
            try
            {
                using (RegistryKey p = k.OpenSubKey("Parameters"))
                {
                    if (p == null) return "";
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    foreach (string v in p.GetValueNames())
                    {
                        object o = p.GetValue(v);
                        if (o == null) continue;
                        sb.Append(v).Append('=').Append(o.ToString()).Append(' ');
                    }
                    return sb.ToString();
                }
            }
            catch { return ""; }
        }

        private static Dictionary<string, string> LoadStates()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT Name, State, Started FROM Win32_SystemDriver"))
                using (ManagementObjectCollection rows = s.Get())
                {
                    foreach (ManagementObject mo in rows)
                    {
                        try
                        {
                            string name = Convert.ToString(mo["Name"]);
                            string state = Convert.ToString(mo["State"]);
                            bool started = false;
                            try { started = Convert.ToBoolean(mo["Started"]); } catch { }
                            map[name] = (started ? "运行中 · " : "") + (state ?? "unknown");
                        }
                        catch { }
                        finally { mo.Dispose(); }
                    }
                }
            }
            catch (Exception ex) { Log.Warn("读取驱动运行状态失败：" + ex.Message); }
            return map;
        }

        public static string ResolveImagePath(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw.Trim().Trim('"');
            if (s.StartsWith("\\??\\", StringComparison.Ordinal)) s = s.Substring(4);
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (s.StartsWith("\\SystemRoot\\", StringComparison.OrdinalIgnoreCase))
                s = Path.Combine(win, s.Substring("\\SystemRoot\\".Length));
            else if (s.StartsWith("System32\\", StringComparison.OrdinalIgnoreCase))
                s = Path.Combine(win, s);
            s = Environment.ExpandEnvironmentVariables(s);
            if (!Path.IsPathRooted(s))
                s = Path.Combine(Path.Combine(win, @"System32\drivers"), s);
            return s;
        }

        /// <summary>四维归属评分：路径、签名、服务名、注册表链路。返回 0-100。</summary>
        public static int Attribute(DriverInfo d, InstalledApp app, List<string> evidence)
        {
            if (d == null || app == null) return 0;
            int score = 0;
            string loc = (app.InstallLocation ?? "").TrimEnd('\\');
            string path = d.ResolvedPath ?? "";
            string appNorm = app.Normalized;

            if (loc.Length > 3 && path.Length > 0 &&
                path.StartsWith(loc + "\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 55;
                if (evidence != null) evidence.Add("驱动文件位于安装目录");
            }
            else if (!string.IsNullOrEmpty(app.DisplayIcon))
            {
                string iconDir = Path.GetDirectoryName(app.DisplayIcon);
                if (iconDir != null && path.Length > 0 &&
                    path.StartsWith(iconDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    score += 40;
                    if (evidence != null) evidence.Add("驱动文件位于主程序目录");
                }
            }

            string meta = (d.ServiceName + " " + d.DisplayName + " " + d.Description).ToLowerInvariant();
            if (appNorm.Length >= 5 && SoftwareInventory.Normalize(meta).IndexOf(appNorm, StringComparison.Ordinal) >= 0)
            {
                score += 18;
                if (evidence != null) evidence.Add("服务名/描述包含软件名");
            }

            if (!string.IsNullOrEmpty(d.Signer) && !string.IsNullOrEmpty(app.Publisher))
            {
                string pub = SoftwareInventory.Normalize(app.Publisher);
                string signer = SoftwareInventory.Normalize(d.Signer);
                if (pub.Length >= 4 && signer.Length >= 4 &&
                    (signer.IndexOf(pub, StringComparison.Ordinal) >= 0 || pub.IndexOf(signer, StringComparison.Ordinal) >= 0))
                {
                    score += 20;
                    if (evidence != null) evidence.Add("数字签名主体与发布者一致（" + d.Signer + "）");
                }
            }

            if (loc.Length > 3 && !string.IsNullOrEmpty(d.ParametersText) &&
                d.ParametersText.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 15;
                if (evidence != null) evidence.Add("服务 Parameters 引用安装目录");
            }

            if (appNorm.Length >= 6 && d.ServiceName.Length > 0)
            {
                string svcNorm = SoftwareInventory.Normalize(d.ServiceName);
                if (svcNorm.IndexOf(appNorm, StringComparison.Ordinal) >= 0) score += 8;
            }

            return Math.Min(100, score);
        }

        public static void AttributeAll(List<DriverInfo> drivers, List<InstalledApp> apps)
        {
            foreach (DriverInfo d in drivers)
            {
                int best = 0;
                InstalledApp bestApp = null;
                foreach (InstalledApp app in apps)
                {
                    List<string> ev = new List<string>();
                    int s = Attribute(d, app, ev);
                    if (s > best)
                    {
                        best = s;
                        bestApp = app;
                        d.Evidence = ev;
                    }
                }
                d.Attribution = best;
                if (bestApp != null && best >= 30) d.OwnerSoftware = bestApp.Name;
                if (d.IsProtected && best >= 45) d.OwnerSoftware = bestApp.Name;
            }
        }

        /// <summary>可进入「待删除」列表的驱动：明确归属且不在保护范围内。</summary>
        public static bool IsDeletable(DriverInfo d)
        {
            return d != null && !d.IsProtected && d.Attribution >= 45;
        }
    }
}


