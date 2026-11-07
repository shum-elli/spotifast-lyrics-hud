[CmdletBinding()]
param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath $framework)) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Install .NET Framework 4.8 on Windows before building.'
}
$output = Join-Path $projectRoot 'bin'
$null = New-Item -ItemType Directory -Path $output -Force
$references = @()
foreach ($name in @('WindowsBase', 'PresentationCore', 'PresentationFramework')) {
    $references += '/r:' + (Join-Path $framework ('WPF\' + $name + '.dll'))
}
foreach ($name in @('System.Xaml', 'System.Web.Extensions', 'System.Net.Http', 'System.Windows.Forms', 'System.Drawing', 'Microsoft.VisualBasic')) {
    $references += '/r:' + (Join-Path $framework ($name + '.dll'))
}
$resources = @()
$data = Join-Path $projectRoot 'data\opencc'
if (Test-Path -LiteralPath $data) {
    $resources += '/resource:' + (Join-Path $data 'TSCharacters.txt') + ',OpenCC.TSCharacters'
    $resources += '/resource:' + (Join-Path $data 'TSPhrases.txt') + ',OpenCC.TSPhrases'
    $resources += '/resource:' + (Join-Path $data 'LICENSE.txt') + ',OpenCC.LICENSE'
}
$source = Join-Path $projectRoot 'src\Hud.cs'
$program = Join-Path $output 'Spotifast Lyrics HUD.exe'
& $compiler /nologo /optimize+ /target:winexe /platform:anycpu ('/out:' + $program) @references @resources $source
if ($LASTEXITCODE -ne 0) {
    throw 'C# compilation failed.'
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $output 'LICENSE.txt') -Force
if (Test-Path -LiteralPath $data) {
    Copy-Item -LiteralPath (Join-Path $data 'LICENSE.txt') -Destination (Join-Path $output 'OpenCC-LICENSE.txt') -Force
}
Write-Output ('Built ' + $program)
if ($Test) {
    $checkSource = Join-Path $projectRoot 'tests\HudChecks.cs'
    if (-not (Test-Path -LiteralPath $checkSource)) {
        throw 'This historical snapshot does not include the current regression checks.'
    }
    $checks = Join-Path $output 'HudChecks.exe'
    & $compiler /nologo /target:exe /main:HudChecks ('/out:' + $checks) @references @resources $source $checkSource
    if ($LASTEXITCODE -ne 0) {
        throw 'Regression-check compilation failed.'
    }
    & $checks
    if ($LASTEXITCODE -ne 0) {
        throw 'Regression checks failed.'
    }
}

