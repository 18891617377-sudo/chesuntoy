using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MetroUninstaller
{
    /// <summary>未签名时的本地签名/测试签名工具包。只生成脚本，不代替用户做系统级决定。</summary>
    public static class SigningHelper
    {
        public static string Describe(SignatureInfo s)
        {
            if (s == null) return "未知";
            if (s.State == "valid")
                return "已签名且证书链可信" + (string.IsNullOrEmpty(s.Signer) ? "" : "（" + s.Signer + "）");
            if (s.State == "unsigned") return "未签名";
            if (s.State == "invalid") return "有签名但证书链不受信任";
            if (s.State == "missing") return "文件不存在";
            return "无法判断（" + s.State + "）";
        }

        public static bool IsTrusted(SignatureInfo s)
        {
            return s != null && s.State == "valid";
        }

        /// <summary>读取 BCD 的 testsigning 状态，供设置页显示。</summary>
        public static string TestSigningState()
        {
            try
            {
                ExecResult r = Executor.Run("bcdedit.exe", "/enum {current}", 30000, null);
                string[] lines = (r.Output ?? "").Split('\n');
                foreach (string raw in lines)
                {
                    string t = raw.Trim();
                    if (!t.StartsWith("testsigning", StringComparison.OrdinalIgnoreCase)) continue;
                    string low = t.ToLowerInvariant();
                    if (low.IndexOf("yes", StringComparison.Ordinal) >= 0 || t.IndexOf("是") >= 0) return "已启用";
                    if (low.IndexOf("no", StringComparison.Ordinal) >= 0 || t.IndexOf("否") >= 0) return "未启用";
                }
            }
            catch { }
            return "未知";
        }

        /// <summary>直接设置 testsigning；返回 null 表示成功，否则为错误文本。</summary>
        public static string SetTestSigning(bool on)
        {
            if (!Admin.IsAdmin) return "需要管理员权限";
            ExecResult r = Executor.Run("bcdedit.exe", "/set testsigning " + (on ? "on" : "off"), 30000, null);
            if (r.ExitCode == 0) return null;
            string msg = (r.Output ?? "").Trim();
            return msg.Length == 0 ? "bcdedit 返回退出码 " + r.ExitCode : msg;
        }

        /// <summary>生成自签名 + 签名副本 + testsigning 开关脚本，返回目录。</summary>
        public static string BuildKit(string exePath, out string dir)
        {
            if (!File.Exists(exePath)) throw new Exception("找不到当前程序文件：" + exePath);
            dir = Path.Combine(AppPaths.Root, "SigningKits",
                AppPaths.Sanitize(Path.GetFileNameWithoutExtension(exePath)) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);

            string unsigned = Path.Combine(dir, "CHESUNUninstaller-unsigned.exe");
            File.Copy(exePath, unsigned, true);

            string readme = Path.Combine(dir, "README.txt");
            File.WriteAllText(readme, ReadmeText(), Encoding.GetEncoding(936));

            WritePs1(Path.Combine(dir, "make-test-sign.ps1"), MakeTestSignPs1());
            WritePs1(Path.Combine(dir, "check-signature.ps1"), CheckSignaturePs1());

            WriteCmd(Path.Combine(dir, "run-signed.cmd"), RunSignedCmd());
            WriteCmd(Path.Combine(dir, "enable-testsigning.cmd"), EnableTestSigningCmd());
            WriteCmd(Path.Combine(dir, "disable-testsigning.cmd"), DisableTestSigningCmd());

            Log.Info("签名工具包已生成：" + dir);
            return dir;
        }

        /// <summary>生成独立内核驱动（chesunark.sys）的测试签名 / 安装工具包，不碰本程序 exe。</summary>
        public static string BuildDriverKit(string sysPath, out string dir)
        {
            if (!File.Exists(sysPath)) throw new Exception("找不到驱动文件：" + sysPath);
            dir = Path.Combine(AppPaths.Root, "DriverKits",
                AppPaths.Sanitize(Path.GetFileNameWithoutExtension(sysPath)) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);

            string unsigned = Path.Combine(dir, "chesunark-unsigned.sys");
            File.Copy(sysPath, unsigned, true);

            // 如果发布包里带了源码，复制一份，方便对方自己重编。
            try
            {
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "driver");
                if (Directory.Exists(srcDir))
                {
                    string dstSrc = Path.Combine(dir, "driver-source");
                    Directory.CreateDirectory(dstSrc);
                    foreach (string f in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(srcDir.Length).TrimStart('\\');
                        string target = Path.Combine(dstSrc, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(f, target, true);
                    }
                }
            }
            catch { }

            WritePs1(Path.Combine(dir, "make-test-sign-driver.ps1"), MakeTestSignDriverPs1());
            WriteCmd(Path.Combine(dir, "enable-testsigning.cmd"), EnableTestSigningCmd());
            WriteCmd(Path.Combine(dir, "disable-testsigning.cmd"), DisableTestSigningCmd());
            WriteCmd(Path.Combine(dir, "install-driver.cmd"), InstallDriverCmd());
            WriteCmd(Path.Combine(dir, "uninstall-driver.cmd"), UninstallDriverCmd());
            File.WriteAllText(Path.Combine(dir, "README.txt"), DriverReadmeText(), Encoding.GetEncoding(936));

            Log.Info("内核驱动签名工具包已生成：" + dir);
            return dir;
        }

        private static string DriverReadmeText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CHESUN 反 Rootkit 内核驱动 · 签名与安装工具包");
            sb.AppendLine("===============================================");
            sb.AppendLine();
            sb.AppendLine("这不是给 CHESUNUninstaller.exe 签名，而是给你自己编译的 chesunark-unsigned.sys 签名。");
            sb.AppendLine("驱动只做三件事：ZwUnloadDriver 卸驱动、删 Services 键、删驱动文件；");
            sb.AppendLine("不摘内核对象、不改 SSDT/inline hook/回调、不绕 PatchGuard。");
            sb.AppendLine();
            sb.AppendLine("一、最快路径（赛博文盲版）");
            sb.AppendLine("1. 右键 make-test-sign-driver.ps1 → 使用 PowerShell 运行。");
            sb.AppendLine("   脚本会自动请求管理员、创建“内核测试签名”证书、导入本机信任根、");
            sb.AppendLine("   签名 chesunark-unsigned.sys → chesunark-signed.sys，并执行 signtool verify。");
            sb.AppendLine("2. 再右键 enable-testsigning.cmd → 使用管理员运行，重启电脑（桌面会出现测试模式水印）。");
            sb.AppendLine("3. 右键 install-driver.cmd → 使用管理员运行；它用 sc create 安装驱动并尝试启动。");
            sb.AppendLine("4. 打开 CHESUN Uninstaller 的驱动页，扫描后选择可疑驱动点“卸载该驱动”。");
            sb.AppendLine("   如果驱动装了本工具，程序会优先调用它做 ZwUnloadDriver，再删服务和文件。");
            sb.AppendLine();
            sb.AppendLine("二、没有 signtool");
            sb.AppendLine("脚本会提示安装 Windows SDK 的 Code Signing Tools；不会偷偷下载安装。");
            sb.AppendLine();
            sb.AppendLine("三、重要边界");
            sb.AppendLine("· 驱动只能卸载“有 DriverUnload 例程”的驱动。");
            sb.AppendLine("· 没有 Unload 的 rootkit 驱动，热卸载会返回失败；本工具会转禁用服务 + 重启删除。");
            sb.AppendLine("· 真正藏在 EFI 分区/固件里的 bootkit，仍要用 WinPE 离线包 clean-efi.cmd。");
            sb.AppendLine("· testsigning 会降低内核安全性，仅测试机短期使用，用完 disable-testsigning.cmd。");
            sb.AppendLine();
            sb.AppendLine("四、正式签名");
            sb.AppendLine("用 EV/OV 证书签 .sys，并按微软要求做 WHQL / attestation 签名后再分发。");
            sb.AppendLine("自签名证书只在本机测试有效，不能拿去公开发行。");
            return sb.ToString();
        }

        private static string MakeTestSignDriverPs1()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine("$dir = Split-Path -Parent $MyInvocation.MyCommand.Path");
            sb.AppendLine("$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)");
            sb.AppendLine("if (-not $isAdmin) {");
            sb.AppendLine("    Write-Host '需要管理员权限，正在请求 UAC…'");
            sb.AppendLine("    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList \"-NoProfile -ExecutionPolicy Bypass -File `\"$PSCommandPath`\"\"");
            sb.AppendLine("    exit");
            sb.AppendLine("}");
            sb.AppendLine("Set-Location -LiteralPath $dir");
            sb.AppendLine("Write-Host '== CHESUN 内核驱动测试签名 ==' -ForegroundColor Cyan");
            sb.AppendLine("$src = Join-Path $dir 'chesunark-unsigned.sys'");
            sb.AppendLine("$signed = Join-Path $dir 'chesunark-signed.sys'");
            sb.AppendLine("if (-not (Test-Path -LiteralPath $src)) { throw '缺少 chesunark-unsigned.sys' }");
            sb.AppendLine("Copy-Item -LiteralPath $src -Destination $signed -Force");
            sb.AppendLine("$subject = 'CN=CHESUN Local Test Kernel Signing'");
            sb.AppendLine("$cert = Get-ChildItem Cert:\\LocalMachine\\My -CodeSigningCert | Where-Object { $_.Subject -eq $subject } | Select-Object -First 1");
            sb.AppendLine("if (-not $cert) {");
            sb.AppendLine("    Write-Host '创建本机内核测试签名证书…'");
            sb.AppendLine("    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `");
            sb.AppendLine("        -CertStoreLocation Cert:\\LocalMachine\\My -KeyExportPolicy Exportable `");
            sb.AppendLine("        -KeyUsage DigitalSignature -NotAfter (Get-Date).AddYears(3)");
            sb.AppendLine("}");
            sb.AppendLine("$cer = Join-Path $dir 'CHESUN-Kernel-Test.cer'");
            sb.AppendLine("Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null");
            sb.AppendLine("Write-Host '导入本机受信任根 / 受信任发布者…'");
            sb.AppendLine("Import-Certificate -FilePath $cer -CertStoreLocation Cert:\\LocalMachine\\Root | Out-Null");
            sb.AppendLine("Import-Certificate -FilePath $cer -CertStoreLocation Cert:\\LocalMachine\\TrustedPublisher | Out-Null");
            sb.AppendLine("function Find-SignTool {");
            sb.AppendLine("    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue");
            sb.AppendLine("    if ($cmd) { return $cmd.Source }");
            sb.AppendLine("    foreach ($r in @((Join-Path $env:ProgramFiles 'Windows Kits\\10\\bin'), (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\\10\\bin'))) {");
            sb.AppendLine("        if (Test-Path -LiteralPath $r) {");
            sb.AppendLine("            $hit = Get-ChildItem -LiteralPath $r -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |");
            sb.AppendLine("                Where-Object { $_.FullName -match '\\\\x64\\\\signtool.exe$' } | Sort-Object FullName -Descending | Select-Object -First 1");
            sb.AppendLine("            if ($hit) { return $hit.FullName }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    return $null");
            sb.AppendLine("}");
            sb.AppendLine("$signtool = Find-SignTool");
            sb.AppendLine("if (-not $signtool) {");
            sb.AppendLine("    Write-Host '未找到 signtool.exe。请安装 Windows SDK 的 Code Signing Tools：' -ForegroundColor Yellow");
            sb.AppendLine("    Write-Host 'https://developer.microsoft.com/windows/downloads/windows-sdk/'");
            sb.AppendLine("    Read-Host '安装后重新运行，按回车退出'");
            sb.AppendLine("    exit 1");
            sb.AppendLine("}");
            sb.AppendLine("& $signtool sign /sha1 $cert.Thumbprint /fd SHA256 /v $signed");
            sb.AppendLine("if ($LASTEXITCODE -ne 0) { throw 'signtool 签名失败' }");
            sb.AppendLine("& $signtool verify /pa /v $signed");
            sb.AppendLine("if ($LASTEXITCODE -ne 0) { throw '签名验证失败' }");
            sb.AppendLine("Write-Host ''");
            sb.AppendLine("Write-Host ('完成：' + $signed) -ForegroundColor Green");
            sb.AppendLine("Write-Host '下一步：管理员运行 enable-testsigning.cmd，重启后再 install-driver.cmd。'");
            sb.AppendLine("Read-Host '按回车退出'");
            return sb.ToString();
        }

        private static string InstallDriverCmd()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("net session >nul 2>&1 || (echo 需要管理员权限 & pause & exit /b 1)");
            sb.AppendLine("cd /d \"%~dp0\"");
            sb.AppendLine("if not exist \"chesunark-signed.sys\" (echo 先运行 make-test-sign-driver.ps1 & pause & exit /b 1)");
            sb.AppendLine("sc stop ChesunArk >nul 2>&1");
            sb.AppendLine("sc delete ChesunArk >nul 2>&1");
            sb.AppendLine("sc create ChesunArk type= kernel start= demand error= normal binPath= \"\\??\\%~dp0chesunark-signed.sys\" DisplayName= \"CHESUN Anti-Rootkit Driver\"");
            sb.AppendLine("if errorlevel 1 (echo sc create 失败 & pause & exit /b 1)");
            sb.AppendLine("sc start ChesunArk");
            sb.AppendLine("if errorlevel 1 (echo 启动失败：确认已 enable-testsigning 并重启；或驱动签名/路径不对 & pause & exit /b 1)");
            sb.AppendLine("echo 已安装并启动 ChesunArk。");
            sb.AppendLine("pause");
            return sb.ToString();
        }

        private static string UninstallDriverCmd()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("net session >nul 2>&1 || (echo 需要管理员权限 & pause & exit /b 1)");
            sb.AppendLine("sc stop ChesunArk >nul 2>&1");
            sb.AppendLine("sc delete ChesunArk");
            sb.AppendLine("echo 已停止并删除 ChesunArk 服务。");
            sb.AppendLine("pause");
            return sb.ToString();
        }

        private static void WritePs1(string path, string text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(true));
        }

        private static void WriteCmd(string path, string text)
        {
            try { File.WriteAllText(path, text, Encoding.GetEncoding(936)); }
            catch { File.WriteAllText(path, text, Encoding.Default); }
        }

        private static string ReadmeText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CHESUN Uninstaller · 签名工具包");
            sb.AppendLine("================================");
            sb.AppendLine();
            sb.AppendLine("一、为什么会有这个包");
            sb.AppendLine("本程序是用户态 exe。Windows 的 WDAC / SmartScreen 只认 Authenticode 签名，");
            sb.AppendLine("未签名时可能直接被拦截。这个包给你两条本地可用路径，以及测试签名的说明。");
            sb.AppendLine();
            sb.AppendLine("二、最快路径：本机自签名副本（推荐）");
            sb.AppendLine("1. 右键 make-test-sign.ps1 → “使用 PowerShell 运行”。");
            sb.AppendLine("   如果提示“禁止运行脚本”，先在 PowerShell 里执行：");
            sb.AppendLine("   Set-ExecutionPolicy -Scope Process Bypass");
            sb.AppendLine("   然后重新运行脚本。");
            sb.AppendLine("2. 脚本会：创建自签名代码签名证书 → 加入当前用户“受信任根/受信任发布者”");
            sb.AppendLine("   → 复制 CHESUNUninstaller-unsigned.exe → 用 signtool 签成 CHESUNUninstaller-signed.exe");
            sb.AppendLine("   → 自动 verify。首次导入证书可能弹“安全警告”，点“是”。");
            sb.AppendLine("3. 看到“完成”后，双击 run-signed.cmd 启动签名副本。");
            sb.AppendLine();
            sb.AppendLine("注意：脚本需要 signtool.exe（Windows SDK 的 Code Signing Tools）。");
            sb.AppendLine("      没装 SDK 时脚本会提示下载地址，不会偷偷装东西。");
            sb.AppendLine("      自签名证书只在本机可信，只适合自己用；不要拿它给公开发行版签名。");
            sb.AppendLine();
            sb.AppendLine("三、测试签名（testsigning）");
            sb.AppendLine("testsigning 只影响“内核驱动”的签名校验，不能让未签名的用户态 exe 绕过 WDAC。");
            sb.AppendLine("只有你要测试未签名驱动时才用：");
            sb.AppendLine("   enable-testsigning.cmd   → bcdedit /set testsigning on，重启生效（桌面会显示测试模式水印）");
            sb.AppendLine("   disable-testsigning.cmd  → 关闭并重启");
            sb.AppendLine("两个脚本都要求管理员，并且会先让你确认。");
            sb.AppendLine();
            sb.AppendLine("四、正式发布");
            sb.AppendLine("购买 OV/EV Authenticode 代码签名证书后，用 signtool 签原 exe：");
            sb.AppendLine("   signtool sign /f your.pfx /p 你的密码 /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 CHESUNUninstaller.exe");
            sb.AppendLine("签名前先退出程序，否则文件被占用。签完用 check-signature.ps1 或 signtool verify /pa 验证。");
            sb.AppendLine();
            sb.AppendLine("五、沙盒");
            sb.AppendLine("run-in-sandbox.wsb 内不需要签名，宿主 WDAC 不影响沙盒。");
            sb.AppendLine();
            sb.AppendLine("六、文件说明");
            sb.AppendLine("make-test-sign.ps1      创建证书 + 签副本 + 验证");
            sb.AppendLine("check-signature.ps1     查看签名状态");
            sb.AppendLine("run-signed.cmd          启动签名副本");
            sb.AppendLine("enable-testsigning.cmd  启用测试签名（仅内核驱动测试）");
            sb.AppendLine("disable-testsigning.cmd 关闭测试签名");
            sb.AppendLine("CHESUNUninstaller-unsigned.exe  未签名副本");
            return sb.ToString();
        }

        private static string MakeTestSignPs1()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine("$dir = Split-Path -Parent $MyInvocation.MyCommand.Path");
            sb.AppendLine("$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)");
            sb.AppendLine("if (-not $isAdmin) {");
            sb.AppendLine("    Write-Host '需要管理员权限（导入受信任根证书），正在请求 UAC…'");
            sb.AppendLine("    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList \"-NoProfile -ExecutionPolicy Bypass -File `\"$PSCommandPath`\"\"");
            sb.AppendLine("    exit");
            sb.AppendLine("}");
            sb.AppendLine("Set-Location -LiteralPath $dir");
            sb.AppendLine("Write-Host '== CHESUN 本地测试签名 ==' -ForegroundColor Cyan");
            sb.AppendLine("Write-Host '只给本机生成自签名证书，不要用于公开发行。'");
            sb.AppendLine("$src = Join-Path $dir 'CHESUNUninstaller-unsigned.exe'");
            sb.AppendLine("$signed = Join-Path $dir 'CHESUNUninstaller-signed.exe'");
            sb.AppendLine("if (-not (Test-Path -LiteralPath $src)) { throw '缺少 CHESUNUninstaller-unsigned.exe' }");
            sb.AppendLine("Copy-Item -LiteralPath $src -Destination $signed -Force");
            sb.AppendLine("");
            sb.AppendLine("$subject = 'CN=CHESUN Local Test Code Signing'");
            sb.AppendLine("$cert = Get-ChildItem Cert:\\CurrentUser\\My -CodeSigningCert |");
            sb.AppendLine("    Where-Object { $_.Subject -eq $subject } | Select-Object -First 1");
            sb.AppendLine("if (-not $cert) {");
            sb.AppendLine("    Write-Host '创建自签名代码签名证书…'");
            sb.AppendLine("    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `");
            sb.AppendLine("        -CertStoreLocation Cert:\\CurrentUser\\My -KeyExportPolicy Exportable `");
            sb.AppendLine("        -KeyUsage DigitalSignature -NotAfter (Get-Date).AddYears(5)");
            sb.AppendLine("}");
            sb.AppendLine("$cer = Join-Path $dir 'CHESUN-Test.cer'");
            sb.AppendLine("Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null");
            sb.AppendLine("Write-Host '把证书加入当前用户“受信任根/受信任发布者”…（可能弹安全警告，点“是”）'");
            sb.AppendLine("Import-Certificate -FilePath $cer -CertStoreLocation Cert:\\CurrentUser\\Root | Out-Null");
            sb.AppendLine("Import-Certificate -FilePath $cer -CertStoreLocation Cert:\\CurrentUser\\TrustedPublisher | Out-Null");
            sb.AppendLine("");
            sb.AppendLine("function Find-SignTool {");
            sb.AppendLine("    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue");
            sb.AppendLine("    if ($cmd) { return $cmd.Source }");
            sb.AppendLine("    $roots = @(");
            sb.AppendLine("        (Join-Path $env:ProgramFiles 'Windows Kits\\10\\bin'),");
            sb.AppendLine("        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\\10\\bin')");
            sb.AppendLine("    )");
            sb.AppendLine("    foreach ($r in $roots) {");
            sb.AppendLine("        if (Test-Path -LiteralPath $r) {");
            sb.AppendLine("            $hit = Get-ChildItem -LiteralPath $r -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |");
            sb.AppendLine("                Where-Object { $_.FullName -match '\\\\x64\\\\signtool.exe$' } |");
            sb.AppendLine("                Sort-Object FullName -Descending | Select-Object -First 1");
            sb.AppendLine("            if ($hit) { return $hit.FullName }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    return $null");
            sb.AppendLine("}");
            sb.AppendLine("$signtool = Find-SignTool");
            sb.AppendLine("if (-not $signtool) {");
            sb.AppendLine("    Write-Host '未找到 signtool.exe。' -ForegroundColor Yellow");
            sb.AppendLine("    Write-Host '请安装 Windows SDK 的 Code Signing Tools：'");
            sb.AppendLine("    Write-Host 'https://developer.microsoft.com/windows/downloads/windows-sdk/'");
            sb.AppendLine("    Read-Host '安装后重新运行本脚本，按回车退出'");
            sb.AppendLine("    exit 1");
            sb.AppendLine("}");
            sb.AppendLine("Write-Host ('使用 signtool：' + $signtool)");
            sb.AppendLine("& $signtool sign /sha1 $cert.Thumbprint /fd SHA256 /v $signed");
            sb.AppendLine("if ($LASTEXITCODE -ne 0) { throw 'signtool 签名失败' }");
            sb.AppendLine("& $signtool verify /pa /v $signed");
            sb.AppendLine("if ($LASTEXITCODE -ne 0) { throw '签名验证失败，请检查证书是否已加入受信任根' }");
            sb.AppendLine("Write-Host ''");
            sb.AppendLine("Write-Host ('完成：' + $signed) -ForegroundColor Green");
            sb.AppendLine("Write-Host '双击 run-signed.cmd 启动签名副本。'");
            sb.AppendLine("Read-Host '按回车退出'");
            return sb.ToString();
        }

        private static string CheckSignaturePs1()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("$dir = Split-Path -Parent $MyInvocation.MyCommand.Path");
            sb.AppendLine("$f = Join-Path $dir 'CHESUNUninstaller-signed.exe'");
            sb.AppendLine("if (-not (Test-Path -LiteralPath $f)) { $f = Join-Path $dir 'CHESUNUninstaller-unsigned.exe' }");
            sb.AppendLine("Get-AuthenticodeSignature -LiteralPath $f | Format-List Status, StatusMessage, SignerCertificate, Path");
            sb.AppendLine("Read-Host '按回车退出'");
            return sb.ToString();
        }

        private static string RunSignedCmd()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("cd /d \"%~dp0\"");
            sb.AppendLine("if not exist \"CHESUNUninstaller-signed.exe\" (");
            sb.AppendLine("  echo 还没有签名副本，请先运行 make-test-sign.ps1");
            sb.AppendLine("  pause");
            sb.AppendLine("  exit /b 1");
            sb.AppendLine(")");
            sb.AppendLine("start \"\" \"CHESUNUninstaller-signed.exe\"");
            return sb.ToString();
        }

        private static string EnableTestSigningCmd()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("net session >nul 2>&1 || (echo 需要管理员权限 & pause & exit /b 1)");
            sb.AppendLine("echo 警告：testsigning 会降低内核安全性，仅用于测试未签名驱动。");
            sb.AppendLine("echo 它不会让本工具（用户态 exe）绕过 WDAC / SmartScreen。");
            sb.AppendLine("choice /c YN /m \"确认启用 testsigning\"");
            sb.AppendLine("if errorlevel 2 exit /b 0");
            sb.AppendLine("bcdedit /set testsigning on");
            sb.AppendLine("if errorlevel 1 (echo 设置失败 & pause & exit /b 1)");
            sb.AppendLine("echo 已启用，重启后生效；桌面会显示“测试模式”水印。");
            sb.AppendLine("echo 关闭：disable-testsigning.cmd");
            sb.AppendLine("pause");
            return sb.ToString();
        }

        private static string DisableTestSigningCmd()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("net session >nul 2>&1 || (echo 需要管理员权限 & pause & exit /b 1)");
            sb.AppendLine("bcdedit /set testsigning off");
            sb.AppendLine("if errorlevel 1 (echo 设置失败 & pause & exit /b 1)");
            sb.AppendLine("echo 已关闭，重启后生效。");
            sb.AppendLine("pause");
            return sb.ToString();
        }
    }
}
