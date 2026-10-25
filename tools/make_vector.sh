#!/usr/bin/env bash
# > パス: tools/make_vector.sh
# Vector 投稿用の zip を作る（UwView の dist/vector と同じ作り。Windows x64 だけ）。
#   out/dist/UwDiff-<版>-win-x64.zip（tools/package.sh が作る）を展開し、
#   ・マークダウンのマニュアルを外し、日本語の操作マニュアルの PDF（UwDiff_Manual_ja.pdf）を入れる
#   ・Vector の書式の UwDiff_Readme.txt（docs/vector/UwDiff_Readme.txt。UTF-8・BOM 付き）を入れる
#   ・最上位にフォルダーを作らない平らな zip にまとめる
# 生成物（out/dist/vector/）:
#   UwDiff-<版>-win-x64.zip            投稿する zip
#   UwDiff-<版>-win-x64/                その中身（確かめ用）
#   UwDiff_Readme-<版>.txt              Readme（投稿の画面に貼る用）
#   SHA256SUMS-<版>-vector.txt          zip の SHA256
# 要るもの: unzip・zip・pandoc・Google Chrome（PDF を刷る）
# 使い方: tools/make_vector.sh [版数]   既定: Directory.Build.props の版数
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

NAME=UwDiff
VER="${1:-$(grep -oE '<InformationalVersion>[^<]+' Directory.Build.props | sed 's/<InformationalVersion>//' | head -1)}"
SRC="out/dist/$NAME-$VER-win-x64.zip"
V="out/dist/vector"
D="$V/$NAME-$VER-win-x64"
CHROME="${CHROME:-/Applications/Google Chrome.app/Contents/MacOS/Google Chrome}"
[ -f "$SRC" ] || { echo "$SRC がありません（先に tools/package.sh win-x64）" >&2; exit 1; }
command -v pandoc >/dev/null || { echo "pandoc がありません（brew install pandoc）" >&2; exit 1; }
[ -x "$CHROME" ] || { echo "Google Chrome がありません（CHROME=… で場所を指定）" >&2; exit 1; }
grep -q "$NAME $VER" docs/vector/UwDiff_Readme.txt || { echo "docs/vector/UwDiff_Readme.txt の版数が $VER ではありません（書き直してください）" >&2; exit 1; }

mkdir -p "$V"
rm -rf "$D" "$V/$NAME-$VER-win-x64.zip"
TMP=$(mktemp -d)
unzip -q "$SRC" -d "$TMP"
mv "$TMP/$NAME-$VER" "$D"
rm -rf "$TMP"
rm -f "$D"/manual.*.md

# 操作マニュアルの PDF（docs/manual.ja.md → HTML → Chrome で刷る）
HTML=$(mktemp -t uwdiff-manual).html
pandoc docs/manual.ja.md -s -f gfm -t html5 --metadata pagetitle="UwDiff 操作マニュアル" --metadata lang=ja \
  --css "$(pwd)/docs/vector/manual.css" --embed-resources -o "$HTML" 2>/dev/null \
  || pandoc docs/manual.ja.md -s -f gfm -t html5 --metadata pagetitle="UwDiff 操作マニュアル" --metadata lang=ja \
       --css "file://$(pwd)/docs/vector/manual.css" -o "$HTML"
"$CHROME" --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf="$(pwd)/$D/${NAME}_Manual_ja.pdf" "file://$HTML" >/dev/null 2>&1
rm -f "$HTML"
[ -s "$D/${NAME}_Manual_ja.pdf" ] || { echo "PDF を作れませんでした" >&2; exit 1; }

# Readme（UTF-8・BOM 付き。UwView と同じ）
readme() { printf '\xef\xbb\xbf'; cat docs/vector/UwDiff_Readme.txt; }
readme > "$D/${NAME}_Readme.txt"
readme > "$V/${NAME}_Readme-$VER.txt"

# 平らな zip（Readme を先頭に。UwView と同じ並び）
( cd "$D" && zip -qrX "../$NAME-$VER-win-x64.zip" "${NAME}_Readme.txt" UwDiff.exe uvd.exe third-party THIRD-PARTY-NOTICES.md "${NAME}_Manual_ja.pdf" LICENSE LICENSEjp.txt )
( cd "$V" && shasum -a 256 "$NAME-$VER-win-x64.zip" > "SHA256SUMS-$VER-vector.txt" )

echo "Vector 投稿用（$NAME $VER）: $V"
ls -l "$V"
unzip -l "$V/$NAME-$VER-win-x64.zip"
cat "$V/SHA256SUMS-$VER-vector.txt"
