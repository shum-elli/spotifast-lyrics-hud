# Spotifast Lyrics HUD

A lightweight Windows desktop lyrics overlay for Spotifast, written in C# and WPF.

为 Spotifast 提供独立、置顶的桌面歌词浮窗。当前快照为 **v0.1.0**。

## 功能

- 基础置顶歌词浮窗与播放进度同步

## 构建与运行

需要 Windows 和 .NET Framework 4.8。构建使用系统自带的 C# 编译器，无需 .NET SDK。

在项目目录运行

    powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

然后打开 bin 目录中的 Spotifast Lyrics HUD.exe。Spotifast 应已运行。

初始验证环境为 Spotifast 0.17.1，默认安装路径为 %LOCALAPPDATA%\Programs\Spotifast\spotifast.exe。工具通过 now-playing --raw 读取播放信息，并读取 %LOCALAPPDATA%\paolino\spotifast 中的歌词缓存。缓存结构依赖 Spotifast 版本。





## 歌词同步

优先读取 Spotifast 已缓存的歌词，再按曲目元数据访问 [LRCLIB](https://lrclib.net)。网络查询会发送曲名、歌手、专辑和时长。



带行时间戳的歌词支持逐行跟随。

## 版本记录

[HISTORY.md](HISTORY.md) 列出 10 个开发节点。各版本通过 versions/vX.Y.Z 分支保留，可直接在 GitHub 切换分支查看。

这些是从本次开发记录恢复并核对的历史源码快照，发布提交在整理时创建。中间未完成的编辑状态未作为版本发布。

## 许可与来源

项目代码采用 [MIT License](LICENSE)。

与本工具配合使用的 [Spotifast](https://github.com/crmne/spotifast) 和 [LRCLIB](https://github.com/tranxuanthang/lrclib) 各自独立维护。
