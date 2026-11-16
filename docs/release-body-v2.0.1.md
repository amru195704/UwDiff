<!--
パス: UwDiff/docs/release-body-v2.0.1.md（GitHub Releases の本文。下書き・開発部 UwDiff課・2026-11-16・オーナー指示 11/16 対話「2.0.1 で作る」）
- 中身: アプリのアイコンを付けただけ（UwDiff f256050）。比べる処理・CLI の出力は 2.0.0 と同じ。
- 配布物: 2026-11-16 に 6 本（tools/package.sh・ビルド番号 26.11.16.12）。SHA256 は out/dist/SHA256SUMS-2.0.1.txt と照合。
- 書式は release-body-v2.0.0.md に合わせた。金額・「最速」は書かない。文の仕上げ・英語の見直しは宣伝部 UwDiff宣伝課。
-->
*日本語 ｜ [English](#uwdiff-v201-english)*

## UwDiff v2.0.1

### ✨ 変わったこと

- **アプリのアイコンを付けました。** Mac の Finder・Dock、Windows のエクスプローラー・タスクバー、窓の左上に UwDiff のアイコンが出ます（v2.0.0 までは出ていませんでした）。
- 比べる処理、`uvd` の出力・終了コードは v2.0.0 と同じです。

### 📥 ダウンロード

```bash
brew install --cask amru195704/uwview/uwdiff     # Mac（更新は brew upgrade --cask uwdiff）
scoop install uwdiff                             # Windows（更新は scoop update uwdiff）
sudo snap install uwdiff                         # Linux（更新は snap refresh uwdiff）
```

手で入れる場合は、このページの下の **Assets** から取ってください。Mac は dmg（Apple シリコン〈M1〜M4〉用の `arm64`・Intel 用の `x64`）、Windows は zip（`x64`・`arm64`）、Linux は tar.gz（`x86_64`・`aarch64`）です。.NET を別に入れる必要はありません。チェックサムは `SHA256SUMS-2.0.0.txt` にあります。

```
327a9d20ba0a73b19f06b55a0da14bc9e6cd8cf4a50a2004c3ad46c3e3d8cf7b  UwDiff-2.0.1-linux-aarch64.tar.gz
ebe0c4ab7eff3ee6aec048c7d54ca561d14ce5015677eaf4587ec64ad35a4578  UwDiff-2.0.1-linux-x86_64.tar.gz
92c63d753b6c8d9264582a64158bd3bf08105d1d7a5041d8c126e6392b00b338  UwDiff-2.0.1-mac-arm64.dmg
42ccc6d27bf3011005fc7bb47e3409e33ed90ef3763a7151184e5e8095f13eac  UwDiff-2.0.1-mac-x64.dmg
96e860b25b654e47f520eadf2f53ce8d7cf0c54f087670bf7a313e9dacbe8283  UwDiff-2.0.1-win-arm64.zip
ae5ec932ef8e11ec17af400b1c7b02209260980e37faa6bb8173e717637bfd7e  UwDiff-2.0.1-win-x64.zip
（2026-11-16 作成。macOS 版は署名・公証済み。ビルド番号はすべて 26.11.16.12（日本時間）です）
```

（前の版は v2.0.0 “Fixed Star” です → [v2.0.0](https://github.com/amru195704/UwDiff/releases/tag/v2.0.0)）

---

## UwDiff v2.0.1 (English)

*[日本語](#uwdiff-v201) ｜ English*

### ✨ What's changed

- **The app now has an icon.** The UwDiff icon appears in Finder and the Dock on the Mac, in Explorer and the taskbar on Windows, and in the window title bar (it was missing up to v2.0.0).
- Comparison, and the output and exit codes of `uvd`, are the same as v2.0.0.

### 📥 Download

```bash
brew install --cask amru195704/uwview/uwdiff     # Mac (update: brew upgrade --cask uwdiff)
scoop install uwdiff                             # Windows (update: scoop update uwdiff)
sudo snap install uwdiff                         # Linux (update: snap refresh uwdiff)
```

To install by hand, get the files from **Assets** below: a dmg for the Mac (`arm64` for Apple silicon M1–M4, `x64` for Intel), a zip for Windows (`x64`, `arm64`), a tar.gz for Linux (`x86_64`, `aarch64`). No separate .NET installation is needed. Checksums are in `SHA256SUMS-2.0.0.txt`.

```
327a9d20ba0a73b19f06b55a0da14bc9e6cd8cf4a50a2004c3ad46c3e3d8cf7b  UwDiff-2.0.1-linux-aarch64.tar.gz
ebe0c4ab7eff3ee6aec048c7d54ca561d14ce5015677eaf4587ec64ad35a4578  UwDiff-2.0.1-linux-x86_64.tar.gz
92c63d753b6c8d9264582a64158bd3bf08105d1d7a5041d8c126e6392b00b338  UwDiff-2.0.1-mac-arm64.dmg
42ccc6d27bf3011005fc7bb47e3409e33ed90ef3763a7151184e5e8095f13eac  UwDiff-2.0.1-mac-x64.dmg
96e860b25b654e47f520eadf2f53ce8d7cf0c54f087670bf7a313e9dacbe8283  UwDiff-2.0.1-win-arm64.zip
ae5ec932ef8e11ec17af400b1c7b02209260980e37faa6bb8173e717637bfd7e  UwDiff-2.0.1-win-x64.zip
(Built 2026-11-16. The macOS builds are signed and notarized. All build numbers are 26.11.16.12, Japan time.)
```

(The previous release is v2.0.0 "Fixed Star" → [v2.0.0](https://github.com/amru195704/UwDiff/releases/tag/v2.0.0))

---
**支援について**: UwDiff は、個人での利用と社内業務での利用なら今後も無料です。続いてほしいと思っていただけたら [GitHub Sponsors](https://github.com/sponsors/amru195704) からどうぞ（任意です）。うまく比べられなかったファイルの報告や、手元の大きなファイルで測った結果も助かります → [Issues](https://github.com/amru195704/UwDiff/issues)。

**Supporting UwDiff**: UwDiff stays free for personal use and internal business use. If you would like it to keep going, you can support it via [GitHub Sponsors](https://github.com/sponsors/amru195704) (optional). Reports of files it did not compare well, and measurements from your own large files, help too → [Issues](https://github.com/amru195704/UwDiff/issues).
