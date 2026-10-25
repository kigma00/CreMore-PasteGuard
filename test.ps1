param([switch]$Bridge, [switch]$NativeLifecycle, [switch]$Ui)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /out:tests\CoreTests.exe /r:System.Web.Extensions.dll Core.cs tests\CoreTests.cs
    if ($LASTEXITCODE) { throw 'Core test build failed' }
    & .\tests\CoreTests.exe
    if ($LASTEXITCODE) { throw 'Core tests failed' }
    & $compiler /nologo /out:tests\RulePresetsTests.exe /r:System.Web.Extensions.dll Core.cs RulePresets.cs tests\RulePresetsTests.cs
    if ($LASTEXITCODE) { throw 'Preset test build failed' }
    & .\tests\RulePresetsTests.exe
    if ($LASTEXITCODE) { throw 'Preset tests failed' }
    & node .\tests\extension-tests.js
    if ($LASTEXITCODE) { throw 'Extension tests failed' }
    if ($Bridge) {
        & $compiler /nologo /out:tests\BridgeTests.exe /r:System.Web.Extensions.dll Bridge.cs tests\BridgeTests.cs
        if ($LASTEXITCODE) { throw 'Bridge test build failed' }
        & .\tests\BridgeTests.exe
        if ($LASTEXITCODE) { throw 'Bridge tests failed; close PasteGuard before running these tests' }
    }
    if ($NativeLifecycle) {
        & $compiler /nologo /out:tests\NativeHarness.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Core.cs WindowsGuard.cs tests\NativeHarness.cs
        if ($LASTEXITCODE) { throw 'Native test build failed' }
        & .\tests\NativeHarness.exe --lifecycle
        if ($LASTEXITCODE) { throw 'Native lifecycle test failed' }
    }
    if ($Ui) {
        $uiTestDir = Join-Path $PSScriptRoot ('tests\uitest\run-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $uiTestDir | Out-Null
        $uiTestExe = Join-Path $uiTestDir 'UiFlowTests.exe'
        & $compiler /nologo /main:UiFlowTests "/out:$uiTestExe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Security.dll /resource:assets\cremore.ico,CreMore.Icon /resource:assets\cremore-logo.png,CreMore.Logo Program.cs Ui.cs RulePresets.cs Core.cs WindowsGuard.cs Bridge.cs tests\UiFlowTests.cs
        if ($LASTEXITCODE) { throw 'UI test build failed' }
        Copy-Item -LiteralPath .\PasteGuard.exe.config -Destination "$uiTestExe.config"
        & $uiTestExe
        if ($LASTEXITCODE) { throw 'UI flow tests failed' }
    }
} finally { Pop-Location }
