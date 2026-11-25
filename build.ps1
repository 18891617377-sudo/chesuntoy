# MetroUninstaller 构建脚本（.NET Framework 4.8 csc 直接编译，无需 Visual Studio）
# 用法：右键「使用 PowerShell 运行」，或 pwsh -File build.ps1

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) {
    throw '未找到 .NET Framework 4.x 编译器 csc.exe，请先启用 .NET Framework 4.8。'
}

$files = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -Filter *.cs |
    Sort-Object FullName | ForEach-Object { $_.FullName }

$args = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    ('/win32manifest:' + (Join-Path $root 'app.manifest')),
    ('/out:' + (Join-Path $root 'CHESUNUninstaller.exe')),
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll', '/r:System.Management.dll', '/r:System.Xml.dll',
    '/r:System.Runtime.Serialization.dll'
)

Write-Host ('编译 ' + $files.Count + ' 个源文件…')
& $csc @args $files
if ($LASTEXITCODE -ne 0) { throw ('编译失败，退出码 ' + $LASTEXITCODE) }
Write-Host ('完成：' + (Join-Path $root 'CHESUNUninstaller.exe'))

