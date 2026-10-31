param(
    [string]$Version = "1.2.0"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ExtDir = Join-Path $ProjectRoot "browser-extension"
$DistDir = Join-Path $ProjectRoot "dist"
$ObjDir = Join-Path $ProjectRoot "obj"

New-Item -ItemType Directory -Force -Path $DistDir, $ObjDir | Out-Null

$RequiredFiles = @(
    "manifest.json",
    "background.js",
    "popup.html",
    "popup.css",
    "popup.js",
    "shihab_countries.js",
    "shihab_globe.js",
    "icons\icon16.png",
    "icons\icon32.png",
    "icons\icon48.png",
    "icons\icon128.png"
)

Write-Host "[1/3] Validating Manifest V3 extension files..."
foreach ($rel in $RequiredFiles) {
    $full = Join-Path $ExtDir $rel
    if (-not (Test-Path -LiteralPath $full)) {
        throw "Missing required browser extension file: $full"
    }
}

$manifestObj = Get-Content -LiteralPath (Join-Path $ExtDir "manifest.json") -Raw | ConvertFrom-Json
if ($manifestObj.manifest_version -ne 3) {
    throw "Expected manifest_version 3 in browser-extension\manifest.json"
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$OutZip = Join-Path $DistDir "Shihab-Shield-VPN-Browser-Extension-$Version.zip"
if (Test-Path -LiteralPath $OutZip) {
    Remove-Item -LiteralPath $OutZip -Force
}

Write-Host "[2/3] Creating Chrome & Brave Web Store / Developer Zip ($OutZip)..."
$zipArchive = [System.IO.Compression.ZipFile]::Open($OutZip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $allFiles = Get-ChildItem -LiteralPath $ExtDir -Recurse -File
    foreach ($file in $allFiles) {
        $relPath = $file.FullName.Substring($ExtDir.Length).TrimStart('\', '/').Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zipArchive,
            $file.FullName,
            $relPath,
            [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
}
finally {
    $zipArchive.Dispose()
}

$ZipInfo = Get-Item -LiteralPath $OutZip
$ZipSha = (Get-FileHash -LiteralPath $OutZip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("      Built Extension ZIP: {0} ({1:N1} KB, SHA-256: {2})" -f $OutZip, ($ZipInfo.Length / 1KB), $ZipSha)

Write-Host "[3/3] Packing signed .crx3 package via Chromium..."
$BrowserBin = $null
foreach ($cand in @(
    "C:\Program Files\Google\Chrome\Application\chrome.exe",
    "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    "C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe"
)) {
    if (Test-Path -LiteralPath $cand) {
        $BrowserBin = $cand
        break
    }
}

$OutCrx = Join-Path $DistDir "Shihab-Shield-VPN-Browser-Extension-$Version.crx"
if ($BrowserBin) {
    $KeyPem = Join-Path $ObjDir "shihab_browser_ext.pem"
    $TempStageExt = Join-Path $ObjDir "crx_pack_stage"
    $TempStageCrx = "$TempStageExt.crx"
    $TempStagePem = "$TempStageExt.pem"

    if (Test-Path -LiteralPath $TempStageExt) { Remove-Item -LiteralPath $TempStageExt -Recurse -Force }
    if (Test-Path -LiteralPath $TempStageCrx) { Remove-Item -LiteralPath $TempStageCrx -Force }
    if (Test-Path -LiteralPath $TempStagePem) { Remove-Item -LiteralPath $TempStagePem -Force }

    Copy-Item -LiteralPath $ExtDir -Destination $TempStageExt -Recurse -Force

    $packArgs = @("--headless=new", "--no-gpu", "--no-sandbox", "--pack-extension=$TempStageExt")
    if (Test-Path -LiteralPath $KeyPem) {
        $packArgs += "--pack-extension-key=$KeyPem"
    }

    $packProc = Start-Process -FilePath $BrowserBin -ArgumentList $packArgs -Wait -PassThru -WindowStyle Hidden
    if (Test-Path -LiteralPath $TempStagePem) {
        Move-Item -LiteralPath $TempStagePem -Destination $KeyPem -Force
    }
    if (Test-Path -LiteralPath $TempStageCrx) {
        Move-Item -LiteralPath $TempStageCrx -Destination $OutCrx -Force
        $CrxInfo = Get-Item -LiteralPath $OutCrx
        $CrxSha = (Get-FileHash -LiteralPath $OutCrx -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host ("      Built Extension CRX: {0} ({1:N1} KB, SHA-256: {2})" -f $OutCrx, ($CrxInfo.Length / 1KB), $CrxSha)
    } else {
        Write-Warning "Chromium exited with $($packProc.ExitCode) without emitting .crx; unpacked directory and .zip are ready."
    }

    if (Test-Path -LiteralPath $TempStageExt) {
        Remove-Item -LiteralPath $TempStageExt -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "      Chromium binary not found; skipped optional .crx pack (ZIP + unpacked extension ready)."
}

Write-Host "SUCCESS! Chrome & Brave extension artifacts ready in dist\ and browser-extension\."

