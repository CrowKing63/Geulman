# Geulman build & package script
# Usage: powershell -ExecutionPolicy Bypass -File installer/build.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot) -Parent
$out = Join-Path $PSScriptRoot "publish"

Write-Host "=== Geulman Build ===" -ForegroundColor Cyan

# 1. Publish WinUI 3 app (self-contained, x64)
Write-Host "[1/3] Publishing WinUI 3 app..."
dotnet publish "$root\src\Geulman\Geulman.csproj" `
    -c Release -r win-x64 --self-contained true `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "WinUI publish failed" }

# 2. Publish CLI engine
Write-Host "[2/3] Publishing CLI engine..."
$cliOut = Join-Path $out "cli"
dotnet publish "$root\src\Geulman.Docx\Geulman.Docx.csproj" `
    -c Release -r win-x64 --self-contained true `
    -o $cliOut
if ($LASTEXITCODE -ne 0) { throw "CLI publish failed" }

# 3. Copy bridge script
Write-Host "[3/3] Copying bridge script..."
New-Item -ItemType Directory -Force -Path (Join-Path $out "bridge") | Out-Null
Copy-Item "$root\src\hwp_bodytext_swap.py" (Join-Path $out "bridge\hwp_bodytext_swap.py") -Force

# 4. Compile installer
Write-Host "=== Compiling installer ===" -ForegroundColor Cyan
$iscc = "C:\Users\uitaek\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
$script = Join-Path $PSScriptRoot "Geulman.isn"
& $iscc /q $script
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Write-Host "=== Done ===" -ForegroundColor Green
Write-Host "Output: $(Join-Path $PSScriptRoot 'Output\GeulmanSetup-0.1.0.exe')"
