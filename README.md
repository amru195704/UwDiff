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
| **大きさ・速さ** | CSV 10GB × 2 本のキー照合 約 21 秒、テキスト 10GB × 2 本 約 21 秒（Mac mini M4 の実測）。メモリを超えても一時ファイルへ逃がして止まらない |
| **OS** | macOS・Linux・Windows |

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
.NET を別に入れる必要はありません。

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
| `tools/` | 試験の自動実行（`run_tests.sh`）・危ないところの試験（`edgecases.py`・`textcases.py`・`formatcases.py`）・試験データの生成器・配布物の作成（`package.sh`） |

## 試験

```
tools/run_tests.sh                 # 10MB・100MB と、危ないところ・形式・画面の試験
tools/run_tests.sh 1GB 10GB        # 大きいもの（試験データを作る。10GB は全部で約 90GB）
```

試験データの置き場所は `DATA=…`（既定は `out/testdata`）。いつも同じ場所なら `tools/local.env` に `DATA=…` と書く（Git には入れない）。

## ライセンス

[UwDiff License](LICENSE)（PolyForm Internal Use 1.0.0 ＋ 公式バイナリのパッケージ再配布の許可。[日本語の参考訳](LICENSEjp.txt)）。
同梱しているものは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。問い合わせは [Issues](https://github.com/amru195704/UwDiff/issues) へ。
