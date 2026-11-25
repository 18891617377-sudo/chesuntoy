using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MetroUninstaller
{
    /// <summary>生成 Metro 风格的清理报告（HTML）与摘要行。</summary>
    public static class ReportWriter
    {
        public static string Write(CleanReport r)
        {
            AppPaths.EnsureDirs();
            string name = AppPaths.Sanitize(r.AppName);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string html = Path.Combine(AppPaths.Reports, stamp + "_" + name + ".html");
            string txt = Path.Combine(AppPaths.Reports, stamp + "_" + name + ".txt");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            sb.AppendLine("<title>清理报告 · " + Esc(r.AppName) + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{margin:0;background:#e6e6e6;font-family:'Segoe UI','Microsoft YaHei UI',sans-serif;color:#1a1a1a}");
            sb.AppendLine(".hd{background:#1ba1e2;color:#fff;padding:36px 48px}");
            sb.AppendLine(".hd h1{margin:0;font-weight:300;font-size:40px;letter-spacing:1px}");
            sb.AppendLine(".hd p{margin:8px 0 0;font-size:14px;opacity:.9}");
            sb.AppendLine(".wrap{padding:32px 48px 64px}");
            sb.AppendLine(".tiles{display:flex;flex-wrap:wrap;gap:10px;margin-bottom:28px}");
            sb.AppendLine(".tile{background:#fff;padding:18px 22px;min-width:150px}");
            sb.AppendLine(".tile b{display:block;font-size:32px;font-weight:300}");
            sb.AppendLine(".tile span{font-size:12px;color:#707070}");
            sb.AppendLine("h2{font-weight:300;font-size:24px;margin:28px 0 10px}");
            sb.AppendLine("table{border-collapse:collapse;width:100%;background:#fff;font-size:13px}");
            sb.AppendLine("th{background:#f2f2f2;text-align:left;padding:10px 12px;font-weight:600;border-bottom:1px solid #ddd}");
            sb.AppendLine("td{padding:9px 12px;border-bottom:1px solid #eee;word-break:break-all}");
            sb.AppendLine(".ok{color:#60a917}.bad{color:#c13a3a}.warn{color:#d8622a}.mut{color:#888}");
            sb.AppendLine(".log{background:#1f1f1f;color:#ddd;padding:16px 20px;font-family:Consolas,monospace;font-size:12px;line-height:1.7;white-space:pre-wrap}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<div class=\"hd\"><h1>" + Esc(r.AppName) + "</h1>");
            sb.AppendLine("<p>" + (r.Success ? "清理完成" : "清理未完全成功") + " · " + r.Finished.ToString("yyyy-MM-dd HH:mm:ss") + " · " + (r.Elevated ? "管理员权限" : "标准权限") + "</p></div>");
            sb.AppendLine("<div class=\"wrap\">");

            sb.AppendLine("<div class=\"tiles\">");
            Tile(sb, r.Items.Count.ToString(), "扫描残留项");
            Tile(sb, (r.DeletedFiles + r.DeletedRegistry + r.DeletedServices + r.DeletedTasks).ToString(), "已删除项");
            Tile(sb, r.Drivers.Count.ToString(), "清理驱动");
            Tile(sb, r.PendingRestart.ToString(), "待重启删除");
            Tile(sb, r.Failed.ToString(), "失败项");
            sb.AppendLine("</div>");

            sb.AppendLine("<h2>执行步骤</h2><div class=\"log\">");
            foreach (string s in r.Steps) sb.AppendLine(Esc(s));
            sb.AppendLine("</div>");

            if (r.RestorePoint.Length > 0 || r.BackupPath.Length > 0)
            {
                sb.AppendLine("<h2>回滚信息</h2><table><tr><th>类型</th><th>位置 / 结果</th></tr>");
                if (r.RestorePoint.Length > 0)
                    sb.AppendLine("<tr><td>系统还原点</td><td>" + Esc(r.RestorePoint) + "</td></tr>");
                if (r.BackupPath.Length > 0)
                    sb.AppendLine("<tr><td>备份目录（保留 " + Config.BackupDays + " 天）</td><td>" + Esc(r.BackupPath) + "</td></tr>");
                sb.AppendLine("</table>");
            }

            sb.AppendLine("<h2>残留与驱动明细</h2><table><tr><th>类型</th><th>目标</th><th>归属证据</th><th>结果</th></tr>");
            foreach (ResidueItem it in r.Items)
            {
                string status = it.Protected ? "<span class=\"mut\">受保护 · 跳过</span>" : "<span class=\"ok\">已处理</span>";
                sb.AppendLine("<tr><td>" + Esc(it.KindText) + "</td><td>" + Esc(it.Target) +
                    (it.ValueName.Length > 0 ? " → " + Esc(it.ValueName) : "") + "</td><td class=\"mut\">" +
                    Esc(it.Evidence) + "</td><td>" + status + "</td></tr>");
            }
            foreach (DriverInfo d in r.Drivers)
            {
                sb.AppendLine("<tr><td>内核驱动</td><td>" + Esc(d.ServiceName) + " · " + Esc(d.Resolution()) +
                    "</td><td class=\"mut\">" + Esc(d.OwnerSoftware) + " / 归属 " + d.Attribution +
                    "%</td><td><span class=\"ok\">服务已删除</span></td></tr>");
            }
            sb.AppendLine("</table></div></body></html>");

            try { File.WriteAllText(html, sb.ToString(), Encoding.UTF8); }
            catch (Exception ex) { Log.Error("写入报告失败：" + ex.Message); }

            try
            {
                StringBuilder t = new StringBuilder();
                t.AppendLine("# " + r.AppName);
                t.AppendLine("app\t" + r.AppName);
                t.AppendLine("time\t" + r.Finished.ToString("yyyy-MM-dd HH:mm:ss"));
                t.AppendLine("result\t" + (r.Success ? "OK" : "FAIL"));
                t.AppendLine("items\t" + r.Items.Count);
                t.AppendLine("deleted\t" + (r.DeletedFiles + r.DeletedRegistry + r.DeletedServices + r.DeletedTasks));
                t.AppendLine("drivers\t" + r.Drivers.Count);
                t.AppendLine("pending\t" + r.PendingRestart);
                t.AppendLine("failed\t" + r.Failed);
                t.AppendLine("report\t" + html);
                File.WriteAllText(txt, t.ToString(), Encoding.UTF8);
            }
            catch { }

            r.ReportPath = html;
            return html;
        }

        private static void Tile(StringBuilder sb, string value, string label)
        {
            sb.AppendLine("<div class=\"tile\"><b>" + Esc(value) + "</b><span>" + Esc(label) + "</span></div>");
        }

        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }

    public class HistoryEntry
    {
        public string App = "";
        public DateTime Time;
        public string Result = "";
        public int Items;
        public int Deleted;
        public int Drivers;
        public int Pending;
        public string ReportPath = "";
        public string MetaPath = "";
    }

    public static class HistoryStore
    {
        public static List<HistoryEntry> List()
        {
            List<HistoryEntry> list = new List<HistoryEntry>();
            try
            {
                if (!Directory.Exists(AppPaths.Reports)) return list;
                foreach (string meta in Directory.GetFiles(AppPaths.Reports, "*.txt"))
                {
                    HistoryEntry h = new HistoryEntry();
                    h.MetaPath = meta;
                    foreach (string line in File.ReadAllLines(meta))
                    {
                        if (line.StartsWith("# ")) { if (h.App.Length == 0) h.App = line.Substring(2).Trim(); continue; }
                        string[] p = line.Split('\t');
                        if (p.Length < 2) continue;
                        switch (p[0])
                        {
                            case "app": h.App = p[1]; break;
                            case "time": DateTime.TryParse(p[1], out h.Time); break;
                            case "result": h.Result = p[1]; break;
                            case "items": int.TryParse(p[1], out h.Items); break;
                            case "deleted": int.TryParse(p[1], out h.Deleted); break;
                            case "drivers": int.TryParse(p[1], out h.Drivers); break;
                            case "pending": int.TryParse(p[1], out h.Pending); break;
                            case "report": h.ReportPath = p[1]; break;
                        }
                    }
                    if (h.Time == DateTime.MinValue) h.Time = File.GetLastWriteTime(meta);
                    list.Add(h);
                }
            }
            catch (Exception ex) { Log.Warn("读取历史失败：" + ex.Message); }
            list.Sort(delegate (HistoryEntry a, HistoryEntry b) { return b.Time.CompareTo(a.Time); });
            return list;
        }
    }
}


