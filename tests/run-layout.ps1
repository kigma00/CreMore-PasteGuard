$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$probeDir = Join-Path $PSScriptRoot ('uitest\layout-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$probeExe = Join-Path $probeDir 'UiLayoutProbe.exe'
$sources = 'Program.cs', 'Ui.cs', 'RulePresets.cs', 'Core.cs', 'WindowsGuard.cs', 'Bridge.cs' | ForEach-Object { Join-Path $projectDir $_ }
& $compiler /nologo /main:UiLayoutProbe "/out:$probeExe" "/win32manifest:$projectDir\app.manifest" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Security.dll "/resource:$projectDir\assets\cremore.ico,CreMore.Icon" "/resource:$projectDir\assets\cremore-logo.png,CreMore.Logo" $sources (Join-Path $PSScriptRoot 'UiLayoutProbe.cs')
if ($LASTEXITCODE) { throw 'Layout probe build failed.' }
Copy-Item -LiteralPath (Join-Path $projectDir 'PasteGuard.exe.config') -Destination "$probeExe.config"
& $probeExe
$probeExit = $LASTEXITCODE
Write-Output "Layout report: $(Join-Path $probeDir 'layout-report.txt')"
if ($probeExit) { throw "Layout probe failed with exit code $probeExit." }
