#!/usr/bin/env bash
# > パス: tools/sync_uwview.sh
# UwView.Core から、src/UwView.Formats/ に写したファイルを写し直す（ファイルは変えない。例外は UPSTREAM.md の「写した後に外すもの」）。UPSTREAM.md の commit も書き換える。
# 使い方: tools/sync_uwview.sh <UwView の作業ツリー>
set -eu
SRC="${1:?UwView の作業ツリーを指定してください}"
SELFDIR=$(cd "$(dirname "$0")" && pwd)
DST="${SELFDIR}/../src/UwView.Formats"
COMMIT=$(git -C "${SRC}" rev-parse HEAD)
grep -E '^  - `.*\.cs`$' "${DST}/UPSTREAM.md" | sed -E 's/^  - `(.*)`$/\1/' | while read -r f; do
  cp "${SRC}/UwView.Core/${f}" "${DST}/${f}"
  echo "写した: ${f}"
done
# 写した後に外すもの: UwDiff では使わない UwView Pro の案内（OfficeDocumentFile.UnsupportedText）
perl -0pi -e 's/\n\n *\/\/\/ <summary>この版では探さないときの文.*?UwView Pro";\n/\n/s' "${DST}/OfficeDocumentFile.cs"
if grep -q 'UnsupportedText' "${DST}/OfficeDocumentFile.cs"; then echo "OfficeDocumentFile.UnsupportedText を外せませんでした（UwView で形が変わった？）手で外してください" >&2; exit 1; fi
sed -i.bak -E "s/^- commit: \`[0-9a-f]+\`/- commit: \`${COMMIT}\`/" "${DST}/UPSTREAM.md" && rm -f "${DST}/UPSTREAM.md.bak"
echo "元の commit: ${COMMIT}（git diff で変わった所を確かめ、ビルドと試験をしてください）"
