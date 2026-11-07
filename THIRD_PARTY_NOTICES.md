# 第三方内容

## OpenCC 1.2.0 词库

来源为 https://github.com/BYVoid/OpenCC ，固定提交 907bfcbbd3aae86ff04bc8eaca67c8af03108ddf。

本项目使用 TSCharacters.txt 和 TSPhrases.txt 的原始词库，未修改词条。版权归 OpenCC 贡献者所有，许可为 Apache-2.0，完整许可见 data/opencc/LICENSE.txt。构建时将词库及许可嵌入 exe。

转换代码使用最长词条匹配并优先使用词组结果。此项目使用词库数据实现转换，没有链接 OpenCC 原生库。
