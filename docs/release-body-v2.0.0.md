<!--
パス: UwDiff/docs/release-body-v2.0.0.md（GitHub Releases の本文。下書き・開発部 UwDiff課・2026-11-13・指示-20261113-06）
- 骨組みだけ。文の仕上げ・英語の見直しは宣伝部 UwDiff宣伝課（1.0.0 の本文と同じ流れ）。末尾の「支援について」は宣伝課の文案 B-2 をそのまま貼った（変えない）。
- 1.1.0（commit eb9a068・タグ／リリース無し）の内容も 2.0.0 に含める読みで書いた（要判断・0-HQ/開発部/UwDiff課/報告.md 2026-11-13 の節）。
- 数字は docs/benchmarks.md の「1.1.0」の節の値だけ（条件つき）。「速くなった」とは書かない（オーナー決定 2026-11-03）。金額・「最速」は書かない。
- SHA256・配布物の表は、配布物を作ってから足す（未）。リリースは検査課の合格の後。
-->
*日本語 ｜ [English](#uwdiff-v200-english)*

## UwDiff v2.0.0

### ✨ 変わったこと

- **支援の案内**: 1 か月ほど使った後（初めて使った日から 30 日以上・使った日が 10 日以上）と年末（12/15〜1/10）に、画面の上に支援の案内（GitHub Sponsors）を**年 1 回まで**出します。閉じなくても作業できます（モーダルではありません）。［後で］は 90 日後に 1 回だけもう一度、［支援する］［もう表示しない］の後は出しません。設定…の「支援の案内を表示しない」で止められます。**使った日数はこのパソコンの中だけで数え、外部には送りません。**
- `uvd --version`・`uvd -h` の最後に支援の案内を 1 行だけ出します。**差分の出力・終了コード・標準エラーは変わりません。**
- テキストの比較で、旧と新の**先頭から同じ部分を行に切らずに飛ばす**ようにしました（差分は 1.0.0 とバイト単位で同じ）。先頭から同じ部分が長いファイルでは CPU 時間とメモリが減ります（条件と値: [docs/benchmarks.md](https://github.com/amru195704/UwDiff/blob/main/docs/benchmarks.md) の「1.1.0」の節）。

---

## UwDiff v2.0.0 (English)

### ✨ What's changed

- **Support message**: after about a month of use (30+ days since first use and 10+ days of use) and at the end of the year (Dec 15 – Jan 10), UwDiff shows a support message (GitHub Sponsors) at the top of the window **at most once a year**. It is not modal — you can keep working. "Later" shows it once more after 90 days; after "Support" or "Don't show again" it is not shown again. Turn it off in Settings… ("Don't show support messages"). **Days of use are counted only on this computer and are never sent anywhere.**
- `uvd --version` and `uvd -h` end with one line about supporting UwDiff. **Diff output, exit codes and standard error are unchanged.**
- Text comparison now **skips the common leading part** of old and new without splitting it into lines (the diff is byte-for-byte the same as 1.0.0). For files with a long common prefix this reduces CPU time and memory (conditions and numbers: the "1.1.0" section of [docs/benchmarks.md](https://github.com/amru195704/UwDiff/blob/main/docs/benchmarks.md)).

---
**支援について**: UwDiff は、個人での利用と社内業務での利用なら今後も無料です。続いてほしいと思っていただけたら [GitHub Sponsors](https://github.com/sponsors/amru195704) からどうぞ（任意です）。うまく比べられなかったファイルの報告や、手元の大きなファイルで測った結果も助かります → [Issues](https://github.com/amru195704/UwDiff/issues)。

**Supporting UwDiff**: UwDiff stays free for personal use and internal business use. If you would like it to keep going, you can support it via [GitHub Sponsors](https://github.com/sponsors/amru195704) (optional). Reports of files it did not compare well, and measurements from your own large files, help too → [Issues](https://github.com/amru195704/UwDiff/issues).
