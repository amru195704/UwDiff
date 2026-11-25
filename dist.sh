#!/usr/bin/env bash
# UwDiff（画面 UwDiff と CLI uvd）の配布物を作る入口はこれ 1 本（2026-11-25。UwView・UwVGrep・UwGCnv と同じ形）。
#
#   ./dist.sh                 Mac（arm64）だけ → dist-test/                    ＝「dist 作成して」
#   ./dist.sh all             発売物 6 点（mac 2・Linux 2・Windows 2）→ dist/ と dist/latest/   ＝「dist-all」
#   ./dist.sh test [rid…]     試しに作る（rid を選べる）→ dist-test/   既定: osx-arm64 linux-arm64 win-arm64
#
# 出力は 2 つだけ: dist/（発売物）と dist-test/（試験の物）。途中の物は out/ の下。
# mac は tools/local.env（か環境変数）に MAC_SIGN_ID・AC_PROFILE があると署名・公証まで行う（Apple に送る。画面のロックを外しておく）。
# 版数は Directory.Build.props。部品（直接は呼ばない）: tools/package.sh・tools/make_latest.sh・tools/make_vector.sh。
# 動かさずに流れだけ見る: DRY_RUN=1 ./dist.sh …
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
run() { echo "+ $*"; [ -n "${DRY_RUN:-}" ] || env "$@"; }
case "${1:-mac}" in
  mac)  run OUT=dist-test tools/package.sh osx-arm64 ;;
  all)  run OUT=dist tools/package.sh ;;
  test) shift; rids=("$@"); [ ${#rids[@]} -eq 0 ] && rids=(osx-arm64 linux-arm64 win-arm64)
        run OUT=dist-test tools/package.sh "${rids[@]}" ;;
  *)    sed -n '2,7p' "$0" | sed 's/^# \{0,1\}//' >&2; exit 2 ;;
esac
