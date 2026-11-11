<#
.SYNOPSIS
  Builds a GroceryPOS release: tests, publish, portable zip and the Windows installer.

.EXAMPLE
  ./build/publish.ps1                       # self-contained exe + portable zip + GroceryPOS-Setup-<version>.exe
  ./build/publish.ps1 -FrameworkDependent   # smaller build that needs the .NET Desktop Runtime 8.0 (x64)
  ./build/publish.ps1 -SkipTests -SkipInstaller

.NOTES
  Output goes to artifacts/: publish/<profile>/, GroceryPOS-<version>-portable.zip and installer/.
  The installer needs Inno Setup 6.3+ (winget install JRSoftware.InnoSetup); without it the step is skipped with a warning.
  The version comes from <Version> in Directory.Build.props.
#>
[CmdletBinding()]
param(
    [switch] $FrameworkDependent,
    [switch] $SkipTests,
    [switch] $SkipInstaller,
    [switch] $SkipPortable
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$profileName = if ($FrameworkDependent) { 'win-x64-framework' } else { 'win-x64' }
$publishDir = Join-Path $artifacts "publish/$profileName"

[xml] $props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Không đọc được <Version> trong Directory.Build.props.' }
Write-Host "GroceryPOS $version ($profileName)" -ForegroundColor Green

function Invoke-Step([string] $title, [scriptblock] $action) {
    Write-Host "==> $title" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "$title thất bại (mã $LASTEXITCODE)." }
}

if (-not $SkipTests) {
    Invoke-Step 'dotnet test' { dotnet test (Join-Path $root 'GroceryPOS.sln') -c Release --nologo }
}

Invoke-Step "dotnet publish ($profileName)" {
    dotnet publish (Join-Path $root 'src/GroceryPOS.App/GroceryPOS.App.csproj') "-p:PublishProfile=$profileName" --nologo
}

if (-not $SkipPortable -and -not $FrameworkDependent) {
    Invoke-Step 'Portable zip' {
        $staging = Join-Path $artifacts "portable/GroceryPOS-$version"
        if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
        New-Item -ItemType Directory -Path $staging | Out-Null
        Copy-Item (Join-Path $publishDir '*') $staging -Recurse
        # The marker makes the app keep its data in .\data beside the exe (USB stick / no-install use).
        Set-Content -Path (Join-Path $staging 'portable.txt') -Encoding UTF8 -Value @(
            'GroceryPOS bản di động: dữ liệu (cơ sở dữ liệu, log, sao lưu) nằm trong thư mục "data" cạnh file GroceryPOS.exe.',
            'Xóa file này để dùng thư mục %LocalAppData%\GroceryPOS như bản cài đặt.')
        $zip = Join-Path $artifacts "GroceryPOS-$version-portable.zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path $staging -DestinationPath $zip
        Write-Host "    $zip"
    }
}

if (-not $SkipInstaller) {
    $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    if (-not $iscc) {
        $iscc = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    }

    if (-not $iscc) {
        Write-Warning 'Không tìm thấy Inno Setup 6 (ISCC.exe) – bỏ qua bộ cài. Cài bằng: winget install JRSoftware.InnoSetup'
    }
    else {
        $frameworkFlag = if ($FrameworkDependent) { '1' } else { '0' }
        Invoke-Step 'Inno Setup' {
            & $iscc "/DAppVersion=$version" "/DSourceDir=$publishDir" "/DFrameworkDependent=$frameworkFlag" (Join-Path $root 'installer/GroceryPOS.iss')
        }
    }
}

Write-Host "Xong. Kết quả trong $artifacts" -ForegroundColor Green
