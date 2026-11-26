# CHESUN 反 Rootkit 驱动构建脚本
# 需要：Visual Studio Build Tools（MSVC x64）+ Windows Driver Kit (WDK)
# 用法：pwsh -File build-driver.ps1

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'chesunark.c'
if (-not (Test-Path -LiteralPath $src)) { throw "找不到 $src" }

function Find-Cl {
    $cmd = Get-Command cl.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
        if ($vs) {
            $hit = Get-ChildItem -LiteralPath (Join-Path $vs 'VC\Tools\MSVC') -Filter cl.exe -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\bin\Hostx64\x64\cl.exe$' } | Select-Object -First 1
            if ($hit) { return $hit.FullName }
        }
    }
    return $null
}

function Find-Wdk {
    foreach ($base in @((Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'), (Join-Path $env:ProgramFiles 'Windows Kits\10'))) {
        if (-not (Test-Path -LiteralPath $base)) { continue }
        $incRoot = Join-Path $base 'Include'
        $libRoot = Join-Path $base 'Lib'
        if (-not (Test-Path -LiteralPath $incRoot)) { continue }
        $ver = Get-ChildItem -LiteralPath $incRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if (-not $ver) { continue }
        $km = Join-Path $ver.FullName 'km'
        $shared = Join-Path $ver.FullName 'shared'
        $lib = Join-Path (Join-Path $libRoot $ver.Name) 'km\x64'
        if ((Test-Path -LiteralPath $km) -and (Test-Path -LiteralPath $lib)) {
            return [pscustomobject]@{ IncludeKm = $km; IncludeShared = $shared; Lib = $lib; Version = $ver.Name }
        }
    }
    return $null
}

$cl = Find-Cl
if (-not $cl) { throw '未找到 cl.exe。请安装 Visual Studio Build Tools（C++ x64）后重开终端。' }
$wdk = Find-Wdk
if (-not $wdk) { throw '未找到 WDK 10 的 km 头文件/库。请安装 Windows Driver Kit。' }

$link = Join-Path (Split-Path $cl) 'link.exe'
if (-not (Test-Path -LiteralPath $link)) { throw "找不到 link.exe：$link" }

$obj = Join-Path $root 'chesunark.obj'
$sys = Join-Path $root 'chesunark.sys'
Remove-Item -LiteralPath $obj, $sys -Force -ErrorAction SilentlyContinue

$cargs = @(
    '/nologo', '/c', '/kernel', '/GS-', '/O2', '/W3',
    '/D_AMD64_', '/DAMD64', '/DWIN32=100', '/D_WIN64',
    '/I' + $wdk.IncludeKm,
    '/I' + $wdk.IncludeShared,
    '/Fo' + $obj,
    $src
)
& $cl @cargs
if ($LASTEXITCODE -ne 0) { throw "cl 编译失败，退出码 $LASTEXITCODE" }

$largs = @(
    '/nologo', '/DRIVER', '/SUBSYSTEM:NATIVE', '/ENTRY:DriverEntry',
    '/OUT:' + $sys,
    $obj,
    (Join-Path $wdk.Lib 'ntoskrnl.lib'),
    (Join-Path $wdk.Lib 'hal.lib'),
    (Join-Path $wdk.Lib 'wdmsec.lib'),
    (Join-Path $wdk.Lib 'BufferOverflowK.lib')
)
& $link @largs
if ($LASTEXITCODE -ne 0) { throw "link 失败，退出码 $LASTEXITCODE" }

Write-Host ('完成：' + $sys)
Write-Host '下一步：在 CHESUN Uninstaller → 设置 → “反 Rootkit 驱动签名”里选择这个 .sys。'
