$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputDir = Join-Path $projectDir 'dist\PasteGuard'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
& (Join-Path $projectDir 'assets\build-icons.ps1')
$appName = 'CreMore PasteGuard.exe'
$sources = 'Program.cs', 'Ui.cs', 'RulePresets.cs', 'Core.cs', 'WindowsGuard.cs', 'Bridge.cs' | ForEach-Object { Join-Path $projectDir $_ }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output "/out:$outputDir\$appName" "/win32manifest:$projectDir\app.manifest" "/win32icon:$projectDir\assets\cremore.ico" "/resource:$projectDir\assets\cremore.ico,CreMore.Icon" "/resource:$projectDir\assets\cremore-logo.png,CreMore.Logo" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Security.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $projectDir 'PasteGuard.exe.config') -Destination (Join-Path $outputDir "$appName.config") -Force
Copy-Item -LiteralPath (Join-Path $projectDir 'README.md'),(Join-Path $projectDir 'README.en.md') -Destination $outputDir -Force
Copy-Item -LiteralPath (Join-Path $projectDir 'docs') -Destination $outputDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectDir 'extension') -Destination $outputDir -Recurse -Force
Compress-Archive -LiteralPath (Join-Path $outputDir $appName),(Join-Path $outputDir "$appName.config"),(Join-Path $outputDir 'README.md'),(Join-Path $outputDir 'README.en.md'),(Join-Path $outputDir 'docs'),(Join-Path $outputDir 'extension') -DestinationPath (Join-Path $projectDir 'dist\CreMore-PasteGuard-windows.zip') -Force
Write-Output "Built: $outputDir\$appName"
