<!--
パス: UwDiff/docs/release-body-v2.0.0.md（GitHub Releases の本文。下書き・開発部 UwDiff課・2026-11-13・指示-20261113-06）
- 骨組みだけ。文の仕上げ・英語の見直しは宣伝部 UwDiff宣伝課（1.0.0 の本文と同じ流れ）。末尾の「支援について」は宣伝課の文案 B-2 をそのまま貼った（変えない）。
- 1.1.0（commit eb9a068・タグ／リリース無し）の内容も 2.0.0 に含める読みで書いた（要判断・0-HQ/開発部/UwDiff課/報告.md 2026-11-13 の節）。
- 数字は docs/benchmarks.md の「1.1.0」の節の値だけ（条件つき）。「速くなった」とは書かない（オーナー決定 2026-11-03）。金額・「最速」は書かない。
- 配布物: 2026-11-15 に 6 本（支援の案内の説明の言い換えで作り直し）（tools/package.sh・ビルド番号 26.11.15.02）。ダウンロードの節と SHA256 は開発部 UwDiff課が足した（out/dist/SHA256SUMS-2.0.0.txt と照合）。リリースは検査課の合格の後。
-->
*日本語 ｜ [English](#uwdiff-v200-fixed-star-english)*

## UwDiff v2.0.0 “Fixed Star”

### ✨ 変わったこと

- **支援の案内**: 1 か月ほど使った後（初めて使った日から 30 日以上・使った日が 10 日以上）と年末（12/15〜1/10）に、画面の上に支援の案内（GitHub Sponsors）を出します（間は 90 日以上あけます）。閉じなくても作業できます（モーダルではありません）。［後で］は 90 日後に 1 回だけもう一度、［支援する］［もう表示しない］の後は出しません。設定…の「支援の案内を表示しない」で止められます。**使った日数はこのパソコンの中だけで数え、外部には送りません。**
- `uvd --version`・`uvd -h` の最後に支援の案内を 1 行だけ出します。**差分の出力・終了コード・標準エラーは変わりません。**
- テキストの比較で、旧と新の**先頭から同じ部分を行に切らずに飛ばす**ようにしました（差分は 1.0.0 とバイト単位で同じ）。先頭から同じ部分が長いファイルでは CPU 時間とメモリが減ります（条件と値: [docs/benchmarks.md](https://github.com/amru195704/UwDiff/blob/main/docs/benchmarks.md) の「1.1.0」の節）。

### 📥 ダウンロード

```bash
brew install --cask amru195704/uwview/uwdiff     # Mac（更新は brew upgrade --cask uwdiff）
scoop install uwdiff                             # Windows（更新は scoop update uwdiff）
sudo snap install uwdiff                         # Linux（更新は snap refresh uwdiff）
```

手で入れる場合は、このページの下の **Assets** から取ってください。Mac は dmg（Apple シリコン〈M1〜M4〉用の `arm64`・Intel 用の `x64`）、Windows は zip（`x64`・`arm64`）、Linux は tar.gz（`x86_64`・`aarch64`）です。.NET を別に入れる必要はありません。チェックサムは `SHA256SUMS-2.0.0.txt` にあります。

```
308d23ba946cdcf9dcd120c2d5036c79389ffd0c66961e523c69fb11796b489a  UwDiff-2.0.0-linux-aarch64.tar.gz
284c2a25631082653934ccf14c0b347f8b2c6fac62671522ea656714c5d19439  UwDiff-2.0.0-linux-x86_64.tar.gz
17e7b1bd27d6400b1864ae65491ebdf2212d013b6aa417dc57f576a12ec3ee84  UwDiff-2.0.0-mac-arm64.dmg
37986b97311e44748bea09b24c8d28fb1de1adc27cd282ce4ba1735bd42550c7  UwDiff-2.0.0-mac-x64.dmg
1b2cdf8cd1e090addf27fb8ff5105a3445704c18e80c3efb5059be5169b24558  UwDiff-2.0.0-win-arm64.zip
4d14b5f8921112be75073e440dfd95eb185fac151b4c851c58bfd66c62c1fc94  UwDiff-2.0.0-win-x64.zip
（2026-11-15 作成。macOS 版は署名・公証済み。ビルド番号はすべて 26.11.15.02（日本時間）です）
```

（前の版は v1.0.0 です → [v1.0.0](https://github.com/amru195704/UwDiff/releases/tag/v1.0.0)）

---

## UwDiff v2.0.0 “Fixed Star” (English)

*[日本語](#uwdiff-v200-fixed-star) ｜ English*

### ✨ What's changed

- **Support message**: after about a month of use (30+ days since first use and 10+ days of use) and at the end of the year (Dec 15 – Jan 10), UwDiff shows a support message (GitHub Sponsors) at the top of the window (at least 90 days apart). It is not modal — you can keep working. "Later" shows it once more after 90 days; after "Support" or "Don't show again" it is not shown again. Turn it off in Settings… ("Don't show support messages"). **Days of use are counted only on this computer and are never sent anywhere.**
- `uvd --version` and `uvd -h` end with one line about supporting UwDiff. **Diff output, exit codes and standard error are unchanged.**
- Text comparison now **skips the common leading part** of old and new without splitting it into lines (the diff is byte-for-byte the same as 1.0.0). For files with a long common prefix this reduces CPU time and memory (conditions and numbers: the "1.1.0" section of [docs/benchmarks.md](https://github.com/amru195704/UwDiff/blob/main/docs/benchmarks.md)).

### 📥 Download

```bash
brew install --cask amru195704/uwview/uwdiff     # Mac (update: brew upgrade --cask uwdiff)
scoop install uwdiff                             # Windows (update: scoop update uwdiff)
sudo snap install uwdiff                         # Linux (update: snap refresh uwdiff)
```

To install by hand, get the files from **Assets** below: a dmg for the Mac (`arm64` for Apple silicon M1–M4, `x64` for Intel), a zip for Windows (`x64`, `arm64`), a tar.gz for Linux (`x86_64`, `aarch64`). No separate .NET installation is needed. Checksums are in `SHA256SUMS-2.0.0.txt`.

```
308d23ba946cdcf9dcd120c2d5036c79389ffd0c66961e523c69fb11796b489a  UwDiff-2.0.0-linux-aarch64.tar.gz
284c2a25631082653934ccf14c0b347f8b2c6fac62671522ea656714c5d19439  UwDiff-2.0.0-linux-x86_64.tar.gz
17e7b1bd27d6400b1864ae65491ebdf2212d013b6aa417dc57f576a12ec3ee84  UwDiff-2.0.0-mac-arm64.dmg
37986b97311e44748bea09b24c8d28fb1de1adc27cd282ce4ba1735bd42550c7  UwDiff-2.0.0-mac-x64.dmg
1b2cdf8cd1e090addf27fb8ff5105a3445704c18e80c3efb5059be5169b24558  UwDiff-2.0.0-win-arm64.zip
4d14b5f8921112be75073e440dfd95eb185fac151b4c851c58bfd66c62c1fc94  UwDiff-2.0.0-win-x64.zip
(Built 2026-11-15. The macOS builds are signed and notarized. All build numbers are 26.11.15.02, Japan time.)
```

(The previous release is v1.0.0 → [v1.0.0](https://github.com/amru195704/UwDiff/releases/tag/v1.0.0))

---
**支援について**: UwDiff は、個人での利用と社内業務での利用なら今後も無料です。続いてほしいと思っていただけたら [GitHub Sponsors](https://github.com/sponsors/amru195704) からどうぞ（任意です）。うまく比べられなかったファイルの報告や、手元の大きなファイルで測った結果も助かります → [Issues](https://github.com/amru195704/UwDiff/issues)。

**Supporting UwDiff**: UwDiff stays free for personal use and internal business use. If you would like it to keep going, you can support it via [GitHub Sponsors](https://github.com/sponsors/amru195704) (optional). Reports of files it did not compare well, and measurements from your own large files, help too → [Issues](https://github.com/amru195704/UwDiff/issues).
