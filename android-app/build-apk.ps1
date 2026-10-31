param(
    [string]$Version = "1.2.0"
)

$ErrorActionPreference = "Stop"

$AndroidDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $AndroidDir
$DistDir = Join-Path $ProjectRoot "dist"
$ObjDir = Join-Path $ProjectRoot "obj\android_build"

New-Item -ItemType Directory -Force -Path $DistDir, $ObjDir | Out-Null

# 1. Discover JDK (java, javac, jar, keytool) without installing global software
$JavacCmd = Get-Command javac -ErrorAction SilentlyContinue
$JavacExe = if ($JavacCmd) { $JavacCmd.Source } else { $null }
if (-not $JavacExe -and $env:JAVA_HOME) {
    $cand = Join-Path $env:JAVA_HOME "bin\javac.exe"
    if (Test-Path -LiteralPath $cand) { $JavacExe = $cand }
}
if (-not $JavacExe) {
    $portableJdk = Get-ChildItem -Path (Join-Path $ProjectRoot "obj\android-toolchain") -Recurse -Filter "javac.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($portableJdk) { $JavacExe = $portableJdk.FullName }
}

if (-not $JavacExe) {
    throw "JDK (javac.exe) not found. Install OpenJDK 17+ (or enable the Android CLI plugin) and re-run .\android-app\build-apk.ps1."
}

$JdkBin = Split-Path -Parent $JavacExe
$JavaExe = Join-Path $JdkBin "java.exe"
$JarExe = Join-Path $JdkBin "jar.exe"
$KeytoolExe = Join-Path $JdkBin "keytool.exe"
$env:JAVA_HOME = Split-Path -Parent $JdkBin
$env:PATH = "$JdkBin;$env:PATH"

# 2. Discover Android SDK (build-tools >= 34 & platforms >= android-34)
$SdkCandidates = @(
    $env:ANDROID_HOME,
    $env:ANDROID_SDK_ROOT,
    (Join-Path $env:LOCALAPPDATA "Android\Sdk"),
    (Join-Path $ProjectRoot "obj\android-toolchain\sdk")
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

$SdkRoot = $SdkCandidates | Select-Object -First 1
if (-not $SdkRoot) {
    throw "Android SDK not found (checked ANDROID_HOME, ANDROID_SDK_ROOT, %LOCALAPPDATA%\Android\Sdk). Configure Android CLI / Android SDK with platforms;android-34 and build-tools;34.0.0, then re-run .\android-app\build-apk.ps1."
}

$BuildToolsDir = Get-ChildItem -LiteralPath (Join-Path $SdkRoot "build-tools") -Directory -ErrorAction SilentlyContinue |
    Sort-Object { [version](($_.Name -replace '[^0-9\.]', '') + '.0' -replace '\.0\.0$', '.0') } -Descending |
    Select-Object -First 1

$Platform34 = Join-Path $SdkRoot "platforms\android-34"
if (Test-Path -LiteralPath $Platform34) {
    $PlatformDir = Get-Item -LiteralPath $Platform34
} else {
    $PlatformDir = Get-ChildItem -LiteralPath (Join-Path $SdkRoot "platforms") -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
}

if (-not $BuildToolsDir -or -not $PlatformDir) {
    throw "Android SDK at '$SdkRoot' is missing build-tools or platforms\android-34."
}

$Aapt2 = Join-Path $BuildToolsDir.FullName "aapt2.exe"
$ZipAlign = Join-Path $BuildToolsDir.FullName "zipalign.exe"
$D8Jar = Join-Path $BuildToolsDir.FullName "lib\d8.jar"
$D8Bat = Join-Path $BuildToolsDir.FullName "d8.bat"
$ApkSignerJar = Join-Path $BuildToolsDir.FullName "lib\apksigner.jar"
$ApkSignerBat = Join-Path $BuildToolsDir.FullName "apksigner.bat"
$AndroidJar = Join-Path $PlatformDir.FullName "android.jar"

foreach ($tool in @($Aapt2, $ZipAlign, $AndroidJar)) {
    if (-not (Test-Path -LiteralPath $tool)) {
        throw "Required Android SDK component missing: $tool"
    }
}

Write-Host "      Using JDK:         $JdkBin"
Write-Host "      Using Build-Tools: $($BuildToolsDir.FullName)"
Write-Host "      Using Platform:    $($PlatformDir.FullName)"

Write-Host "[1/6] Compiling Android resources via aapt2..."
$MainDir = Join-Path $AndroidDir "app\src\main"
$ResDir = Join-Path $MainDir "res"
$ManifestXml = Join-Path $MainDir "AndroidManifest.xml"
$JavaSrcDir = Join-Path $MainDir "java"

$ResZip = Join-Path $ObjDir "compiled_res.zip"
$GenJavaDir = Join-Path $ObjDir "gen_r"
$ClassesDir = Join-Path $ObjDir "classes"
$ClassesJar = Join-Path $ObjDir "classes.jar"
$SourcesList = Join-Path $ObjDir "javac_sources.txt"
$DexDir = Join-Path $ObjDir "dex"
$UnalignedApk = Join-Path $ObjDir "unaligned.apk"
$AlignedApk = Join-Path $ObjDir "aligned.apk"
$OutApk = Join-Path $DistDir "MASTER-VPN-PRO-Android-$Version.apk"

Remove-Item -LiteralPath $ObjDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $GenJavaDir, $ClassesDir, $DexDir | Out-Null

& $Aapt2 compile --dir $ResDir -o $ResZip
if ($LASTEXITCODE -ne 0) { throw "aapt2 compile failed with exit code $LASTEXITCODE" }

Write-Host "[2/6] Linking AndroidManifest.xml + resources..."
& $Aapt2 link `
    -I $AndroidJar `
    --manifest $ManifestXml `
    --java $GenJavaDir `
    --min-sdk-version 26 `
    --target-sdk-version 34 `
    --version-code 120 `
    --version-name $Version `
    -o $UnalignedApk `
    $ResZip
if ($LASTEXITCODE -ne 0) { throw "aapt2 link failed with exit code $LASTEXITCODE" }

Write-Host "[3/6] Compiling Java sources (com.shihab.shieldvpn)..."
$JavaFiles = @(
    Get-ChildItem -LiteralPath $JavaSrcDir -Recurse -Filter "*.java" | Select-Object -ExpandProperty FullName
    Get-ChildItem -LiteralPath $GenJavaDir -Recurse -Filter "*.java" | Select-Object -ExpandProperty FullName
)
$QuotedJavaFiles = $JavaFiles | ForEach-Object { '"' + ($_.Replace('\', '/')) + '"' }
Set-Content -LiteralPath $SourcesList -Value ($QuotedJavaFiles -join "`r`n") -Encoding ASCII

& $JavacExe -encoding UTF-8 -source 1.8 -target 1.8 -bootclasspath $AndroidJar -classpath $AndroidJar -d $ClassesDir "@$SourcesList"
if ($LASTEXITCODE -ne 0) { throw "javac compilation failed with exit code $LASTEXITCODE" }

& $JarExe cf $ClassesJar -C $ClassesDir .
if ($LASTEXITCODE -ne 0) { throw "jar packaging failed with exit code $LASTEXITCODE" }

Write-Host "[4/6] Compiling Dalvik bytecode (classes.dex) via d8..."
if (Test-Path -LiteralPath $D8Jar) {
    & $JavaExe -cp $D8Jar com.android.tools.r8.D8 --lib $AndroidJar --min-api 26 --output $DexDir $ClassesJar
} else {
    & $D8Bat --lib $AndroidJar --min-api 26 --output $DexDir $ClassesJar
}
if ($LASTEXITCODE -ne 0) { throw "d8 dexing failed with exit code $LASTEXITCODE" }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$apkArchive = [System.IO.Compression.ZipFile]::Open($UnalignedApk, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    $dexFile = Join-Path $DexDir "classes.dex"
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $apkArchive, $dexFile, "classes.dex", [System.IO.Compression.CompressionLevel]::Optimal
    ) | Out-Null
}
finally {
    $apkArchive.Dispose()
}

Write-Host "[5/6] Aligning APK on 4-byte boundaries via zipalign..."
if (Test-Path -LiteralPath $AlignedApk) { Remove-Item -LiteralPath $AlignedApk -Force }
& $ZipAlign -f -p 4 $UnalignedApk $AlignedApk
if ($LASTEXITCODE -ne 0) { throw "zipalign failed with exit code $LASTEXITCODE" }

Write-Host "[6/6] Signing APK (v1 + v2 + v3) via apksigner..."
$KeystorePath = Join-Path $ProjectRoot "obj\shihab_android_signing.jks"
if (-not (Test-Path -LiteralPath $KeystorePath)) {
    & $KeytoolExe -genkeypair -v `
        -keystore $KeystorePath `
        -alias shihabshield `
        -keyalg RSA -keysize 2048 -validity 10000 `
        -storepass shihabvpn120 -keypass shihabvpn120 `
        -dname "CN=Mehedi Hasan Shihab, OU=ShihabShieldVPN, O=OpenSource, L=Dhaka, C=BD" | Out-Null
}

if (Test-Path -LiteralPath $OutApk) { Remove-Item -LiteralPath $OutApk -Force }
if (Test-Path -LiteralPath $ApkSignerJar) {
    & $JavaExe -jar $ApkSignerJar sign `
        --ks $KeystorePath `
        --ks-key-alias shihabshield `
        --ks-pass pass:shihabvpn120 `
        --key-pass pass:shihabvpn120 `
        --out $OutApk `
        $AlignedApk
} else {
    & $ApkSignerBat sign `
        --ks $KeystorePath `
        --ks-key-alias shihabshield `
        --ks-pass pass:shihabvpn120 `
        --key-pass pass:shihabvpn120 `
        --out $OutApk `
        $AlignedApk
}
if ($LASTEXITCODE -ne 0) { throw "apksigner failed with exit code $LASTEXITCODE" }

$ApkInfo = Get-Item -LiteralPath $OutApk
$ApkSha = (Get-FileHash -LiteralPath $OutApk -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("SUCCESS! Built Signed Android APK: {0} ({1:N1} KB, SHA-256: {2})" -f $OutApk, ($ApkInfo.Length / 1KB), $ApkSha)
