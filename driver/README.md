# CHESUN 反 Rootkit 内核驱动

`chesunark.sys` 是一个**独立**的内核驱动，不随 `CHESUNUninstaller.exe` 自动安装，也不使用它的签名。  
它只做三件事，全部是文档化 API：

1. `ZwUnloadDriver`：按服务名热卸载有 `DriverUnload` 例程的驱动；
2. `ZwDeleteKey`：删除 `Services\<name>` 键；
3. `ZwDeleteFile`：删除指定 NT 路径的驱动文件。

设备对象用 `IoCreateDeviceSecure` 创建，SDDL 只允许 SYSTEM 与 Administrators 打开，普通进程不能借这个驱动删文件/删服务。

明确不做：

- 不摘 `PsLoadedModuleList`；
- 不修改 `DriverObject` / `DriverSection`；
- 不摘 SSDT / inline hook / 内核回调；
- 不绕 PatchGuard / HVCI；
- 不伪装、不注入、不隐藏。

所以它不是“恶意驱动”，也不能把没有 `DriverUnload` 的 rootkit 强行 unlink。  
没有 Unload 的驱动，程序会走“禁用服务 → 停止服务 → 重启删除文件”；EFI 里的 bootkit 走 WinPE 离线包。

## 构建

需要 Visual Studio Build Tools + WDK 10：

```powershell
pwsh -File .\build-driver.ps1
```

输出：`chesunark.sys`。

## 测试签名（自己签，程序不替你签）

1. 打开 CHESUN Uninstaller → 设置 → **反 Rootkit 驱动签名**；
2. 选择 `chesunark.sys`，生成工具包；
3. 运行 `make-test-sign-driver.ps1`：自动创建本机测试证书、导入 `LocalMachine\Root` / `TrustedPublisher`、`signtool sign` + `verify`；
4. 运行 `enable-testsigning.cmd`，重启（桌面显示“测试模式”水印）；
5. 运行 `install-driver.cmd`，`sc create ChesunArk` 并 `sc start`；
6. 回到驱动页扫描，选中可疑驱动点“卸载该驱动”，程序会优先调用本驱动。

## 正式签名

生产环境必须用 EV/OV 代码签名证书，并按微软要求完成 WHQL / attestation 签名；自签名证书只在本机测试有效。

## 文件

- `chesunark.c` — 驱动源码
- `chesunark.inf` — 传统 INF（也可用 `sc create`）
- `build-driver.ps1` — WDK 构建脚本
