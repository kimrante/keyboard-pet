<#
.SYNOPSIS
  Keyboard Pet을 단일 실행 파일로 게시한다.

.DESCRIPTION
  기본값은 자체 포함(self-contained) 단일 exe이며 .NET 런타임 설치가 필요 없다.
  -Compress            exe를 압축한다(약 65MB). 대신 실행 중 메모리를 약 70MB 더 쓴다(기본은 비압축, 약 145MB).
  -FrameworkDependent  .NET 10 Desktop Runtime이 설치된 PC용 소형 exe를 만든다.

.EXAMPLE
  .\scripts\publish.ps1
  .\scripts\publish.ps1 -Compress
  .\scripts\publish.ps1 -FrameworkDependent
#>
[CmdletBinding()]
param(
    [switch]$Compress,
    [switch]$FrameworkDependent,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\KeyboardPet.App\KeyboardPet.App.csproj"
$selfContained = -not $FrameworkDependent
$suffix = if ($FrameworkDependent) { "-fdd" } elseif ($Compress) { "-compressed" } else { "" }
$publishDir = Join-Path $root "artifacts\$Runtime$suffix"

Write-Host "Publishing Keyboard Pet ($Runtime, self-contained=$selfContained, compress=$($Compress.IsPresent)) -> $publishDir"

dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=$selfContained `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=$($Compress.IsPresent) `
    -p:DebugType=embedded `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Get-ChildItem $publishDir -File | ForEach-Object {
    "{0,-40} {1,10:N1} MB" -f $_.Name, ($_.Length / 1MB)
}
