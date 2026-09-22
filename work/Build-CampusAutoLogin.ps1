$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'CampusAutoLogin.cs'
$portalSource = Join-Path $PSScriptRoot 'WhutPortalClient.cs'
$wifiSource = Join-Path $PSScriptRoot 'CampusWifi.cs'
$output = Join-Path (Split-Path $PSScriptRoot -Parent) 'outputs\CampusFlow-WHUT.exe'
$buildOutput = Join-Path (Split-Path $PSScriptRoot -Parent) 'outputs\CampusFlow-WHUT.new.exe'
$icon = Join-Path $PSScriptRoot 'assets\CampusFlow.ico'
New-Item -ItemType Directory -Path (Split-Path $output -Parent) -Force | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
& $csc /nologo /codepage:65001 /target:winexe /out:$buildOutput /win32icon:$icon /reference:System.dll,System.Core.dll,System.Drawing.dll,System.Security.dll,System.Xml.dll,System.Windows.Forms.dll,System.Web.Extensions.dll $source $portalSource $wifiSource
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
Move-Item -LiteralPath $buildOutput -Destination $output -Force

Write-Host "Built: $output"
