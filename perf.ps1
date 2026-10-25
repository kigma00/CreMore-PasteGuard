param([switch]$Native)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /optimize+ /out:tests\CorePerformance.exe Core.cs tests\CorePerformance.cs
    if ($LASTEXITCODE) { throw 'Performance probe build failed' }
    & .\tests\CorePerformance.exe
    if ($LASTEXITCODE) { throw 'Core performance probe failed' }
    & node .\tests\extension-perf.js
    if ($LASTEXITCODE) { throw 'Extension performance probe failed' }
    if ($Native) {
        & $compiler /nologo /optimize+ /out:tests\NativePerformance.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Core.cs WindowsGuard.cs tests\NativePerformance.cs
        if ($LASTEXITCODE) { throw 'Native performance probe build failed' }
        & .\tests\NativePerformance.exe
        if ($LASTEXITCODE) { throw 'Foreground window was unavailable for measurement' }
    }
} finally { Pop-Location }
