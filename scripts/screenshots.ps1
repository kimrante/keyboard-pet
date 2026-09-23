<#
.SYNOPSIS
  빌드된 Keyboard Pet을 데모 설정으로 실행해 스크린샷을 만든다(CI의 Windows 러너에서도 동작).

.DESCRIPTION
  앱을 --screenshot 모드로 실행하면 펫 창(효과가 시간에 따라 움직이는 연속 컷, 키 규칙 효과)과
  설정 창 탭들을 PNG로 저장한다. 앱이 준비 신호(ready.flag)를 남기면 이 스크립트가 화면 전체도 찍는다.
  설정은 임시 폴더(--data-dir)에서 읽으므로 실제 사용자 설정은 건드리지 않는다.

.EXAMPLE
  dotnet build src/KeyboardPet.App -c Release
  .\scripts\screenshots.ps1 -OutDir screenshots
#>
[CmdletBinding()]
param(
    [string]$OutDir = "screenshots",
    [string]$Exe = "",
    [string]$Settings = "",
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $root "src\KeyboardPet.App\bin\Release\net10.0-windows\KeyboardPet.exe" }
if (-not $Settings) { $Settings = Join-Path $PSScriptRoot "screenshot-settings.json" }
if (-not (Test-Path $Exe)) { throw "실행 파일이 없습니다: $Exe (먼저 dotnet build -c Release)" }

$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir | Out-Null

$dataDir = Join-Path ([IO.Path]::GetTempPath()) ("KeyboardPet-shots-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $dataDir | Out-Null
Copy-Item $Settings (Join-Path $dataDir "settings.json")

Write-Host "Launching $Exe"
$proc = Start-Process -FilePath $Exe -PassThru -ArgumentList @("--screenshot", "`"$OutDir`"", "--data-dir", "`"$dataDir`"", "--no-hook", "--no-tray")

$ready = Join-Path $OutDir "ready.flag"
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not (Test-Path $ready) -and -not $proc.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }

if (Test-Path $ready) {
    try {
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bmp.Save((Join-Path $OutDir "desktop.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
        Write-Host "Captured desktop.png ($($bounds.Width)x$($bounds.Height))"
    }
    catch {
        Write-Warning "화면 전체 캡처 실패(데스크톱 세션이 없는 환경일 수 있음): $_"
    }
    New-Item -ItemType File -Path (Join-Path $OutDir "done.flag") | Out-Null
}
else {
    Write-Warning "앱이 준비 신호를 남기지 않았습니다."
}

if (-not $proc.WaitForExit(60000)) { $proc.Kill(); throw "앱이 종료되지 않았습니다." }

# 진단 로그(무인 실행 중 생략된 메시지 포함)를 함께 남긴다.
foreach ($name in "startup.log", "crash.log") {
    $log = Join-Path $env:LOCALAPPDATA "KeyboardPet\$name"
    if (Test-Path $log) { Copy-Item $log (Join-Path $OutDir $name) }
}

Remove-Item (Join-Path $OutDir "*.flag") -ErrorAction SilentlyContinue
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue

$pngs = Get-ChildItem $OutDir -Filter *.png
$pngs | ForEach-Object { "{0,-28} {1,8:N0} KB" -f $_.Name, ($_.Length / 1KB) }
if ($proc.ExitCode -ne 0) { throw "앱 종료 코드 $($proc.ExitCode)" }
if ($pngs.Count -lt 3) { throw "스크린샷이 만들어지지 않았습니다." }
