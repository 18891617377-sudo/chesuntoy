# CHESUN Uninstaller · 深度卸载与内核驱动清理

Windows 8 Modern UI（Metro 磁贴）风格的深度卸载工具。核心是两件事：把普通软件的**全量残留**清干净，同时只移除**明确归属于目标软件**的内核驱动，系统原生 / WHQL / 其他软件驱动一律锁定。

- 语言 / 技术栈：C#（.NET Framework 4.8）+ WinForms 自绘 Metro 控件，无第三方依赖
- 编译产物：`CHESUNUninstaller.exe`（单文件，约 168 KB）
- 编译状态：**通过，0 错误 0 警告**
- 真机运行状态：**已在 Windows Sandbox 内启动并验证界面与扫描流程**（宿主因 WDAC 策略拦截未签名 exe，详见第 5 节）

---

## 1. 功能实现

### 1.1 普通软件超净卸载

- 软件清单来自三处 `Uninstall` 注册表位置（HKLM 64 位 / HKLM WOW6432Node / HKCU），过滤 `SystemComponent=1`、补丁与子项。
- 深度扫描并清理：
  - 安装目录内全部文件与子目录（超过上限会截断并提示）
  - `%AppData%` / `%LocalAppData%` / `%ProgramData%` / 用户目录 / 文档 下的同名目录
  - 注册表：软件主键、卸载信息键、`Run`/`RunOnce`、`Services`、`Environment`、`SharedDLLs`、`App Paths`
  - 环境变量只移除指向安装目录的**片段**（例如从 PATH 中删掉该目录），不会清空整条变量
  - 计划任务（读取 `C:\Windows\System32\Tasks` 的 XML，命中则 `schtasks /delete`）
  - 桌面 / 公共桌面 / 开始菜单 / 快速启动中的快捷方式（解析 `.lnk` 目标路径）
  - COM 组件与右键菜单扩展（`CLSID` / `shellex\ContextMenuHandlers` 的 `InprocServer32` 路径匹配）
- 强制终止：WMI `Win32_Process` 枚举（含隐藏会话进程），按安装路径 / 主程序路径 / 命令行匹配，含递归子进程树。
- 占用文件：`MoveFileEx(MOVEFILE_DELAY_UNTIL_REBOOT)` 登记重启删除，目录则逐个登记剩余文件。

### 1.2 内核驱动精准清理（核心约束）

- 枚举 `HKLM\SYSTEM\CurrentControlSet\Services` 中 `Type=1/2` 的驱动，解析 `ImagePath`（含 `\SystemRoot\`、`\??\`、相对路径），用 WMI `Win32_SystemDriver` 取运行状态。
- 签名：`WinVerifyTrust` 校验嵌入签名 + 提取签署者 CN；区分 `valid / unsigned / invalid / missing`。
- **四维归属打分（0–100）**
  | 维度 | 分值 |
  |---|---|
  | 驱动文件位于 `InstallLocation` 内 | +55 |
  | 位于主程序目录 | +40 |
  | 服务名 / 描述包含软件名 | +18 |
  | 签名主体与发布者一致 | +20 |
  | 服务 `Parameters` 引用安装目录 | +15 |
  | 服务名包含软件名 | +8 |
- 只有 **归属 ≥ 45 且未被保护** 的驱动才进入可删除列表；30–44 只展示为「疑似」。
- **安全红线（硬编码，优先于一切）**
  - Windows 目录下任何驱动、`System32\drivers`、DriverStore → 受保护
  - 关键系统驱动服务名清单（disk / ntfs / tcpip / fltmgr / volsnap / wdf01000 …）
  - Microsoft 签名、Boot/System 级驱动、用户白名单
  - 安装目录过于宽泛（盘符根、`Program Files`、`Windows`、用户目录）时，直接跳过文件扫描与「服务程序位于安装目录」判定
  - 厂商公共根（`Microsoft` / `Google` / `Adobe` / `Intel` / `NVIDIA` / `Valve` …）的注册表键与数据目录**只允许向下一层匹配具体产品**，永不会整根删除
- 删除前：备份 `.sys` 文件与 `Services` 注册表项 → 禁用启动（`Start=4`）→ 多轮 `sc stop` → `sc delete` → 删文件（占用则 SYSTEM 计划任务接管 → 仍失败登记重启删除）→ `pnputil` 清 DriverStore 驱动包。
- 二次确认：确认清单 → 「永久删除」第二次确认才真正执行。

### 1.3 反 Rootkit / 反 Bootkit

- **Rootkit 启发式扫描**：服务存在但驱动文件缺失、无签名且不在系统目录、`Start=4` 却仍在运行（隐藏服务）、文件位于 Temp / Downloads / AppData、非微软签名的 Boot/System 级驱动；输出高危 / 可疑分级清单。
- **已加载内核模块枚举**：`NtQuerySystemInformation(SystemModuleInformation)` 取全部已加载内核模块（含完整路径），与 `Services` 注册表比对；已加载但无服务注册、且签名不可信或位于 Windows 目录外的模块单独标记，用于发现删了服务键但仍在内存里的隐藏驱动。
- **Bootkit 扫描**：读取 Secure Boot 状态，解析 `bcdedit /enum all` 中的非标准 `.efi` 加载路径（带启动项 GUID）；`mountvol S: /S` 挂载 ESP 后遍历整块 ESP 的 `.efi/.exe/.dll`，逐个校验签名，非微软签名项标红，`finally` 卸载 ESP。
- **WinPE 离线清理包**：一键生成 `clean-driver.cmd` + `clean-efi.cmd` +（有非标准 BCD 项时）`clean-bcd.cmd` + `README.txt`。离线挂载 SYSTEM 注册表 → 备份 → 禁用并删除服务键 → 删除 `System32\drivers\*.sys` → 删除 DriverStore 包；`clean-efi.cmd D:\` 先备份再删除可疑引导组件；BCD 部分先 `/export` 备份，再按 GUID 删除非标准启动项，并提示用 `bcdboot` 修复引导。在 WinPE / 微 PE 下原系统内核不运行，可彻底绕过运行中驱动无法卸载的限制。
- **WDAC 按哈希拉黑**：调用系统自带 ConfigCI（`New-CIPolicyRule -Level Hash -DriverFilePath ... -Deny`），以 `AllowAll.xml` 为底合并，生成 `policy.cip` + `deploy-wdac.cmd`。部署后该驱动文件哈希被内核拒绝加载，重启即失效；脚本不自动部署，避免误拦导致无法启动。

### 1.4 一键全流程

`终止进程 → 卸载主程序（QuietUninstallString / msiexec /qn）→ 深度扫描 → 匹配驱动 → 备份 → 删除 → 报告`，全程日志实时显示在覆盖层。

### 1.5 安全保障

- 卸载前调用 WMI `SystemRestore.CreateRestorePoint`（系统保护未开启时告警并继续）
- 删除前备份：文件复制 + `reg export`，默认保留 7 天，可在「还原点管理」页一键恢复
- 备份上限：单文件 256 MB、单次 2 GB，超出则记录「跳过备份」并继续
- 待重启删除项统计（`PendingFileRenameOperations`）
- 白名单：`%LOCALAPPDATA%\CHESUN Uninstaller\whitelist.txt` + 内置规则双层
- 清理报告：Metro 风格 HTML（概览磁贴、步骤日志、明细表）+ `.txt` 摘要供历史页读取

### 1.6 签名 / 测试签名助手

- 启动时用 `WinVerifyTrust` 检查自身 Authenticode 签名；未签名或证书链不可信时自动弹窗，不阻止继续使用。
- 弹窗说明四种路径：本机自签名副本、购买正式证书、WinPE/测试签名、Windows Sandbox。
- **生成签名工具包**：`make-test-sign.ps1`（自签名 + 导入当前用户受信任根/受信任发布者 + 签出 `CHESUNUninstaller-signed.exe` + verify）、`check-signature.ps1`、`run-signed.cmd`、`enable-testsigning.cmd`、`disable-testsigning.cmd`、`README.txt`。
- 脚本自动请求 UAC；缺 `signtool.exe` 时给出 Windows SDK 下载地址，不自动安装任何东西。
- 设置页可随时重开「代码签名 / 测试签名」，并可直接启用/关闭 `testsigning`（红色按钮、明确警告）。

**边界**：`testsigning` 只放宽内核驱动签名校验，不能让未签名的用户态 exe 绕过 WDAC / SmartScreen；自签名证书只在本机可信，不能用于公开发行版。

---

## 2. 界面

严格按提示词的 Metro 规范：纯色、无圆角、无渐变、无阴影、Segoe UI Light 标题、内容优先。

| 页面 | 内容 |
|---|---|
| 起始页 | 磁贴网格：310×310（软件卸载 / 驱动管理）、310×150（扫描历史 / 系统状态）、150×150（设置 / 还原点 / 白名单）；磁贴实时显示软件数、可疑驱动数 |
| 软件卸载 | 搜索栏 + 「一键深度扫描」方形按钮 + 长条磁贴列表（选中项强调色填充）+ 右侧滑入详情面板（基本信息 / 残留统计 / 关联驱动 / 一键卸载） |
| 驱动管理 | 全部 / 可疑无签名 / 可归属 三种筛选；行内色块标签（无签名红、受保护灰、归属百分比橙）；详情展示签名、路径、加载级别、状态、四维证据；顶部「反 Rootkit / Bootkit 扫描」，详情底部「卸载该驱动 / WDAC 拉黑 / 离线清理包」 |
| 扫描历史 | 报告归档，点击用默认浏览器打开 HTML |
| 系统状态 | 操作系统、权限、还原点状态、备份占用、待重启项 |
| 设置 | 还原点、备份、保留天数、深度扫描、卸载程序超时、文件数上限、代码签名 / 测试签名（点击切换） |
| 还原点管理 | 创建系统还原点 + 备份列表一键恢复 |
| 白名单 | 添加 / 移除自定义保护项 |

确认弹窗为全屏半透明覆盖层（先截取页面位图再压暗，保证背景内容可见），按钮横向排列。

---

## 3. 目录结构

```
MetroUninstaller/
├─ app.manifest                   # 兼容性 / DPI / asInvoker
├─ build.ps1                      # csc 一键编译
├─ CHESUNUninstaller.exe           # 已编译产物
├─ README.md
└─ src/
   ├─ Program.cs                  # 入口、DPI、单实例、全局异常
   ├─ Theme.cs                    # Metro 色板、字体缓存、几何图标、DPI 缩放
   ├─ Core.cs                     # 路径、配置、日志、数据模型、提权
   ├─ Services/
   │  ├─ SoftwareInventory.cs     # 已安装软件清单
   │  ├─ ProcessTools.cs          # 关联进程与进程树终止
   │  ├─ ResidueScanner.cs        # 文件/注册表/服务/任务/快捷方式/COM 扫描
   │  ├─ SignatureVerifier.cs     # WinVerifyTrust + 证书主体
   │  ├─ DriverInventory.cs       # 驱动枚举、四维归属、可删判定
   │  ├─ SafetyGuard.cs           # 系统保护、厂商根、白名单、安全目录判定
   │  ├─ BackupService.cs         # 备份清单/恢复/过期清理（含 RestorePointService 还原点）
   │  ├─ Executor.cs              # 外部命令、注册表操作、重启删除
   │  ├─ UninstallPipeline.cs     # 一键流程 / 驱动强制清理
   │  ├─ SystemRunner.cs          # SYSTEM 计划任务兜底删除
   │  ├─ AntiRootkit.cs           # Rootkit/Bootkit 扫描、WinPE 离线包、WDAC 策略
   │  ├─ SigningHelper.cs         # 签名检测、自签名工具包、testsigning 开关
   │  ├─ ReportWriter.cs          # HTML 报告 + 历史摘要
   │  └─ HistoryStore.cs（同文件）
   └─ UI/
      ├─ MetroControls.cs         # MetroTile / MetroButton / MetroRow / MetroSearch / ScrollPanel / Overlay
      ├─ SidePanel.cs             # 右侧滑入详情面板
      ├─ MainForm.cs              # 顶栏、导航、启动时后台加载清单
      ├─ PagesSoftware.cs         # 软件卸载页
      ├─ PagesDriver.cs           # 驱动管理页
      └─ PagesMisc.cs             # 历史 / 状态 / 设置 / 还原点 / 白名单
```

---

## 4. 编译与运行

```powershell
# 编译（需要 Windows 自带的 .NET Framework 4.x，无需 VS / dotnet SDK）
pwsh -File .\build.ps1

# 运行
.\CHESUNUninstaller.exe
```

- 程序启动时检测权限，非管理员会自动弹 UAC 重新拉起（`runas`）；因此驱动清理与还原点按钮开箱即用。
- `app.manifest` 仍声明 `asInvoker`，提权由程序自身完成，兼容从沙盒 / 受限环境启动。
- 运行数据：`%LOCALAPPDATA%\CHESUN Uninstaller\`（`Backups` / `Reports` / `Logs` / `settings.ini` / `whitelist.txt`）。

---

## 5. 运行验证（Windows Sandbox）

宿主启用了 WDAC 企业策略，直接运行未签名 exe 会被「应用程序控制策略已阻止此文件」拦截（CodeIntegrity `3033`/`3077`，Policy ID `{e060f2f6-d742-4729-8cdb-c2…}`）。因此改为在 **Windows Sandbox** 内验证，宿主策略在沙盒内不生效。

```powershell
# 只读映射工程目录，登录后自动启动程序
WindowsSandbox.exe .\run-in-sandbox.wsb
```

沙盒内实测结果（截图见 `sandbox-shots\`）：

- 启动页：310×310 / 310×150 / 150×150 磁贴按 Metro 网格排列，软件数与可疑驱动数实时回填
- 软件卸载页（`software.png`）：读取到沙盒内 3 个卸载项（Microsoft Edge / Edge Update / WebView2），搜索栏、「一键深度扫描」、刷新、长条磁贴行、右侧滑入详情均正常；行内含图标、名称、发布者、版本、安装日期、路径、体积
- 驱动管理页（`driver.png`）：读取 394 个内核驱动，可疑 0、可清理 0；每行展示 `ImagePath`、加载级别、运行状态、`受保护` 色块与签名状态（Microsoft 签名 / 无签名）；筛选与「清理可归属驱动」按钮可用

实机验证中发现并修复的两个问题：

1. **首屏空白**——`MainForm` 只在 `OnResize` 排版，而构造函数先设窗口尺寸、后加控件，`_header`/`_content`/`_status` 停在默认 200×100。已抽出 `LayoutUi()`，构造函数末尾与 `OnShown` 各调用一次。
2. **中文标题显示方框**——`Segoe UI Light` 无 CJK 字形，`TextRenderer` 不回退。`Theme.Display()` 改用 `Microsoft YaHei UI Light`。

当前构建 SHA256：

```
13CA09A02B57B1EDAA407B3E7DFA7F9860000460AC1112D7A3032E4CECD656CD
```

宿主直接运行仍需签名或 WDAC 补充策略放行；沙盒 / 虚拟机内无需放行。

---

## 6. 已知边界

- 不处理 MSIX / AppX 商店应用（只覆盖 Win32 卸载项）。
- WHQL 目录签名（catalog 签名）未做 catalog 校验；对无嵌入签名的系统驱动，靠「Windows 目录 + 关键服务名 + Boot/System 级」三层保护兜底。
- 卸载程序若是交互式 GUI 且无静默参数，会等待用户操作，默认 600 秒超时后强制结束并继续清理。
- 计划任务按 `Tasks` 目录中的相对名称调用 `schtasks /delete`，个别系统任务可能受权限限制。
- 系统还原点依赖「系统保护」开启与 VSS 服务可用；不可用时会在流程中告警并继续（仍保留文件/注册表备份）。
- Office 等共享型产品键被列为受保护根，不会整根删除，因此这类软件的残留清理偏保守。
- 驱动归属是启发式打分，阈值 45 分；不确定时宁可漏删，不可误删。
- **已映射进内核且没有 `DriverUnload` 的驱动，运行期无法安全 unlink**：工具走「禁用启动 + 删服务 + 删文件 + 重启删除」，重启后不再加载；要立即从内存移除只能进 WinPE 离线清理（本工具可生成脚本）。
- 不实现自写内核驱动篡改 `DriverObject` / 摘内核回调 / 复制 SYSTEM 与 TrustedInstaller 令牌 / BYOVD 漏洞驱动利用：这类做法属于 rootkit 手法，会被 HVCI、PatchGuard、驱动签名与安全软件拦截，且极易蓝屏。
- ESP 扫描依赖 `mountvol S: /S`（需管理员且可能与其他卷冲突）；Legacy/BIOS 机器会返回「EFI 系统分区未挂载」。
- `clean-efi.cmd` 删除 `bootmgfw.efi` 后需手动 `bcdboot` 修复引导，脚本只提示不自动执行。
- WDAC 策略只生成不自动部署：部署前需在测试机确认，避免误拦导致无法启动。
- SPI 固件 / UEFI 变量级植入（真正 Bootkit）不在工具范围，需刷固件或厂商修复。
- 当前发布包未做 Authenticode 签名：宿主 WDAC 可能直接拦截。程序只能弹窗并提供自签名工具包，不代替用户签系统级证书，也不会自动安装 Windows SDK。
- `testsigning` 只用于未签名内核驱动的测试，不能让未签名 exe 过 WDAC；启用后内核签名强制被放宽，测试完应关闭并重启。







---

## 7. v1.1 变更（2026-11-26）

- 产品更名为 **CHESUN Uninstaller**（可执行文件 `CHESUNUninstaller.exe`，用户数据目录 `%LOCALAPPDATA%\CHESUN Uninstaller\`）。
- 卸载流程健壮性：
  - MSI 卸载项统一归一化为 `msiexec.exe /x {ProductCode} /qn /norestart`，兼容只写 `/I` 或只给 GUID 的注册表项。
  - 没有 `QuietUninstallString` 的程序改为**可见窗口**启动卸载程序，给足用户点击时间（默认至少 300 秒），不再因隐藏窗口等待而"假死"超时。
  - 明确处理退出码：`0` / `3010` / `1641` 视为成功（后两者需重启），`1605` / `1614` 视为"已不存在"，`1602` 视为用户取消，均继续清理残留而不是中止。
  - 卸载程序路径不存在时直接跳过并在日志说明，不再卡在启动失败。
  - 残留清理顺序改为 **服务 → 注册表/任务/环境变量 → 文件 → 目录**，避免目录先删导致子项被锁。
  - 文件/目录删除被拒绝时，自动 `takeown` + `icacls` 接管所有权后重试一次，仍失败才登记"重启后删除"。
- 首屏自绘控件未排版（空白窗口）与中文标题方框（Segoe UI Light 缺字）两处修复。

---

## 7.1 v1.2 变更（2026-11-26，强制驱动移除）

- **启动即提权**：非管理员启动时自动 `runas` 重新拉起自己，避免“驱动清理不可用”。
- **运行中驱动强制移除流程**：`sc config start= disabled` → 轮询 `sc stop`（最多约 60 秒）→ `sc delete`（接受 1072 已标记删除）→ 删文件 → 仍失败则登记 `PendingFileRenameOperations` 重启删除 → 清理 Services 键。
- **SYSTEM 权限兜底**（新增 `src/Services/SystemRunner.cs`）：通过临时计划任务以 `LocalSystem` 运行 `takeown /f` + `icacls /grant *S-1-5-32-544:F` + `del`，处理 TrustedInstaller / SYSTEM ACL 拒绝；不伪造令牌，走 Windows 原生 SYSTEM 账户。
- **DriverStore 驱动包清理**：从 `FileRepository\<oemXX.inf>_amd64_*` 解析 INF 名，执行 `pnputil /delete-driver <inf> /uninstall /force`，避免设备管理器再自动装回。
- **内核占用是硬限制**：已映射进内核的驱动镜像无法在运行期 unlink，必须重启；工具保证重启前服务已被禁用/删除，重启时文件被删除，之后不再加载。
- 仍保留安全红线：系统原生、Microsoft 签名、Boot/System 级、关键服务名驱动不进入删除队列。

---

## 7.2 v1.3 变更（2026-11-26，反 Rootkit / 反 Bootkit）

- 新增 `src/Services/AntiRootkit.cs`：
  - `ScanRootkit`：隐藏服务（Start=4 仍运行）、文件缺失、无签名且不在系统目录、位于 Temp/AppData、非微软签名的 Boot/System 级驱动。
  - `ScanBootkit`：Secure Boot 状态、`bcdedit /enum all` 非标准 EFI 路径、ESP `\EFI` 下二进制签名校验。
  - `BuildOfflinePack`：生成 WinPE 离线清理包（服务键备份/禁用/删除、sys 与 DriverStore 删除、EFI 可疑文件备份删除、`bcdboot` 修复提示）。
  - `BuildWadcDeny`：调用 ConfigCI 生成按哈希 Deny 的 `policy.cip` 与 `deploy-wdac.cmd`。
- 驱动页新增「反 Rootkit / Bootkit 扫描」入口，详情面板新增「卸载该驱动 / WDAC 拉黑 / 离线清理包」三个按钮。
- WDAC 生成修正：先把 `AllowAll.xml` 复制到输出目录再 `Merge-CIPolicy`，避免直接读写 `%WINDIR%\schemas` 被拒绝。
- 离线脚本修正：从离线 SYSTEM 的 `Select\Current` 推导 `ControlSet00N`，不再硬编码 `ControlSet001`；EFI 清理同时纳入「高 / 中」级 ESP 命中项。
- README 补齐反 Rootkit / Bootkit 能力、已知边界与 v1.3 说明。

---

## 7.3 v1.4 变更（2026-11-26，签名 / 测试签名助手）

- 新增 `src/Services/SigningHelper.cs`：签名状态描述、`BuildKit` 生成签名工具包、读取与设置 `testsigning`。
- 启动检测自身签名；未签名 / 证书链不可信时弹出 Metro 覆盖层，列出本机自签名、正式证书、测试签名、沙盒四条路径。
- 签名工具包：`make-test-sign.ps1` 自动请求 UAC、创建自签名代码签名证书、导入当前用户受信任根/受信任发布者、签出签名副本并 `signtool verify`；同时提供 `check-signature.ps1`、`run-signed.cmd`、`enable-testsigning.cmd`、`disable-testsigning.cmd`、`README.txt`。
- 设置页新增「代码签名 / 测试签名」与「测试签名（仅内核驱动）」两行；后者可直接启用/关闭 `testsigning`，启用按钮为红色并写明副作用。
- 明确边界：`testsigning` 只影响内核驱动，不能放行未签名用户态 exe；自签名证书只在本机可信。

---

## 7.4 v1.5 变更（2026-11-26，内核模块枚举与 BCD 清理）

- `ScanLoadedModules`：用 `NtQuerySystemInformation(SystemModuleInformation)` 枚举已加载内核模块（含完整路径），与驱动服务表交叉比对，发现“服务键已删但仍在内存运行”的隐藏驱动；非可信签名标红，Windows 目录外的有效签名驱动标为待确认。
- `ScanBootkit`：ESP 改为遍历整块分区（不再只看 `\EFI`）；BCD 非标准 `.efi` 路径带上启动项 GUID。
- `BuildOfflinePack`：新增 `clean-bcd.cmd`（有非标准 BCD 项时生成），先 `bcdedit /export` 备份，再按 GUID 删除，最后提示 `bcdboot` 修复；EFI 路径解析改为按 `ESP:` 标记截取，支持带空格的路径。
- 仍不实现：运行期无 `DriverUnload` 驱动的安全 unlink、内核回调/SSDT 摘除、复制 SYSTEM/TrustedInstaller 令牌、BYOVD。上述属于内核 rootkit 手法，HVCI/PatchGuard/签名策略会拦，且蓝屏风险极高。

---

## 8. 许可证

本项目采用 **BSD 3-Clause License**，全文见仓库根目录 [LICENSE](LICENSE)。

Copyright (c) 2026 CHESUN。允许在保留版权声明、条件列表与免责声明的前提下自由使用、修改与再分发（含商业用途）；未经书面许可不得使用版权人或贡献者的名义为衍生产品背书。软件按“现状”提供，不附带任何明示或默示担保。
