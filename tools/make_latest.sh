#!/usr/bin/env bash
# > パス: tools/make_latest.sh
# out/dist/latest/ に「版数を外した名前」の複製を作る（配布ページのリンクを版ごとに書き換えなくて済むように。UwView の build/make-latest.sh と同じ作り）。
#   UwDiff-1.0.0-linux-x86_64.tar.gz → latest/UwDiff-linux-x86_64.tar.gz
# latest/ には SHA256SUMS（版数なしの名前で計算）と VERSION（中身の版数）も置く。
# 6 点（mac arm64/x64・linux x86_64/aarch64・win x64/arm64）が揃っていなければ作らない（古い版と混ざるため）。
# 使い方: tools/make_latest.sh [版数] [dist]   既定: Directory.Build.props の版数・out/dist
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

PRODUCT=UwDiff
VER="${1:-$(grep -oE '<InformationalVersion>[^<]+' Directory.Build.props | sed 's/<InformationalVersion>//' | head -1)}"
DIST="${2:-out/dist}"
LATEST="$DIST/latest"
SUFFIXES=(mac-arm64.dmg mac-x64.dmg linux-x86_64.tar.gz linux-aarch64.tar.gz win-x64.zip win-arm64.zip)

missing=()
for s in "${SUFFIXES[@]}"; do
  [[ -f "$DIST/$PRODUCT-$VER-$s" ]] || missing+=("$PRODUCT-$VER-$s")
done
if [[ ${#missing[@]} -gt 0 ]]; then
  echo "latest/ は作りません（揃っていない: ${missing[*]}）" >&2
  exit 0
fi

rm -rf "$LATEST"
mkdir -p "$LATEST"
for s in "${SUFFIXES[@]}"; do
  cp -p "$DIST/$PRODUCT-$VER-$s" "$LATEST/$PRODUCT-$s"
done
( cd "$LATEST" && shasum -a 256 "${SUFFIXES[@]/#/$PRODUCT-}" > SHA256SUMS )
echo "$VER" > "$LATEST/VERSION"

# 複製が元と同じ中身か（名前だけ違う）を確かめる
for s in "${SUFFIXES[@]}"; do
  cmp -s "$DIST/$PRODUCT-$VER-$s" "$LATEST/$PRODUCT-$s" || { echo "複製が元と一致しません: $s" >&2; exit 1; }
done

echo ""
echo "latest/（$PRODUCT $VER・版数なしの名前）:"
ls -l "$LATEST"
cat "$LATEST/SHA256SUMS"
