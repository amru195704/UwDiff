<!--
パス: UwDiff/docs/release-body-v1.0.0.md（GitHub Releases の本文。下書き・宣伝部 UwDiff宣伝課・2026-10-25）
- 数字は docs/benchmarks.md（uvd 1.0.0・Mac mini Apple M4・キャッシュを消していない値）だけ。金額・「最速」は書かない。
- SHA256 は out/dist/SHA256SUMS-1.0.0.txt の値（build 26.10.25.20・2026-10-25 20:12 に全 6 本を作り直した値。mac の dmg は署名・公証済み。検査の③〜⑥〔ライセンス全文など〕を入れた）。dist を作り直したら差し替える。
-->
*日本語 ｜ [English](#uwdiff-v100-first-light-english)*

## UwDiff v1.0.0 “First Light”

UwDiff の最初の版です。2つのファイル（または2つのフォルダ）を比べて、どこが違うかを出します。**形式・文字コード・大きさ・本数を問いません。** 画面（UwDiff）と CLI（`uvd`）が同じ比べ方をします。

> ログでも CSV でも Excel でも PDF でも。Shift-JIS でも UTF-16 でも。1 本 10GB でも、1 万本のフォルダでも。

### ✨ できること

- **4 つの比べ方**
  - テキスト: 行の並びを保って比べ、`diff -u` と同じ形（unified diff）で出す。`patch` で旧に当てると新になる
  - 表: CSV・TSV・Excel を**キー列で照合**（**行の順番が違ってもよい**・列は見出しの名前で対応・変わったセルごとに 1 行）
  - 順番を問わない比較: どの行が増えた・減ったかだけを数える
  - フォルダ対フォルダ: 旧だけ・新だけ・違う・同じの一覧。`-include`・`-exclude` は UwView の `uvf` と同じ書き方
- **採る変更だけを反映**（`uvd apply`・画面の「採った変更を反映…」）: 旧の文字コード・区切り・改行・引用符・行の順番を保ったまま新しいファイルを書く（元のファイルは変えない）
- **形式**: テキスト／圧縮 7 形式（gz・bz2・xz・lzma・zst・lz4・br）の中身／Word（.docx）・Excel（.xlsx）・PDF の文字
- **文字コード**: UTF-8・Shift-JIS（CP932）・EUC-JP・UTF-16 を自動判定。旧と新で違ってもよい（同じなら変換せずに比べる）
- **CLI と画面**: `uvd … -open` で結果をそのまま画面へ（画面はもう一度比べない）。画面の「コマンドライン…」から `uvd` の引数で比べることもできる
- **画面**: 変わった文字を濃く塗る・「変更の所だけ」・前／次の変更（F7・Shift+F7）・タブ・日本語と英語の切り替え（設定…）
- **終了コードは diff と同じ**（0 = 同じ・1 = 違いあり・2 = エラー）
- メモリを超えても一時ファイルへ逃がして止まらない（`-mem`・`-tmp`）

### ⚡ 速さ（実測）

| 比べるもの（2 本とも同じ大きさ） | 時間（秒） | メモリ |
|---|---:|---|
| CSV 10GB × 2 本・キー照合・行順そろい（UTF-8／CP932） | 21.8／20.5 | 4.0〜4.3GiB |
| CSV 10GB × 2 本・キー照合・行順ばらばら（UTF-8／CP932） | 23.3／22.3 | 11.1〜11.5GiB |
| テキスト 10GB × 2 本・行の並びを保つ比較 | 20.7 | 0.4GiB |
| 画面: テキスト 10GB × 2 本、「比べる」から画面に出るまで | 20.5 | — |
| フォルダ 1 万本（片側 2.2GB） | 3.1（キャッシュに載った状態） | — |

負けも書きます。**ほぼ全行が違う最悪の組**（テキスト 10GB × 2 本）は **110 秒**、行順ばらばらの表をメモリ上限 476MB で比べると **58〜60 秒**です。

条件: Mac mini（Apple M4・10コア・メモリ 32GB）・macOS・外付け SSD／uvd 1.0.0／キャッシュを消していない値（10GB × 2 本はメモリに収まらないので cold に近い）。どれも結果が正解と完全に一致することを確かめたうえでの時間です。全部の表は [docs/benchmarks.md](https://github.com/amru195704/UwDiff/blob/main/docs/benchmarks.md)。

### ⚠️ 注意

- Word・Excel・PDF は**取り出した文字**の比較です（書式・図・数式は比べません）。古い .doc・.xls、パスワード付き、文字の無い PDF は読めません。
- Excel のシートの照合と「順番を問わない」比較は、差分の一覧まで（反映はしません）。
- 1 か所で数百万行が入れ替わるような所は、その範囲を「置き換え」として出すことがあります（正しい差分ですが、最小とは限りません）。
- CLI の出力は日本語です（画面は日本語・英語を切り替えられます）。

### 📥 ダウンロード

このページの下の **Assets** から取ってください。Mac は dmg（Apple シリコン〈M1〜M4〉用の `arm64`・Intel 用の `x64`）、Windows は zip（`x64`・`arm64`）、Linux は tar.gz（`x86_64`・`aarch64`）です。.NET を別に入れる必要はありません。

- Mac: dmg を開いて UwDiff を Applications へ。CLI は `UwDiff.app/Contents/MacOS/uvd`
- Windows・Linux: 好きな所に展開。画面（`UwDiff`）と CLI（`uvd`）が並ぶ
- ターミナルで `uvd` と打つだけで使うには、画面の「ヘルプ」→「コマンドライン設定…」

チェックサムは `SHA256SUMS-1.0.0.txt` にあります。

```
72187ec32802616fd05b5e3531909233d357dbf637bb21228642e2dbbb315ac4  UwDiff-1.0.0-linux-aarch64.tar.gz
2f79466214a5043da59792b3c8f804aae43b5d36346669f70045a1761fd3011e  UwDiff-1.0.0-linux-x86_64.tar.gz
2d00447edf6ccc0ff625759dfa9a847bec480f926c1fe200f4cd8df501038482  UwDiff-1.0.0-mac-arm64.dmg
cd9be7a6de5ec2f28a7fc8e6e84fb02f01255d11637592699a32d00bf56c09bd  UwDiff-1.0.0-mac-x64.dmg
a4da8049e0fc5131ae6d481ee419616eeed4fc370bdab2f865149c84872555f5  UwDiff-1.0.0-win-arm64.zip
b8137d9a02c735c668efbf0751e2f85e7255fd7f04ad994b630dcd71cd6c340b  UwDiff-1.0.0-win-x64.zip
```

### 📖 使い方とライセンス

- 操作マニュアル: [docs/manual.ja.md](https://github.com/amru195704/UwDiff/blob/main/docs/manual.ja.md)・サイト版 https://uvp.y42u.net/uwdiff-manual/
- 紹介ページ: https://uvp.y42u.net/uwdiff/
- ライセンス: [UwDiff License](https://github.com/amru195704/UwDiff/blob/main/LICENSE)（PolyForm Internal Use 1.0.0 ＋ 公式バイナリのパッケージ再配布の許可。[日本語の参考訳](https://github.com/amru195704/UwDiff/blob/main/LICENSEjp.txt)）。個人利用・企業の社内業務利用は無料。公式バイナリを無改変のまま無償でパッケージとして再配布できます（Homebrew・Scoop・Flathub・Snap・AUR など）。オープンソースではありません。
- 不具合・要望: [Issues](https://github.com/amru195704/UwDiff/issues)

> **姉妹ツール**: 巨大なファイルを開いて探すなら → [UwView](https://github.com/amru195704/UwView)（UwView で探して、UwDiff で比べる）

---

## UwDiff v1.0.0 “First Light” (English)

*[日本語](#uwdiff-v100-first-light) ｜ English*

The first release of UwDiff. It compares two files (or two folders) and shows what changed — **regardless of format, character encoding, size or number of files.** The GUI (UwDiff) and the CLI (`uvd`) use the same comparison engine.

> Logs, CSV, Excel or PDF. Shift-JIS or UTF-16. One 10 GB file or a folder of 10,000 files.

### ✨ What it does

- **Four ways to compare**
  - Text: order-preserving comparison written in the same format as `diff -u` (unified diff); applying it with `patch` turns OLD into NEW
  - Tables: CSV, TSV and Excel matched by **key columns** (**row order may differ**; columns matched by header name; one row per changed cell)
  - Order-independent lines: counts which lines were added or removed
  - Folder vs folder: only-in-old, only-in-new, different, same. `-include` / `-exclude` use the same syntax as UwView's `uvf`
- **Apply only the changes you pick** (`uvd apply`, or "Apply selected changes…" in the GUI): writes a new file keeping the old file's encoding, delimiter, line endings, quoting and row order (the originals are not modified)
- **Formats**: text / the content of 7 compression formats (gz, bz2, xz, lzma, zst, lz4, br) / the text of Word (.docx), Excel (.xlsx) and PDF
- **Encodings**: UTF-8, Shift-JIS (CP932), EUC-JP and UTF-16, detected automatically; old and new may differ (compared without conversion when they match)
- **CLI and GUI**: `uvd … -open` shows the result in the GUI (the GUI does not compare again); the GUI's Command Line… runs `uvd` arguments too
- **GUI**: changed characters highlighted, "Changes only", previous / next change (F7, Shift+F7), tabs, Japanese / English (Settings…)
- **Exit codes follow diff** (0 = same, 1 = different, 2 = error)
- Spills to temporary files instead of running out of memory (`-mem`, `-tmp`)

### ⚡ Speed (measured)

| Comparison (both files the same size) | Time (s) | Memory |
|---|---:|---|
| CSV 10 GB × 2, key matching, same row order (UTF-8 / CP932) | 21.8 / 20.5 | 4.0 to 4.3 GiB |
| CSV 10 GB × 2, key matching, shuffled rows (UTF-8 / CP932) | 23.3 / 22.3 | 11.1 to 11.5 GiB |
| Text 10 GB × 2, order-preserving comparison | 20.7 | 0.4 GiB |
| GUI: text 10 GB × 2, from Compare until on screen | 20.5 | — |
| Folders of 10,000 files (2.2 GB per side) | 3.1 (data in the page cache) | — |

Losses too: **the worst case where almost every line differs** (text, 10 GB × 2) takes **110 s**, and shuffled tables with the memory limit at 476 MB take **58 to 60 s**.

Conditions: Mac mini (Apple M4, 10 cores, 32 GB memory), macOS, external SSD / uvd 1.0.0 / cache not cleared (two 10 GB files do not fit in memory, so close to cold). Every time was taken after confirming the result matches the expected answer exactly. Full tables: [docs/benchmarks.md](https://github.com/amru195704/UwDiff/blob/main/docs/benchmarks.md).

### ⚠️ Notes

- Word, Excel and PDF are compared by their **extracted text** (formatting, images and formulas are not compared). Legacy .doc and .xls, password-protected files and PDFs without text cannot be read.
- Excel sheet matching and order-independent comparison list differences but cannot be applied.
- Where millions of lines are rearranged in one place, that range may be reported as a replacement (a correct diff, but not necessarily the minimal one).
- CLI output is in Japanese (the GUI switches between Japanese and English).

### 📥 Download

Get the files from **Assets** below: a dmg for the Mac (`arm64` for Apple silicon M1–M4, `x64` for Intel), a zip for Windows (`x64`, `arm64`), a tar.gz for Linux (`x86_64`, `aarch64`). No separate .NET installation is needed.

- Mac: open the dmg and drag UwDiff to Applications. The CLI is `UwDiff.app/Contents/MacOS/uvd`
- Windows / Linux: extract anywhere; the GUI (`UwDiff`) and the CLI (`uvd`) are side by side
- To run `uvd` by name in a terminal: Help → Command Line Setup… in the GUI

Checksums are in `SHA256SUMS-1.0.0.txt`.

```
72187ec32802616fd05b5e3531909233d357dbf637bb21228642e2dbbb315ac4  UwDiff-1.0.0-linux-aarch64.tar.gz
2f79466214a5043da59792b3c8f804aae43b5d36346669f70045a1761fd3011e  UwDiff-1.0.0-linux-x86_64.tar.gz
2d00447edf6ccc0ff625759dfa9a847bec480f926c1fe200f4cd8df501038482  UwDiff-1.0.0-mac-arm64.dmg
cd9be7a6de5ec2f28a7fc8e6e84fb02f01255d11637592699a32d00bf56c09bd  UwDiff-1.0.0-mac-x64.dmg
a4da8049e0fc5131ae6d481ee419616eeed4fc370bdab2f865149c84872555f5  UwDiff-1.0.0-win-arm64.zip
b8137d9a02c735c668efbf0751e2f85e7255fd7f04ad994b630dcd71cd6c340b  UwDiff-1.0.0-win-x64.zip
```

### 📖 Manual and license

- User manual: [docs/manual.en.md](https://github.com/amru195704/UwDiff/blob/main/docs/manual.en.md) · on the site: https://uvp.y42u.net/en/uwdiff-manual-en/
- Product page: https://uvp.y42u.net/en/uwdiff-en/
- License: [UwDiff License](https://github.com/amru195704/UwDiff/blob/main/LICENSE) (PolyForm Internal Use 1.0.0 plus a permission to redistribute the official binaries as packages). Free for internal use by individuals and companies. The official, unmodified binaries may be redistributed free of charge as packages (Homebrew, Scoop, Flathub, Snap, AUR and so on). Not open source.
- Bugs and requests: [Issues](https://github.com/amru195704/UwDiff/issues)

> **Sister tool**: to open and search huge files → [UwView](https://github.com/amru195704/UwView) (search with UwView, compare with UwDiff)
