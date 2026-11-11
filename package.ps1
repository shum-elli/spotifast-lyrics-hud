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

修复新歌已有本地歌词但 HUD 找不到的问题：从当前歌单缓存严格匹配歌曲，无需等待约 30 秒后才写入的播放历史。保留每 3 秒检查缓存及防止网络结果覆盖同步歌词的修正。
Match first-play tracks from the current cached playlist instead of waiting for the roughly 30-second history threshold. Also includes periodic local cache discovery and protection against stale network results.
"@
[IO.File]::WriteAllText((Join-Path $output 'RELEASE_NOTES.txt'), $notes)
Write-Output ('Packaged ' + $standalone)
Write-Output ('Packaged ' + $zip)
