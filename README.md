# UwDiff（UVD）

[English](README.en.md)

**どんな形式・文字コード・OS でも、多数・巨大ファイルを高速に比べる無料の道具。** 画面（UwDiff）と CLI（`uvd`）を同梱。

> ログでも CSV でも Excel でも PDF でも。Shift-JIS でも UTF-16 でも。1 本 10GB でも、1 万本のフォルダでも。

| | |
|---|---|
| **形式** | テキスト（ログ・CSV・TSV・ソース）／圧縮 7 形式（gz・bz2・xz・lzma・zst・lz4・br）の中身／Word（.docx）・Excel（.xlsx）・PDF の文字 |
| **文字コード** | UTF-8・Shift-JIS（CP932）・EUC-JP・UTF-16（BOM あり・なし）を自動判定。旧と新で違ってもよい |
| **比べ方** | テキスト（行の並びを保つ・unified diff）／表（キー列で照合・**行の順番が違ってもよい**・セル単位）／順番を問わない比較／フォルダ対フォルダ |
| **採る変更だけ反映** | 表の差分から採る行を選び、旧の文字コード・区切り・改行・引用符を保って新しいファイルを書く |
| **大きさ・速さ** | CSV 10GB × 2 本のキー照合 約 21 秒、テキスト 10GB × 2 本 約 21 秒（MacBook Air M4 の実測）。メモリを超えても一時ファイルへ逃がして止まらない |
| **OS** | macOS・Linux・Windows |

## 数字で見る UwDiff

10GB（約 1 億行）のファイル 2 本を、同じ Mac・同じ試験データで比べました。どの時間も、出た差分が正解と**完全に一致した**ものです。

| こんなとき | 相手 | **UwDiff** |
|---|---|---|
| **行の順番がばらばらの CSV をキー列で照合** | qsv diff は 10 分で終わらず打ち切り／DuckDB の SQL 220.06 秒 | **`uvd … -key` 22.61 秒** |
| **行の順番がそろった CSV をキー列で照合** | qsv diff 32.56 秒（1.5 倍）／DuckDB 124.66 秒（5.8 倍） | **21.52 秒** |
| **テキストを比べる（`diff -u` と同じ形）** | GNU diff 150.93 秒・17.23GiB／macOS の diff 289.24 秒・25.86GiB | **21.23 秒・0.41GiB** |
| **Shift-JIS と UTF-8 の CSV を比べる** | iconv で変換してから GNU diff 339.66 秒／変換してから qsv diff 160.58 秒 | **変換せずそのまま 26.15 秒（テキスト）・23.57 秒（キー照合）** |
| **画面で比べる** | KDiff3・P4Merge は 1GB で 600 秒たっても表示されず | **10GB を 21.91 秒で表示** |
| **Excel で開いて比べる** | 1,048,576 行で切り捨て、`1-10` が `1月10日` に変わる | **行数の上限なし・セルの文字はそのまま** |

**先に負けと同等を書きます。**

- **行の順番がそろった表なら、qsv diff は 1GB で同等**（2.73 秒 対 2.28 秒）、5GB・10GB でも 1.5〜1.6 倍の差です。**使うメモリはどの大きさでも qsv diff の方が少ない**です（10GB で qsv 1.7〜2.1GiB、UwDiff 4.2〜4.5GiB）。
- **行の順番がばらばらの表では、UwDiff は 10GB で 12.7〜13.4GiB 使います。** メモリの上限を決めれば（`-mem 476MB` など）一時ファイルへ逃がして最後まで通りますが、58〜60 秒かかります。
- **差分がいつも最小とは限りません。** 行が細かく入れ替わった試験データでは、GNU diff より 0.5% 多くなりました（どちらも、旧に当てると新とバイト単位で一致する正しい差分です）。
- **フォルダ 1 万本の比較は `diff -rq` と同等**です（3.1 秒 対 3.3 秒）。

> 1.5 倍未満の差は「同等」と書き、差とは呼びません。秒数は 1 台の Mac の値です（条件は[ページの最後](#数字の条件)）。**環境をまたいで秒数を比べないでください。**

---

## 表のキー照合：qsv diff・DuckDB と比べる

`uvd 旧.csv 新.csv -key 点名`／`qsv diff --key 0`／DuckDB の SQL で点名を結合。（ ）は UwDiff の何倍の時間がかかったか。

| 大きさ × 2 本 | 行順 | 文字コード | **UwDiff 1.0.0** | qsv diff 26.0.0 | DuckDB 2.0.0 |
|---|---|---|---|---|---|
| 1GB（1,053 万行） | そろっている | UTF-8 | **2.28 秒** | 2.73 秒（同等） | 4.05 秒（1.8 倍） |
| 1GB | そろっている | CP932 | **2.15 秒** | 2.84 秒（同等） | 11.39 秒（5.3 倍） |
| 1GB | ばらばら | UTF-8 | **2.27 秒** | 5.87 秒（2.6 倍） | 4.41 秒（1.9 倍） |
| 1GB | ばらばら | CP932 | **2.18 秒** | 5.85 秒（2.7 倍） | 11.70 秒（5.4 倍） |
| 5GB（5,267 万行） | そろっている | UTF-8 | **10.88 秒** | 17.15 秒（1.6 倍） | 23.33 秒（2.1 倍） |
| 5GB | そろっている | CP932 | **10.24 秒** | 15.65 秒（1.5 倍） | 59.67 秒（5.8 倍） |
| 5GB | ばらばら | UTF-8 | **11.11 秒** | 36.01 秒（3.2 倍） | 24.39 秒（2.2 倍） |
| 5GB | ばらばら | CP932 | **10.57 秒** | 35.76 秒（3.4 倍） | 60.65 秒（5.7 倍） |
| 10GB（1 億 535 万行） | そろっている | UTF-8 | **21.52 秒** | 32.56 秒（1.5 倍） | 124.66 秒（5.8 倍） |
| 10GB | そろっている | CP932 | **20.35 秒** | 32.56 秒（1.6 倍） | 188.34 秒（9.3 倍） |
| 10GB | ばらばら | UTF-8 | **22.61 秒** | 10 分で終わらず打ち切り | 220.06 秒（9.7 倍） |
| 10GB | ばらばら | CP932 | **21.90 秒** | 10 分で終わらず打ち切り | 357.91 秒（16.3 倍） |

- **メモリ（10GB）**: 行順がそろっていれば UwDiff 4.2〜4.5GiB・qsv 1.7〜2.1GiB（**qsv の方が少ない**）・DuckDB 約 18GiB。ばらばらだと UwDiff 12.7〜13.4GiB・DuckDB 20〜21GiB、qsv は約 18GiB まで使ったところで打ち切り。
- UwDiff は文字コードを自動で見分けるので、CP932 でも UTF-8 と同じ速さです。DuckDB は CP932 を読むのに拡張と指定が要り、時間も UTF-8 の 1.5〜2.8 倍に増えました。

**Beyond Compare・Excel**（参考・2026-10-23・同じ Mac・別の試験データ）

- **Beyond Compare 5.2.6**（表の比較・キー指定）: 10 万行（10MB）約 145 秒、52.6 万行（50MB）16 分 18 秒（件数は正解と一致）。105 万行（100MB）は 20 分で終わらず中止。CP932 は自動判定されず、Shift-JIS を手で指定しても `①`・`髙` などでエラー。
- **Excel（Mac）**: 1,048,576 行で切り捨て。`1-10` が `1月10日` に変わる。BOM の無い UTF-8 を Shift-JIS として読む。

→ 読み物: [Excel に入らない CSV を先月版と比べる](https://uvp.y42u.net/blog/uwdiff-excel-csv-monthly-compare/)

---

## テキスト：GNU diff・macOS の diff と比べる

`uvd 旧 新 -o 差分.diff`／`diff -u 旧 新`。行の並びを保つ比較（unified diff）、行順そろい。時間・メモリ。

| 大きさ × 2 本 | 文字コード | **UwDiff 1.0.0** | GNU diff 3.12 | macOS の diff |
|---|---|---|---|---|
| 1GB | UTF-8 | **2.37 秒**・0.31GiB | 8.24 秒（3.5 倍）・3.15GiB | 11.00 秒（4.6 倍）・2.87GiB |
| 1GB | CP932 | **2.23 秒**・0.29GiB | 8.50 秒（3.8 倍）・3.04GiB | 10.70 秒（4.8 倍）・2.76GiB |
| 5GB | UTF-8 | **10.74 秒**・0.41GiB | 41.24 秒（3.8 倍）・13.78GiB | 96.82 秒（9.0 倍）・14.39GiB |
| 5GB | CP932 | **9.86 秒**・0.39GiB | 42.77 秒（4.3 倍）・13.21GiB | 86.98 秒（8.8 倍）・13.82GiB |
| 10GB | UTF-8 | **21.23 秒**・0.41GiB | 150.93 秒（7.1 倍）・17.23GiB | 289.24 秒（13.6 倍）・25.86GiB |
| 10GB | CP932 | **20.77 秒**・0.41GiB | 117.92 秒（5.7 倍）・18.54GiB | 279.68 秒（13.5 倍）・26.19GiB |

- **差分の行数**（10GB）: UwDiff 254.7 万行・GNU diff 256.5 万行・macOS の diff 256.6 万行。3 つとも、旧に当てると新とバイト単位で一致することを確かめました。
- **大きくなるほど差が開く理由**: GNU diff は 2 本を**丸ごとメモリに読み終えてから**全行をハッシュし、全行をまとめて Myers のアルゴリズムにかけます。UwDiff は 2 本を別のスレッドで 8MB ずつ**読みながら**比べ、違いの周りの窓だけ Myers をかけます。だからメモリは 0.4GiB 前後で止まり、時間は「読む時間＋α」になります。その代わり、差分がいつも最小とは限りません（上の「負け」）。
- 1.1.0 から、先頭から同じ部分は行に切らずにバイト列のまま飛ばします（GNU diff の先頭の切り落としと同じ考え）。1GB で先頭からの同じ部分が長いときは約 3 倍速く、10GB は読む時間で決まるので変わりません（[docs/benchmarks.md](docs/benchmarks.md)）。

→ 読み物: [GNU diff はなぜ 10GB で 150 秒かかるのか（ソースを読んだ）](https://uvp.y42u.net/blog/uwdiff-gnu-diff-grep-source/)

---

## 文字コードが違う 2 本：変換してから比べる手順と比べる

旧は UTF-8（LF）、新は Shift-JIS（CRLF）の CSV。`diff` はバイトを比べるので、そのままでは日本語を含む行がほぼ全部「違う」になります。定番は `iconv -f CP932` で変換してから比べる手順です。

| 10GB（1 億 534 万行）× 2 本 | 変換 | 比較 | 合計 |
|---|---|---|---|
| iconv ＋ GNU diff（`-u --strip-trailing-cr`） | 126.11 秒 | 213.55 秒 | 339.66 秒 |
| iconv ＋ qsv diff（キー照合） | 126.11 秒 | 34.47 秒 | 160.58 秒 |
| **UwDiff（変換せずそのまま・テキスト）** | — | — | **26.15 秒** |
| **UwDiff（変換せずそのまま・キー照合）** | — | — | **23.57 秒** |

- UwDiff は読みながら UTF-8 にそろえるので、変換したファイル（9.4GB）の書き出しと空き容量が要りません。
- **同じ文字コードどうしより 1.1〜1.3 倍遅くなります**（そろえる分）。同じ 2 本を何度も比べるなら、最初に 1 回変換すれば qsv diff でも同等です。
- 画面の比較ツールも既定のままでは化けます。KDiff3 1.12.6 は UTF-8 として読み、P4Merge 2026.3 は「System」のまま。どちらも開く前に文字コードを指定する必要があります。

→ 読み物: [Shift-JIS と UTF-8 の CSV を比べる](https://uvp.y42u.net/blog/uwdiff-sjis-utf8-csv-compare/)

---

## 画面：KDiff3・P4Merge と比べる

テキスト・UTF-8・「比べる」から表示し終わるまで。

| 画面の比較ツール | 100MB | 1GB | 5GB | 10GB |
|---|---|---|---|---|
| **UwDiff 1.0.0** | **2.22 秒** | **3.22 秒** | **11.23 秒** | **21.91 秒・1.37GiB** |
| P4Merge 2026.3 | 12.61 秒・3.77GiB | 600 秒で表示されず | — | — |
| KDiff3 1.12.6 | 59.26 秒・1.27GiB | 600 秒で表示されず | — | — |

- CP932 でも UwDiff は 1.35 → 3.27 → 10.83 → 19.51 秒で、文字コードの指定なしに読めました。
- 10GB（変更 116 万か所）を開いたあと、最後の変更へ飛ぶのは 27ms です。
- 画面の道具は「終わった」を外から知る方法がないので、アプリが CPU を使い終えて表示し終わった時刻を 0.2 秒ごとに見て測りました（誤差はおよそ ±0.2 秒）。

**CLI で比べて、結果をそのまま画面で開けます。**

```
uvd 旧.log.gz 新.log.gz -open
```

→ 読み物: [なぜ UwDiff を作ったか](https://uvp.y42u.net/blog/uwdiff-why-built/)

---

## できることの比べ

| | **UwDiff** | Beyond Compare 5 | qsv diff | DuckDB | GNU diff |
|---|---|---|---|---|---|
| 画面（GUI） | ○ | ○ | ×（画面は別製品） | △（SQL を書く画面） | × |
| CLI | ○ `uvd` | ○（スクリプト） | ○ | ○（SQL） | ○ |
| CLI の結果をそのまま画面へ | ○ `-open` | — | — | — | — |
| 文字コード | **自動判定**・旧と新で違ってもよい | 手で指定 | UTF-8 が前提 | UTF-8・UTF-16・Latin-1（ほかは拡張） | バイトのまま |
| 表をキー列で照合（行順が違ってもよい） | ○（CSV・TSV・Excel） | ○ | ○ | ○（SQL を書く） | × |
| テキストを行の並びで比べる | ○ | ○ | × | × | ○ |
| フォルダどうし | ○ | ○ | × | × | ○（`-r`） |
| 圧縮ファイルの中身 | **7 形式** | gz・bz2・xz・zip・7z など | Snappy | gzip・zstd | × |
| Word・Excel・PDF | ○ | ○（読み取り専用） | xlsx を CSV に書き出す | xlsx を読む | × |
| 採る変更だけを反映 | ○（元の文字コード・区切り・改行を保つ） | ○（画面でコピー） | × | SQL を書く | `patch` で全部当てる |

各道具には、ここに挙げていない得意な所があります（Beyond Compare の画像・バイナリ比較や 3 方向マージ、qsv の多数の CSV 加工、DuckDB の分析 SQL など）。資料と確かめた日は[実測のページ](https://uvp.y42u.net/uwdiff-benchmarks/)にあります。

→ 読み物: [比べる道具を 6 つの軸で選ぶ](https://uvp.y42u.net/blog/uwdiff-six-axes/)

---

## どれを使えばいいか

| やりたいこと | 使うもの |
|---|---|
| **数 GB 以上の CSV・ログを比べる** | **`uvd`**（行の順番が違う表もキー列で） |
| **Shift-JIS と UTF-8 が混ざった 2 本を 1 回だけ比べる** | **`uvd`**（変換せずそのまま） |
| **同じ 2 本を何度も比べる・メモリを少なく** | 一度 UTF-8 に変換して **qsv diff** も手堅い（行順がそろっていれば同等の速さで、メモリは少ない） |
| **比べた結果を画面で見る** | **`uvd … -open`**、または UwDiff の画面 |
| **表の差分から採る変更だけを旧に反映** | **`uvd apply`**（旧の文字コード・区切り・改行・引用符を保つ） |
| **メモリに余裕で収まるテキストで、最小の差分が欲しい** | **GNU diff**（`--minimal`） |
| **画像・バイナリの比較・3 方向マージ** | Beyond Compare などの専用の道具 |
| **比べたうえで SQL で集計・分析** | **DuckDB** |

---

## 入手

**パッケージマネージャで入れる**（画面の UwDiff と CLI の `uvd` が両方入ります）

**Mac（Homebrew）**
```bash
brew install --cask amru195704/uwview/uwdiff
```
**Windows（Scoop）**
```powershell
scoop bucket add uwview https://github.com/amru195704/scoop-uwview   # 追加済みなら不要
scoop install uwdiff
```

**Windows で Scoop がまだ無いとき（初めて 1 回だけ）**
「PowerShell」で実行します（コマンドプロンプトではありません）。スタートメニューで「PowerShell」を検索し、**管理者ではない**普通の権限で開きます。行の頭が `PS C:\Users\...>` なら PowerShell です。
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser   # 聞かれたら Y
Invoke-RestMethod -Uri https://get.scoop.sh | Invoke-Expression        # Scoop を入れる
scoop install git                                                        # バケットの追加に git が要る
```
このあと上の `scoop bucket add …` と `scoop install uwdiff` を実行します。`Set-ExecutionPolicy` が「見つからない」と出るときは、コマンドプロンプトで打っています。
**Linux（Snap）**
```bash
sudo snap install uwdiff
```
Snap では CLI は `uwdiff.uvd` という名前になります（`sudo snap alias uwdiff.uvd uvd` で `uvd` と打てます）。

Homebrew と Scoop は、どちらもこの [Releases](https://github.com/amru195704/UwDiff/releases) の配布物を取ってきて SHA256 を照合します。
更新は `brew upgrade --cask uwdiff` ／ `scoop update uwdiff` ／ `sudo snap refresh uwdiff`。

**手で入れる**: [Releases](https://github.com/amru195704/UwDiff/releases) から、OS に合った配布物（mac は .dmg、Linux は .tar.gz、Windows は .zip）を。
.NET を別に入れる必要はありません。サイトの[ダウンロードページ](https://uvp.y42u.net/uwdiff-download/)からも入手できます。

## 使い方

画面: UwDiff を開き、旧・新を選んで（または窓へ落として）「比べる」。

CLI:

```
uvd 旧.log 新.log -o 差分.diff                  # テキスト（diff -u と同じ形）
uvd 旧.log.gz 新.log.gz -open                   # 圧縮のまま比べ、結果を画面で開く
uvd 旧.csv 新.csv -key 点名 -o 差分.csv         # 表をキーで照合
uvd 台帳_9月.xlsx 台帳_10月.xlsx -key 点名       # Excel のシートごと
uvd 納品_前回/ 納品_今回/ -o 一覧.csv           # フォルダ対フォルダ
uvd apply 旧.csv 新.csv 採る差分.csv -o 結果.csv  # 採った変更だけを旧に反映
```

終了コードは diff と同じ（0 = 同じ・1 = 違いあり・2 = エラー）。詳しくは [操作マニュアル](docs/manual.ja.md)（[English](docs/manual.en.md)）。

---

## もっと詳しく

| | |
|---|---|
| 🏠 **製品ページ** | [UwDiff](https://uvp.y42u.net/uwdiff/)（[English](https://uvp.y42u.net/en/uwdiff-en/)） |
| 📖 **操作マニュアル** | [docs/manual.ja.md](docs/manual.ja.md) ・ [サイト版](https://uvp.y42u.net/uwdiff-manual/)（[English](https://uvp.y42u.net/en/uwdiff-manual-en/)） |
| 📊 **実測の全データと条件** | [実測のページ](https://uvp.y42u.net/uwdiff-benchmarks/)（[English](https://uvp.y42u.net/en/uwdiff-benchmarks-en/)）・[docs/benchmarks.md](docs/benchmarks.md)（UwDiff だけの数字・1.1.0 の比較）。**負けている数字もそのまま載せています** |
| 🧾 **リリースノート** | [v2.0.0](https://github.com/amru195704/UwDiff/releases/tag/v2.0.0)（前の版 [v1.0.0](https://github.com/amru195704/UwDiff/releases/tag/v1.0.0)） |
| ▶ **動画（YouTube）** | [操作デモ（日本語）](https://www.youtube.com/watch?v=qc1sY8wmOTc) ・ [English](https://www.youtube.com/watch?v=TeDYh7_MsjU) |
| 📖 **比べる道具を 6 つの軸で選ぶ** | [記事](https://uvp.y42u.net/blog/uwdiff-six-axes/) |
| 📖 **なぜ UwDiff を作ったか** | [記事](https://uvp.y42u.net/blog/uwdiff-why-built/) |
| 📖 **GNU diff と grep のソースを読んだ** | [記事](https://uvp.y42u.net/blog/uwdiff-gnu-diff-grep-source/) ・ [Zenn](https://zenn.dev/amru195704/articles/0249977bbad7e0) |
| 📖 **Shift-JIS と UTF-8 の CSV を比べる** | [記事](https://uvp.y42u.net/blog/uwdiff-sjis-utf8-csv-compare/) ・ [Qiita](https://qiita.com/amru195704/items/df6acc003865eb4bd028) |
| 📖 **Excel に入らない CSV を先月版と比べる** | [記事](https://uvp.y42u.net/blog/uwdiff-excel-csv-monthly-compare/) |
| 📝 **note マガジン** | [巨大ファイルを比べる——UwDiff 開発と使い方](https://note.com/amru1957/m/m40e91e68f650) |
| 🔎 **巨大ファイルを探して読むなら** | 姉妹品の [UwView](https://github.com/amru195704/UwView)（検索コマンド `uvf` と画面） |

---

## 数字の条件

- **機械**: MacBook Air（Apple M4・10 コア・メモリ 32GB）・macOS 26.3.1・データは外付け SSD（USB）。**秒数は環境によって変わります。環境をまたいで秒数を比べないでください。**
- **CLI の比較（表・テキスト）**: uvd 1.0.0・qsv 26.0.0・DuckDB 2.0.0・GNU diff 3.12・macOS の diff・2026-10-26。毎回キャッシュを消してから（`sudo purge`）1 回ずつ。秒は `/usr/bin/time` の real、メモリは maximum resident set size。10 分を超えたものは打ち切り。台本は `tools/bench_compare_mac.sh`。
- **文字コードが違う 2 本**: 同じ機械・同じ測り方・iconv は macOS 標準・2026-11-04。
- **画面**: UwDiff 1.0.0・KDiff3 1.12.6・P4Merge 2026.3・2026-10-27。台本は `tools/bench_gui_mac.sh`。
- **Beyond Compare・Excel**: 2026-10-23・別の試験データ（参考）。
- **試験データ**: CSV（列: 点名,X,Y,Z,種別,測点番号,観測日,備考）。新は旧から削除・追加・修正・点名の変更・引用符の中の改行を入れて作る（`tools/testdata/` の生成器・種は固定）。行順 そろっている＝旧と同じ、ばらばら＝全部並べ替え。UTF-8（LF）と CP932（CRLF）。
- **v2.0.0 も比べる処理は 1.x と同じ**です（2.0.0 で足したのは支援の案内と細かな修正）。
- 1.5 倍未満の差は「同等」と書いています。

---

## ソースから作る

.NET 10 SDK が要ります。

```
dotnet build -c Release
src/Uvd.Cli/bin/Release/net10.0/uvd -h          # CLI
dotnet run -c Release --project src/UwDiff.App  # 画面
tools/package.sh osx-arm64                      # 配布物（out/dist/）
```

## 構成

| 場所 | 中身 |
|---|---|
| `src/UwDiff.Core/` | 比較エンジン: 表のキー照合（`Diff/`）・テキストの比較（`Text/`）・フォルダ（`Folders/`）・反映（`Apply/`）・文字コード・入力の見分け |
| `src/UwView.Formats/` | [UwView](https://github.com/amru195704/UwView) の公開部品から写した、圧縮の展開と Word・Excel・PDF の文字の取り出し（変えずに写す。元は `UPSTREAM.md`） |
| `src/Uvd.Cli/` | CLI `uvd` |
| `src/UwDiff.App/` | 画面（Avalonia） |
| `src/UwDiff.UiTests/` | 画面の試験（Avalonia Headless） |
| `tools/` | 試験の自動実行（`run_tests.sh`）・危ないところの試験（`edgecases.py`・`textcases.py`・`formatcases.py`）・試験データの生成器・ほかの道具との比較の台本（`bench_compare_mac.sh`・`bench_gui_mac.sh`）・配布物の作成（`package.sh`） |

## 試験

```
tools/run_tests.sh                 # 10MB・100MB と、危ないところ・形式・画面の試験
tools/run_tests.sh 1GB 10GB        # 大きいもの（試験データを作る。10GB は全部で約 90GB）
```

試験データの置き場所は `DATA=…`（既定は `out/testdata`）。いつも同じ場所なら `tools/local.env` に `DATA=…` と書く（Git には入れない）。

## ライセンス

[UwDiff License](LICENSE)（PolyForm Internal Use 1.0.0 ＋ 公式バイナリのパッケージ再配布の許可。[日本語の参考訳](LICENSEjp.txt)）。
同梱しているものは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。問い合わせは [Issues](https://github.com/amru195704/UwDiff/issues) へ。
**手元の巨大ファイルで測った結果——とくに負けていた報告を歓迎します。**
