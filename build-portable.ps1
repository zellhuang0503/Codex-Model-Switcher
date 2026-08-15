[CmdletBinding()]
param(
    [string]$DotnetPath = "dotnet"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$releaseRoot = Join-Path $projectRoot "release"
$workRoot = Join-Path $releaseRoot "work"
$publishRoot = Join-Path $workRoot "publish"
$packageRoot = Join-Path $releaseRoot "Codex-Model-Switcher"
$zipPath = Join-Path $releaseRoot "Codex-Model-Switcher-Windows-x64-Preview.zip"
$projectFile = Join-Path $projectRoot "CodexModelSwitcher.csproj"
$catalogFile = Join-Path $projectRoot "providers.json"
$guideFile = Join-Path $projectRoot "使用說明.html"

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Parent
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒絕操作專案外的路徑：$fullPath"
    }
}

function Remove-BuildTarget {
    param([Parameter(Mandatory)] [string]$Path)

    Assert-ChildPath -Path $Path -Parent $projectRoot
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

foreach ($requiredFile in @($projectFile, $catalogFile, $guideFile)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "缺少發佈必要檔案：$([System.IO.Path]::GetFileName($requiredFile))"
    }
}

Get-Content -LiteralPath $catalogFile -Raw | ConvertFrom-Json | Out-Null

Remove-BuildTarget -Path $workRoot
Remove-BuildTarget -Path $packageRoot
Assert-ChildPath -Path $zipPath -Parent $projectRoot
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

$publishArguments = @(
    "publish",
    $projectFile,
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--output", $publishRoot,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

& $DotnetPath @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw ".NET 發佈失敗，結束碼：$LASTEXITCODE"
}

$publishedExe = Join-Path $publishRoot "CodexModelSwitcher.exe"
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "發佈結果缺少 CodexModelSwitcher.exe。"
}

Copy-Item -LiteralPath $publishedExe -Destination $packageRoot
Copy-Item -LiteralPath $catalogFile -Destination $packageRoot
Copy-Item -LiteralPath $guideFile -Destination $packageRoot

$expectedFiles = @(
    "CodexModelSwitcher.exe",
    "providers.json",
    "使用說明.html"
)
# Windows PowerShell 5.1 沒有 Path.GetRelativePath，改用字首截斷計算相對路徑。
$fullPackageRoot = [System.IO.Path]::GetFullPath($packageRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$actualFiles = Get-ChildItem -LiteralPath $packageRoot -File -Recurse | ForEach-Object {
    $_.FullName.Substring($fullPackageRoot.Length)
}
$unexpectedFiles = $actualFiles | Where-Object { $_ -notin $expectedFiles }
$missingFiles = $expectedFiles | Where-Object { $_ -notin $actualFiles }
if ($unexpectedFiles -or $missingFiles) {
    throw "發佈內容不符合白名單。多餘：$($unexpectedFiles -join ', ')；缺少：$($missingFiles -join ', ')"
}

$forbiddenNames = Get-ChildItem -LiteralPath $packageRoot -Recurse -Force | Where-Object {
    $_.Name -match '(^|\.)env($|\.)|credential|credentials|\.pfx$|\.p12$|\.pem$|\.key$|\.pdb$|^\.git$|^bin$|^obj$'
}
if ($forbiddenNames) {
    throw "發佈內容含禁止檔名：$($forbiddenNames.Name -join ', ')"
}

$secretPatterns = @(
    'sk-[A-Za-z0-9_-]{12,}',
    'AIza[A-Za-z0-9_-]{20,}',
    '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
)
$binarySecretPatterns = @(
    'sk-(?:(?:proj|svcacct)-[A-Za-z0-9_-]{20,}|[A-Za-z0-9]{20,})',
    'AIza[A-Za-z0-9_-]{20,}',
    '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
)
$textFiles = @(
    (Join-Path $packageRoot "providers.json"),
    (Join-Path $packageRoot "使用說明.html")
)
foreach ($pattern in $secretPatterns) {
    $matches = Select-String -LiteralPath $textFiles -Pattern $pattern -AllMatches
    if ($matches) {
        throw "發佈文字檔疑似包含敏感內容，已停止打包。"
    }
}

$exeBytes = [System.IO.File]::ReadAllBytes((Join-Path $packageRoot "CodexModelSwitcher.exe"))
$exeAscii = [System.Text.Encoding]::ASCII.GetString($exeBytes)
$exeUnicode = [System.Text.Encoding]::Unicode.GetString($exeBytes)
try {
    foreach ($pattern in $binarySecretPatterns) {
        if ([regex]::IsMatch($exeAscii, $pattern) -or [regex]::IsMatch($exeUnicode, $pattern)) {
            throw "發佈執行檔疑似包含敏感內容，已停止打包。"
        }
    }
}
finally {
    $exeAscii = $null
    $exeUnicode = $null
    [Array]::Clear($exeBytes, 0, $exeBytes.Length)
}

Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $zipEntries = $archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) }
    if ($zipEntries.Count -ne $expectedFiles.Count) {
        throw "ZIP 內容數量不正確。"
    }

    foreach ($expected in $expectedFiles) {
        # Windows PowerShell 5.1 的 Compress-Archive 以反斜線分隔壓縮項目路徑，比對前先統一為正斜線。
        if (-not ($zipEntries | Where-Object { ($_.FullName -replace '\\', '/') -eq "Codex-Model-Switcher/$expected" })) {
            throw "ZIP 缺少必要檔案：$expected"
        }
    }
}
finally {
    $archive.Dispose()
}

Remove-BuildTarget -Path $workRoot

$zip = Get-Item -LiteralPath $zipPath
$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
Write-Host "WINDOWS_PORTABLE_PREVIEW_OK"
Write-Host "ZIP：$($zip.FullName)"
Write-Host "大小：$($zip.Length) bytes"
Write-Host "SHA256：$($hash.Hash)"
