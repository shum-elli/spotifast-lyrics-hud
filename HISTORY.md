# 开发版本记录

这些源码快照从本次开发记录中的实际编辑恢复，最后一个版本与整理前的工作源码逐字一致。表中的时间是源码节点时间，Git 发布提交于仓库整理时创建。

| 版本 | 原始节点时间（新加坡时间） | 主要变化 | 源码分支 |
| --- | --- | --- | --- |
| v0.1.0 | 2026-11-06 14:55:24 | 基础置顶歌词浮窗与播放进度同步 | [versions/v0.1.0](https://github.com/shum-elli/spotifast-lyrics-hud/tree/versions/v0.1.0) |
| v0.2.0 | 2026-11-06 15:01:24 | 透明度、字号、同步偏移和增强 LRC 导入 | [versions/v0.2.0](https://github.com/shum-elli/spotifast-lyrics-hud/tree/versions/v0.2.0) |
| v0.3.0 | 2026-11-06 15:10:19 | 字体与颜色、边框设置、纯文本歌词翻句 | [versions/v0.3.0](https://github.com/shum-elli/spotifast-lyrics-hud/tree/versions/v0.3.0) |
| v0.4.0 | 2026-11-06 15:14:09 | 联网匹配其他元数据写法的同步歌词 | [versions/v0.4.0](https://github.com/shum-elli/spotifast-lyrics-hud/tree/versions/v0.4.0) |
| v0.5.0 | 2026-11-06 15:19:45 | 拖动缩放窗口、歌名字号、初版简体转换 | [versions/v0.5.0](https://github.com/shum-elli/spotifast-lyrics-hud/tree/versions/v0.5.0) |
| v0.6.0 | 2026-11-06 15:24:02 | 平滑播放时钟与稳定的逐字渐进高亮 | [versions/v0.6.0](https://github.com/shum-elli/spotifast-lyrics-hud/tree/versions/v0.6.0) |
| v0.7.0 | 2026-11-06 16:03:01 | 内置 OpenCC 词库，统一转换歌名与歌词 | [versions/v0.7.0](https://github.com/shum-elli/spotifast-lyrics-hud/tree/versions/v0.7.0) |
| v0.8.0 | 2026-11-06 16:47:20 | 纯文本播放期间自动发现 Spotifast 新缓存的时间轴 | [versions/v0.8.0](https://github.com/shum-elli/spotifast-lyrics-hud/tree/versions/v0.8.0) |
| v0.8.1 | 2026-11-07 15:19:01 | 明确启动时的设置读取顺序与错误提示 | [versions/v0.8.1](https://github.com/shum-elli/spotifast-lyrics-hud/tree/versions/v0.8.1) |
| v0.9.0 | 2026-11-07 15:21:07 | 程序同目录配置与旧配置自动迁移 | [versions/v0.9.0](https://github.com/shum-elli/spotifast-lyrics-hud/tree/versions/v0.9.0) |

每个分支包含该版本源码和构建脚本，历史快照中的已知限制也被保留。最新版本位于 main。

快照 SHA-256 记录在 versions.json，便于核对恢复结果。
