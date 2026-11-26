param(
    [string]$JdkBin = "C:\dev\jdk8\bin"
)

$ErrorActionPreference = "Stop"
$BackendDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcJavaDir = Join-Path $BackendDir "src\main\java"
$SrcResDir  = Join-Path $BackendDir "src\main\resources"
$LibDir     = Join-Path $BackendDir "lib"
$OutDir     = Join-Path $BackendDir "out"
$JarPath    = Join-Path $BackendDir "extbatch-backend.jar"

$Javac = Join-Path $JdkBin "javac.exe"
$Jar   = Join-Path $JdkBin "jar.exe"
if (-not (Test-Path $Javac)) { throw "javac.exe not found at $Javac" }
if (-not (Test-Path $Jar))   { throw "jar.exe not found at $Jar" }

if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$sourcesFile = Join-Path $BackendDir "sources.txt"
$javaFiles = Get-ChildItem -Path $SrcJavaDir -Recurse -Filter "*.java" | ForEach-Object { '"' + ($_.FullName -replace '\\','/') + '"' }
[System.IO.File]::WriteAllLines($sourcesFile, $javaFiles, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[1/3] Compiling Java sources ($($javaFiles.Count) files)..."
& $Javac -encoding UTF-8 -source 1.8 -target 1.8 -cp "$LibDir\*" -d $OutDir "@$sourcesFile"
if ($LASTEXITCODE -ne 0) {
    Remove-Item -Force $sourcesFile -ErrorAction SilentlyContinue
    throw "javac compilation failed (exit=$LASTEXITCODE)"
}
Remove-Item -Force $sourcesFile -ErrorAction SilentlyContinue

Write-Host "[2/3] Copying resources..."
if (Test-Path $SrcResDir) {
    Copy-Item -Path "$SrcResDir\*" -Destination $OutDir -Recurse -Force
}

Write-Host "[3/3] Packaging $JarPath..."
if (Test-Path $JarPath) { Remove-Item -Force $JarPath }
& $Jar cfe $JarPath "com.gcti.extbatch.ExtBatchBackendApplication" -C $OutDir .
if ($LASTEXITCODE -ne 0) {
    throw "jar packaging failed (exit=$LASTEXITCODE)"
}

Write-Host "[OK] Built $JarPath"
