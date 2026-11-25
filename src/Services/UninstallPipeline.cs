using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace MetroUninstaller
{
    /// <summary>一键全流程：终止进程 → 卸载主程序 → 备份 → 清理残留 → 清理驱动 → 报告。</summary>
    public static class UninstallPipeline
    {
        public static CleanReport Run(InstalledApp app, List<ResidueItem> items,
            List<DriverInfo> drivers, bool runUninstaller, Action<string> uiLog)
        {
            CleanReport r = new CleanReport();
            r.AppName = app.Name;
            r.AppPublisher = app.Publisher;
            r.Elevated = Admin.IsAdmin;
            r.Items = items;
            r.Drivers = drivers;

            Action<string> L = delegate (string m)
            {
                r.Steps.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + m);
                if (m.StartsWith("错误")) Log.Error(m);
                else Log.Info(m);
                if (uiLog != null)
                {
                    try { uiLog(m); } catch { }
                }
            };

            L("开始一键清理：" + app.Name + "（" + (app.Version.Length > 0 ? app.Version : "版本未知") + "）");
            if (!Admin.IsAdmin) L("注意：当前为标准权限，驱动清理与系统还原点不可用。");

            // 1. 还原点
            if (Config.CreateRestorePoint && Admin.IsAdmin)
            {
                L("正在创建系统还原点…");
                string err = RestorePointService.Create("CHESUN Uninstaller: 卸载 " + app.Name);
                if (err.Length == 0) r.RestorePoint = "已创建（卸载 " + app.Name + "）";
                else { r.RestorePoint = "失败：" + err; L("警告：还原点未创建 — " + err); }
            }
            else if (Config.CreateRestorePoint && !Admin.IsAdmin)
            {
                L("跳过还原点：需要管理员权限。");
            }

            // 2. 终止进程
            L("正在扫描关联进程…");
            List<ProcessHit> procs = ProcessTools.FindRelated(app);
            if (procs.Count == 0) L("未发现关联进程。");
            else
            {
                L("发现 " + procs.Count + " 个关联进程，正在终止…");
                ProcessTools.KillAll(procs, L);
            }
            Thread.Sleep(500);

            // 3. 卸载主程序
            if (runUninstaller)
            {
                L("正在执行卸载程序…");
                RunUninstaller(app, L);
            }
            else L("已跳过卸载程序，直接进入残留清理。");

            // 4. 备份
            BackupSession session = null;
            if (Config.BackupBeforeDelete)
            {
                session = BackupService.Begin(app.Name);
                r.BackupPath = session.Directory;
                L("已建立备份目录：" + session.Directory);
            }

            // 5. 残留清理
            int total = 0;
            foreach (ResidueItem it in items) if (!it.Protected && it.Selected) total++;
            L("开始清理残留项，共 " + total + " 项…");

            int index = 0;
            foreach (ResidueItem it in OrderItems(items))
            {
                if (it.Protected || !it.Selected) continue;
                index++;
                if (it.RestartRequired) r.PendingRestart++;
                if (session != null) BackupService.BackupItem(session, it, null);

                if (DeleteItem(it, L)) { CountDeleted(it, r); }
                else if (it.RestartRequired) { r.PendingRestart++; }
                else r.Failed++;

                if (index % 50 == 0) L("已处理 " + index + " / " + total + " 项…");
            }

            // 6. 驱动清理
            if (drivers != null && drivers.Count > 0)
            {
                if (!Admin.IsAdmin)
                {
                    L("跳过驱动清理：需要管理员权限。");
                }
                else
                {
                    L("开始清理 " + drivers.Count + " 个内核驱动…");
                    foreach (DriverInfo d in drivers)
                    {
                        if (d.IsProtected)
                        {
                            L("跳过受保护驱动 " + d.ServiceName);
                            continue;
                        }
                        if (!DriverInventory.IsDeletable(d))
                        {
                            L("跳过归属不明确的驱动 " + d.ServiceName + "（归属分 " + d.Attribution + "）");
                            continue;
                        }
                        if (session != null)
                        {
                            ResidueItem fi = new ResidueItem();
                            fi.Kind = "file";
                            fi.Target = d.ResolvedPath;
                            BackupService.BackupItem(session, fi, null);
                            BackupService.BackupItem(session, MakeServiceItem(d.ServiceName), null);
                        }
                        if (DeleteDriver(d, L)) r.DeletedServices++;
                        else r.Failed++;
                    }
                }
            }

            // 7. 收尾
            if (session != null)
            {
                BackupService.WriteManifest(session);
                L("备份完成：文件 " + session.Entries.Count + " 条记录，占用 " + Ui.FormatSize(session.Bytes) +
                    (session.Skipped > 0 ? "，跳过 " + session.Skipped + " 项" : ""));
            }
            r.PendingRestart = Math.Max(r.PendingRestart, PendingDelete.CountPending());
            r.Finished = DateTime.Now;
            r.Success = r.Failed == 0;
            L("清理结束：" + (r.Success ? "成功" : "存在失败项") + "，删除 " +
                (r.DeletedFiles + r.DeletedRegistry + r.DeletedServices + r.DeletedTasks) + " 项。");

            try
            {
                ReportWriter.Write(r);
                L("报告已生成：" + r.ReportPath);
            }
            catch (Exception ex) { L("错误：报告生成失败 " + ex.Message); }

            return r;
        }

        private static ResidueItem MakeServiceItem(string name)
        {
            ResidueItem it = new ResidueItem();
            it.Kind = "service";
            it.Target = name;
            return it;
        }

        /// <summary>独立驱动清理（驱动管理页使用）。</summary>
        public static CleanReport CleanDrivers(List<DriverInfo> drivers, Action<string> uiLog)
        {
            CleanReport r = new CleanReport();
            r.AppName = "内核驱动清理";
            r.Elevated = Admin.IsAdmin;
            r.Drivers = drivers;

            Action<string> L = delegate (string m)
            {
                r.Steps.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + m);
                Log.Info(m);
                if (uiLog != null) { try { uiLog(m); } catch { } }
            };

            L("开始驱动清理：" + drivers.Count + " 个目标");
            if (!Admin.IsAdmin) L("注意：当前为标准权限，服务删除可能失败。");

            if (Config.CreateRestorePoint && Admin.IsAdmin)
            {
                L("正在创建系统还原点…");
                string err = RestorePointService.Create("CHESUN Uninstaller: 驱动清理");
                if (err.Length == 0) r.RestorePoint = "已创建（驱动清理）";
                else { r.RestorePoint = "失败：" + err; L("警告：还原点未创建 — " + err); }
            }

            BackupSession session = null;
            if (Config.BackupBeforeDelete)
            {
                session = BackupService.Begin("驱动清理");
                r.BackupPath = session.Directory;
            }

            foreach (DriverInfo d in drivers)
            {
                if (d.IsProtected)
                {
                    L("跳过受保护驱动 " + d.ServiceName);
                    continue;
                }
                if (!DriverInventory.IsDeletable(d))
                {
                    L("跳过归属不明确的驱动 " + d.ServiceName + "（归属分 " + d.Attribution + "）");
                    continue;
                }
                if (session != null)
                {
                    ResidueItem fi = new ResidueItem();
                    fi.Kind = "file";
                    fi.Target = d.ResolvedPath;
                    BackupService.BackupItem(session, fi, null);
                    BackupService.BackupItem(session, MakeServiceItem(d.ServiceName), null);
                }
                if (DeleteDriver(d, L)) r.DeletedServices++;
                else r.Failed++;
            }

            if (session != null)
            {
                BackupService.WriteManifest(session);
                L("备份完成：" + session.Entries.Count + " 条记录，占用 " + Ui.FormatSize(session.Bytes));
            }

            r.PendingRestart = PendingDelete.CountPending();
            r.Finished = DateTime.Now;
            r.Success = r.Failed == 0;
            L("驱动清理结束：" + (r.Success ? "成功" : "存在失败项"));
            try
            {
                ReportWriter.Write(r);
                L("报告已生成：" + r.ReportPath);
            }
            catch (Exception ex) { L("错误：报告生成失败 " + ex.Message); }
            return r;
        }

        private static void CountDeleted(ResidueItem it, CleanReport r)
        {
            switch (it.Kind)
            {
                case "file": r.DeletedFiles++; break;
                case "dir": r.DeletedFiles++; break;
                case "shortcut": r.DeletedFiles++; break;
                case "regkey": r.DeletedRegistry++; break;
                case "regvalue": r.DeletedRegistry++; break;
                case "env": r.DeletedRegistry++; break;
                case "com": r.DeletedRegistry++; break;
                case "service": r.DeletedServices++; break;
                case "task": r.DeletedTasks++; break;
                default: r.DeletedFiles++; break;
            }
        }

        private static bool DeleteItem(ResidueItem it, Action<string> L)
        {
            try
            {
                switch (it.Kind)
                {
                    case "file":
                    case "shortcut":
                        if (!File.Exists(it.Target)) return true;
                        try { File.SetAttributes(it.Target, FileAttributes.Normal); } catch { }
                        try
                        {
                            File.Delete(it.Target);
                            L("已删除文件 " + it.Target);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            if (Admin.IsAdmin)
                            {
                                L("删除被拒绝，正在接管所有权后重试：" + it.Target);
                                TryTakeOwnership(it.Target, false);
                                try
                                {
                                    File.Delete(it.Target);
                                    L("已删除文件 " + it.Target + "（接管所有权后）");
                                    return true;
                                }
                                catch { }
                            }
                            it.RestartRequired = true;
                            if (PendingDelete.Schedule(it.Target))
                            {
                                L("文件被占用，已登记重启后删除 " + it.Target);
                                return true;
                            }
                            L("错误：无法删除文件 " + it.Target + " — " + ex.Message);
                            return false;
                        }

                    case "dir":
                        if (!Directory.Exists(it.Target)) return true;
                        try
                        {
                            Directory.Delete(it.Target, true);
                            L("已删除目录 " + it.Target);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            if (Admin.IsAdmin)
                            {
                                L("目录删除被拒绝，正在接管所有权后重试：" + it.Target);
                                ClearReadOnly(it.Target);
                                TryTakeOwnership(it.Target, true);
                                try
                                {
                                    Directory.Delete(it.Target, true);
                                    L("已删除目录 " + it.Target + "（接管所有权后）");
                                    return true;
                                }
                                catch { }
                            }
                            int pending = ScheduleDirectoryLeftovers(it.Target);
                            if (pending > 0)
                            {
                                L("目录未能立即删除（" + ex.Message + "），已登记 " + pending + " 个文件重启后删除。");
                                return true;
                            }
                            L("错误：无法删除目录 " + it.Target + " — " + ex.Message);
                            return false;
                        }

                    case "regkey":
                    case "com":
                        if (!Reg.Exists(it.Target)) return true;
                        return Reg.DeleteKey(it.Target, L);

                    case "regvalue":
                        return Reg.DeleteValue(it.Target, it.ValueName, L);

                    case "env":
                        return CleanEnvironment(it, L);

                    case "service":
                        return DeleteService(it.Target, L);

                    case "task":
                        {
                            Executor.Run("schtasks.exe", "/delete /f /tn \"" + it.Target + "\"", 60000, null);
                            L("已删除计划任务 " + it.Target);
                            return true;
                        }
                }
            }
            catch (Exception ex)
            {
                L("错误：处理失败 " + it.Target + " — " + ex.Message);
                return false;
            }
            return false;
        }

        /// <summary>只移除环境变量中指向安装目录的片段，绝不清空整条 PATH。</summary>
        private static bool CleanEnvironment(ResidueItem it, Action<string> L)
        {
            try
            {
                string sub;
                RegistryKey hive = Reg.Hive(it.Target, out sub);
                using (RegistryKey k = hive.OpenSubKey(sub, true))
                {
                    if (k == null) return true;
                    string raw = Convert.ToString(k.GetValue(it.ValueName, ""));
                    if (raw.Length == 0) return true;

                    List<string> keep = new List<string>();
                    int removed = 0;
                    foreach (string seg in raw.Split(';'))
                    {
                        string s = seg.Trim().Trim('"');
                        if (it.PathFragment.Length > 2 && s.StartsWith(it.PathFragment, StringComparison.OrdinalIgnoreCase))
                        {
                            removed++;
                            continue;
                        }
                        keep.Add(seg);
                    }
                    if (removed == 0) return true;

                    if (keep.Count == 0)
                    {
                        k.DeleteValue(it.ValueName, false);
                        L("已删除环境变量 " + it.ValueName + "（原值仅包含目标路径）");
                        return true;
                    }
                    RegistryValueKind kind = RegistryValueKind.String;
                    try { kind = k.GetValueKind(it.ValueName); } catch { }
                    if (kind != RegistryValueKind.ExpandString) kind = RegistryValueKind.String;
                    k.SetValue(it.ValueName, string.Join(";", keep.ToArray()), kind);
                    L("已从环境变量 " + it.ValueName + " 中移除 " + removed + " 个目标路径片段");
                    return true;
                }
            }
            catch (Exception ex)
            {
                L("错误：清理环境变量失败 " + it.Target + " — " + ex.Message);
                return false;
            }
        }

        private static int ScheduleDirectoryLeftovers(string dir)
        {
            int n = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    if (PendingDelete.Schedule(f)) n++;
                foreach (string d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (Directory.GetFileSystemEntries(d).Length == 0)
                        {
                            if (PendingDelete.Schedule(d.TrimEnd('\\'))) n++;
                        }
                    }
                    catch { }
                }
                PendingDelete.Schedule(dir.TrimEnd('\\'));
            }
            catch { }
            return n;
        }

        /// <summary>查询服务状态，返回 RUNNING / STOPPED / STOP_PENDING / NOT_FOUND / UNKNOWN。</summary>
        private static string QueryServiceState(string name)
        {
            ExecResult r = Executor.Run("sc.exe", "query \"" + name + "\"", 30000, null);
            string o = (r.Output ?? "").ToUpperInvariant();
            if (o.IndexOf("1060", StringComparison.Ordinal) >= 0) return "NOT_FOUND";
            if (o.IndexOf("STOP_PENDING", StringComparison.Ordinal) >= 0) return "STOP_PENDING";
            if (o.IndexOf("START_PENDING", StringComparison.Ordinal) >= 0) return "START_PENDING";
            if (o.IndexOf("RUNNING", StringComparison.Ordinal) >= 0) return "RUNNING";
            if (o.IndexOf("STOPPED", StringComparison.Ordinal) >= 0) return "STOPPED";
            return "UNKNOWN";
        }

        /// <summary>把服务启动类型改成 disabled，防止被外部拉起或重启后再次加载。</summary>
        private static void DisableServiceStart(string name, Action<string> L)
        {
            ExecResult r = Executor.Run("sc.exe", "config \"" + name + "\" start= disabled", 30000, null);
            if (r.ExitCode == 0) L("已将 " + name + " 的启动类型设为 disabled");
        }

        /// <summary>尽力停止服务；驱动不支持 unload 时返回 false，由调用方转入重启删除。</summary>
        private static bool StopServiceHard(string name, Action<string> L)
        {
            string state = QueryServiceState(name);
            if (state == "NOT_FOUND" || state == "STOPPED") return true;
            for (int i = 0; i < 3; i++)
            {
                Executor.Run("sc.exe", "stop \"" + name + "\"", 45000, null);
                for (int w = 0; w < 20; w++)
                {
                    Thread.Sleep(1000);
                    state = QueryServiceState(name);
                    if (state == "STOPPED" || state == "NOT_FOUND") return true;
                    if (state == "RUNNING" && w >= 4) break;
                }
            }
            L("驱动 " + name + " 仍在运行（内核对象未释放），无法热卸载，转入重启删除。");
            return false;
        }

        private static bool DeleteService(string name, Action<string> L)
        {
            DisableServiceStart(name, L);
            StopServiceHard(name, L);
            ExecResult r = Executor.Run("sc.exe", "delete \"" + name + "\"", 45000, null);
            string output = (r.Output ?? "").Trim();
            bool ok = r.ExitCode == 0
                || output.IndexOf("SUCCESS", StringComparison.OrdinalIgnoreCase) >= 0
                || output.IndexOf("成功", StringComparison.Ordinal) >= 0
                || output.IndexOf("1060", StringComparison.Ordinal) >= 0   // 服务不存在
                || output.IndexOf("1072", StringComparison.Ordinal) >= 0;  // 已标记删除
            if (ok) L("已删除驱动服务 " + name + (output.IndexOf("1072", StringComparison.Ordinal) >= 0 ? "（已标记重启后删除）" : ""));
            else L("错误：删除服务失败 " + name + " — " + output);
            return ok;
        }

        private static bool DeleteDriver(DriverInfo d, Action<string> L)
        {
            L("强制移除驱动 " + d.ServiceName + "（" + d.Resolution() + "）");
            if (d.State.IndexOf("运行", StringComparison.Ordinal) >= 0 ||
                d.State.IndexOf("RUN", StringComparison.OrdinalIgnoreCase) >= 0)
                L("检测到该驱动正在内核中加载，先禁用启动并尝试热停止；不支持 unload 的驱动将在重启时完成文件删除。");

            bool serviceOk = DeleteService(d.ServiceName, L);
            if (!serviceOk && Admin.IsAdmin)
            {
                string so;
                if (SystemRunner.DeleteServiceAsSystem(d.ServiceName, out so))
                    L("已由 SYSTEM 权限删除服务 " + d.ServiceName + " — " + so);
            }

            bool fileOk = true;
            if (!string.IsNullOrEmpty(d.ResolvedPath) && File.Exists(d.ResolvedPath))
            {
                try
                {
                    File.SetAttributes(d.ResolvedPath, FileAttributes.Normal);
                    File.Delete(d.ResolvedPath);
                    L("已删除驱动文件 " + d.ResolvedPath);
                }
                catch (Exception ex)
                {
                    L("直接删除被拒绝（" + ex.Message + "），改用 SYSTEM 接管所有权…");
                    if (Admin.IsAdmin && SystemRunner.ForceDelete(d.ResolvedPath, false))
                    {
                        L("已由 SYSTEM 强制删除驱动文件 " + d.ResolvedPath);
                    }
                    else if (PendingDelete.Schedule(d.ResolvedPath))
                        L("驱动文件仍被内核占用，已登记重启后删除 " + d.ResolvedPath);
                    else { fileOk = false; L("错误：驱动文件删除失败 " + d.ResolvedPath); }
                }
            }

            // 清掉 DriverStore 里的驱动包，避免设备管理器再次自动装回
            if (!string.IsNullOrEmpty(d.ResolvedPath))
            {
                string inf = DriverPackageInf(d.ResolvedPath);
                if (inf.Length > 0 && Admin.IsAdmin)
                {
                    string po;
                    if (SystemRunner.RemoveDriverPackage(inf, out po))
                        L("已移除 DriverStore 驱动包 " + inf);
                    else L("驱动包移除未完成（可能被设备占用，重启后再试）：" + inf);
                }
            }

            Reg.DeleteKey(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" + d.ServiceName, null);
            if (Reg.Exists(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" + d.ServiceName) && Admin.IsAdmin)
            {
                string regOut;
                SystemRunner.Run("reg delete \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\" + d.ServiceName + "\" /f", 60000, out regOut);
            }

            return serviceOk || fileOk;
        }

        /// <summary>从 DriverStore 路径推出 oemXX.inf 名称。</summary>
        private static string DriverPackageInf(string path)
        {
            try
            {
                string low = path.ToLowerInvariant();
                int idx = low.IndexOf("\\driverstore\\filerepository\\", StringComparison.Ordinal);
                if (idx < 0) return "";
                string folder = path.Substring(idx + "\\driverstore\\filerepository\\".Length);
                int sep = folder.IndexOf('\\');
                if (sep > 0) folder = folder.Substring(0, sep);
                int us = folder.IndexOf("_amd64_", StringComparison.OrdinalIgnoreCase);
                if (us < 0) us = folder.IndexOf("_x86_", StringComparison.OrdinalIgnoreCase);
                if (us > 0) folder = folder.Substring(0, us);
                return folder.ToLowerInvariant().EndsWith(".inf") ? folder : "";
            }
            catch { return ""; }
        }

        /// <summary>服务/注册表先处理，文件次之，目录最后，避免目录把子项提前锁死。</summary>
        private static List<ResidueItem> OrderItems(List<ResidueItem> items)
        {
            List<ResidueItem> first = new List<ResidueItem>();
            List<ResidueItem> mid = new List<ResidueItem>();
            List<ResidueItem> last = new List<ResidueItem>();
            foreach (ResidueItem it in items)
            {
                if (it.Kind == "service" || it.Kind == "task") first.Add(it);
                else if (it.Kind == "dir") last.Add(it);
                else mid.Add(it);
            }
            List<ResidueItem> all = new List<ResidueItem>();
            all.AddRange(first);
            all.AddRange(mid);
            all.AddRange(last);
            return all;
        }

        /// <summary>用 takeown + icacls 接管目标所有权，解决 TrustedInstaller / 系统 ACL 导致的拒绝访问。</summary>
        private static void TryTakeOwnership(string path, bool directory)
        {
            try
            {
                string p = "\"" + path + "\"";
                string take = directory ? ("/f " + p + " /r /d y") : ("/f " + p);
                Executor.Run("takeown.exe", take, 120000, null);
                string grant = p + " /grant *S-1-5-32-544:F" + (directory ? " /t /c /q" : " /c /q");
                Executor.Run("icacls.exe", grant, 120000, null);
            }
            catch { }
        }

        private static void ClearReadOnly(string dir)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                }
            }
            catch { }
        }

        private static ExecResult RunInteractive(string exe, string args, int timeoutMs)
        {
            ExecResult r = new ExecResult();
            try
            {
                ProcessStartInfo si = new ProcessStartInfo(exe, args);
                si.UseShellExecute = true;
                using (Process p = Process.Start(si))
                {
                    r.Started = true;
                    if (!p.WaitForExit(timeoutMs))
                    {
                        r.TimedOut = true;
                        try { p.Kill(); } catch { }
                    }
                    else r.ExitCode = p.ExitCode;
                }
            }
            catch (Exception ex) { r.Output = ex.Message; }
            return r;
        }

        private static void RunUninstaller(InstalledApp app, Action<string> L)
        {
            string cmd = (app.QuietUninstallString ?? "").Trim();
            bool quiet = cmd.Length > 0;
            if (!quiet) cmd = (app.UninstallString ?? "").Trim();

            if (cmd.Length == 0)
            {
                L("未找到卸载命令，跳过主程序卸载，直接清理残留。");
                return;
            }

            // MSI：无论是 /I 还是 /X，统一转成 /X {ProductCode} /qn /norestart
            if (cmd.ToLowerInvariant().Contains("msiexec"))
            {
                string code = app.ProductCode ?? "";
                if (code.Length == 0)
                {
                    Match m = Regex.Match(cmd, @"\{[0-9A-Fa-f\-]{36}\}");
                    if (m.Success) code = m.Value;
                }
                if (code.Length > 0)
                {
                    cmd = "msiexec.exe /x " + code + " /qn /norestart";
                    quiet = true;
                }
            }

            string exe, args;
            string t = cmd.Trim();
            if (t.StartsWith("\""))
            {
                int q = t.IndexOf('"', 1);
                if (q > 0) { exe = t.Substring(1, q - 1); args = t.Substring(q + 1).Trim(); }
                else { exe = t.Trim('"'); args = ""; }
            }
            else
            {
                int sp = t.IndexOf(' ');
                if (sp < 0) { exe = t; args = ""; }
                else { exe = t.Substring(0, sp); args = t.Substring(sp + 1); }
            }

            exe = Environment.ExpandEnvironmentVariables(exe);
            if (exe.IndexOf('\\') >= 0 && !File.Exists(exe))
            {
                L("错误：卸载程序不存在（" + exe + "），跳过主程序卸载，继续清理残留。");
                return;
            }

            int timeoutSec = Config.UninstallerTimeoutSec;
            if (!quiet && timeoutSec < 300) timeoutSec = 300;   // 交互式卸载给足操作时间

            L("执行命令：" + exe + " " + args);
            ExecResult res;
            if (!quiet)
            {
                L("该程序没有静默卸载参数，将弹出卸载界面，请在窗口内完成操作（最长 " + timeoutSec + " 秒）…");
                res = RunInteractive(exe, args, timeoutSec * 1000);
            }
            else
            {
                res = Executor.Run(exe, args, timeoutSec * 1000, null);
            }

            if (!res.Started)
            {
                L("错误：无法启动卸载程序 — " + res.Output);
            }
            else if (res.TimedOut)
            {
                L("警告：卸载程序超时（" + timeoutSec + " 秒），已强制结束并继续清理残留。");
            }
            else
            {
                int code = res.ExitCode;
                if (code == 0) L("卸载程序返回成功（0）。");
                else if (code == 3010 || code == 1641) L("卸载程序返回成功（" + code + "），需要重启完成。");
                else if (code == 1605 || code == 1614) L("系统记录该程序已不存在（" + code + "），继续清理残留。");
                else if (code == 1602) L("卸载被用户取消（1602），仍继续清理残留。");
                else L("卸载程序返回代码 " + code + "，继续执行残留清理。");
            }

            if (!string.IsNullOrEmpty(app.InstallLocation) && Directory.Exists(app.InstallLocation))
                L("安装目录仍存在，将由残留清理继续删除：" + app.InstallLocation);
        }
    }
}





