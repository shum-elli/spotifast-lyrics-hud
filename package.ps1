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
$exeName = 'Spotifast-Lyrics-HUD-v' + $version + '-windows.exe'
$standalone = Join-Path $output $exeName
Copy-Item -LiteralPath (Join-Path $output 'Spotifast Lyrics HUD.exe') -Destination $standalone -Force
Copy-Item -LiteralPath $standalone -Destination (Join-Path $stage 'Spotifast Lyrics HUD.exe')
$zipName = 'Spotifast-Lyrics-HUD-v' + $version + '-windows.zip'
$zip = Join-Path $output $zipName
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$checksums = foreach ($file in @($standalone, $zip)) {
    (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file)
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), [string[]]$checksums)
$notes = @"
## 下载 / Download

直接下载 EXE，放到可写目录运行，无需安装或解压。ZIP 为可选下载。
Download the EXE and run it from a writable folder. No installation or extraction needed. The ZIP is optional.

需要 Windows 10/11、Microsoft .NET Framework 4.8 或 4.8.1，以及同一账号下运行的 Spotifast。
Requires Windows 10/11, Microsoft .NET Framework 4.8 or 4.8.1, and Spotifast running under the same Windows account.

[运行库 / Runtime](https://dotnet.microsoft.com/download/dotnet-framework/net48)

settings.json 保存在 EXE 旁，升级时请保留。许可文本已内置，可在托盘菜单“开源许可”中查看和复制。
Keep settings.json beside the EXE when upgrading. Embedded license texts can be read and copied via the tray menu: 开源许可 (Open-source licenses).

修复自动续播歌曲不在当前歌单缓存，以及歌手中英文名称不同导致本地歌词漏读的问题。扩展到其他已缓存歌单，并用保存的歌曲 ID、歌名、专辑、时长和封面确认名称差异；保留延迟缓存刷新。
Fix local lyric lookup for autoplay tracks outside the current playlist and localized artist names. Search other cached playlists and accept artist-name differences only with a matching saved track ID and full recording metadata. Includes delayed cache refresh fixes.
"@
[IO.File]::WriteAllText((Join-Path $output 'RELEASE_NOTES.txt'), $notes)
Write-Output ('Packaged ' + $standalone)
Write-Output ('Packaged ' + $zip)
