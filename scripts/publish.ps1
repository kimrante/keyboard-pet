<#
.SYNOPSIS
  Keyboard Pet을 배포용으로 게시한다.

.DESCRIPTION
  기본값은 자체 포함(self-contained) 단일 exe이며 .NET 런타임 설치가 필요 없다.
  -Compress            exe를 압축한다(약 65MB). 대신 실행 중 메모리를 약 70MB 더 쓴다(기본은 비압축, 약 145MB).
  -FrameworkDependent  .NET 10 Desktop Runtime이 설치된 PC용 소형 exe를 만든다.
  -Portable            단일 파일이 아닌 폴더 형태(자체 포함)로 게시해 zip으로 묶는다.
                       임시 폴더에 네이티브 DLL을 추출하지 않으므로 백신·AppLocker·TEMP 정책으로
                       단일 exe가 실행되지 않는 PC에서 쓴다.

.EXAMPLE
  .\scripts\publish.ps1
  .\scripts\publish.ps1 -Compress
  .\scripts\publish.ps1 -FrameworkDependent
  .\scripts\publish.ps1 -Portable
#>
[CmdletBinding()]
param(
    [switch]$Compress,
    [switch]$FrameworkDependent,
    [switch]$Portable,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\KeyboardPet.App\KeyboardPet.App.csproj"
$selfContained = -not $FrameworkDependent
$suffix = if ($Portable) { "-portable" } elseif ($FrameworkDependent) { "-fdd" } elseif ($Compress) { "-compressed" } else { "" }
$publishDir = Join-Path $root "artifacts\$Runtime$suffix"

Write-Host "Publishing Keyboard Pet ($Runtime, self-contained=$selfContained, portable=$($Portable.IsPresent), compress=$($Compress.IsPresent)) -> $publishDir"

if ($Portable) {
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    dotnet publish $project `
        -c Release `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=true `
        -p:DebugType=embedded `
        -o $publishDir
} else {
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
}

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if ($Portable) {
    $version = (Select-String -Path $project -Pattern '<Version>(.+?)</Version>' -Encoding UTF8 | Select-Object -First 1).Matches[0].Groups[1].Value
    $zip = Join-Path $root "artifacts\KeyboardPet-$version-$Runtime-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip -CompressionLevel Optimal
    $fileCount = (Get-ChildItem $publishDir -File -Recurse).Count
    "{0,-60} {1,10:N1} MB  ({2} files inside)" -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB), $fileCount
} else {
    Get-ChildItem $publishDir -File | ForEach-Object {
        "{0,-40} {1,10:N1} MB" -f $_.Name, ($_.Length / 1MB)
    }
}
