# Spotifast Lyrics HUD

A lightweight Windows desktop lyrics overlay for Spotifast, written in C# and WPF.

为 Spotifast 提供独立、置顶的桌面歌词浮窗。当前快照为 **v0.4.0**。

## 功能

- 基础置顶歌词浮窗与播放进度同步
- 透明度、字号、同步偏移和增强 LRC 导入
- 字体与颜色、边框设置、纯文本歌词翻句
- 联网匹配其他元数据写法的同步歌词

## 构建与运行

需要 Windows 和 .NET Framework 4.8。构建使用系统自带的 C# 编译器，无需 .NET SDK。

在项目目录运行

    powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

然后打开 bin 目录中的 Spotifast Lyrics HUD.exe。Spotifast 应已运行。

初始验证环境为 Spotifast 0.17.1，默认安装路径为 %LOCALAPPDATA%\Programs\Spotifast\spotifast.exe。工具通过 now-playing --raw 读取播放信息，并读取 %LOCALAPPDATA%\paolino\spotifast 中的歌词缓存。缓存结构依赖 Spotifast 版本。

点右上角齿轮打开歌词与外观设置。

本版本将设置保存在 %LOCALAPPDATA%\SpotifastLyricsHud\settings.json。

## 歌词同步

优先读取 Spotifast 已缓存的歌词，再按曲目元数据访问 [LRCLIB](https://lrclib.net)。网络查询会发送曲名、歌手、专辑和时长。

本地只有纯文本时，会在后台寻找带时间轴的版本，并比较专辑、时长和歌词内容。

带行时间戳的歌词支持逐行跟随。「逐字估算」根据行间时间估算字内进度，准确性取决于歌曲的停顿和拖音。带字词时间戳的增强 LRC 可以通过导入功能使用。纯文本歌词可以用箭头或滚轮翻句。

## 版本记录

[HISTORY.md](HISTORY.md) 列出 10 个开发节点。各版本通过 versions/vX.Y.Z 分支保留，可直接在 GitHub 切换分支查看。

这些是从本次开发记录恢复并核对的历史源码快照，发布提交在整理时创建。中间未完成的编辑状态未作为版本发布。

## 许可与来源

项目代码采用 [MIT License](LICENSE)。

与本工具配合使用的 [Spotifast](https://github.com/crmne/spotifast) 和 [LRCLIB](https://github.com/tranxuanthang/lrclib) 各自独立维护。
