[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -Test
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'VERSION must contain a numeric major.minor.patch version.'
}
$output = Join-Path $PSScriptRoot 'bin'
$stage = Join-Path $output ('package-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $stage
foreach ($name in @('Spotifast Lyrics HUD.exe', 'LICENSE.txt', 'OpenCC-LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $output $name) -Destination (Join-Path $stage $name)
}
$runtime = @'
Spotifast Lyrics HUD

Runtime requirements
- Windows 10 or Windows 11.
- Microsoft .NET Framework 4.8 or 4.8.1.
  Windows 10 1903+ includes 4.8. Windows 11 includes 4.8 or 4.8.1.
  Runtime download: https://dotnet.microsoft.com/download/dotnet-framework/net48
- Spotifast running on the same Windows account.
  Tested with Spotifast 0.17.1 installed at:
  %LOCALAPPDATA%\Programs\Spotifast\spotifast.exe

Extract this entire ZIP into a writable folder and run Spotifast Lyrics HUD.exe.
The app creates settings.json beside the EXE. Keep it when upgrading.
Use the gear button for settings. Drag the bottom-right handle to resize.
Use the lock button to prevent moving and resizing. The lock state is saved.
Controls have transparent backgrounds. Only the unlock button is visible while locked.
Previous, play/pause, and next are available on the overlay and tray menu.
The app stays in the system tray. Double-click its icon to show the overlay.
The X button hides the overlay. Use Exit in the tray menu to quit.
OpenCC conversion dictionaries are embedded in the EXE.
LRCLIB requests send the track title, artist, album, and duration.

Source and help: https://github.com/shum-elli/spotifast-lyrics-hud
'@
[IO.File]::WriteAllText((Join-Path $stage 'RUNNING.txt'), $runtime)
$zipName = 'Spotifast-Lyrics-HUD-v' + $version + '-windows.zip'
$zip = Join-Path $output $zipName
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), $hash + '  ' + $zipName + [Environment]::NewLine)
$notes = 'Portable Windows build. Extract the ZIP and run Spotifast Lyrics HUD.exe.' +
    [Environment]::NewLine + [Environment]::NewLine +
    'Requires Windows 10/11, Microsoft .NET Framework 4.8 or 4.8.1, and Spotifast.' +
    [Environment]::NewLine +
    'Runtime download: https://dotnet.microsoft.com/download/dotnet-framework/net48' +
    [Environment]::NewLine +
    'settings.json is created beside the EXE. Preserve it when upgrading.' +
    [Environment]::NewLine +
    'Transparent controls; locking hides all overlay controls except the unlock button.' +
    [Environment]::NewLine +
    'The package includes RUNNING.txt and the MIT and OpenCC licenses.'
[IO.File]::WriteAllText((Join-Path $output 'RELEASE_NOTES.txt'), $notes)
Write-Output ('Packaged ' + $zip)
