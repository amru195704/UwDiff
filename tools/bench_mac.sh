#!/usr/bin/env bash
# > パス: tools/bench_mac.sh
{ # ← 全体を { } で囲む（走っている最中に書き換えても壊れないように）
# UwDiff 段階 0: uvd（照合エンジンの試作）を Mac で測る。
#   1) Release でビルド
#   2) 読み込みだけの速さ（10GB: 2 本を同時に読む／1 本ずつ読む）… 方式検討の「交互に読むと遅くならないか」
#   3) 照合: 100MB・1GB・5GB・10GB × UTF-8・CP932 を cold（purge 後）と hot で 1 回ずつ
#   4) 正解との突き合わせは Claude が後で行う（差分は out/uvd/ に残す）
# 使い方: ./tools/bench_mac.sh [データのフォルダ] [大きさ...]
#   データ: 引数 1 か、tools/local.env の BENCH_DATA（Git に入れない）   既定の大きさ: 100MB 1GB 5GB 10GB
#   PURGE=0 で purge しない（cold は参考値）。最初に sudo のパスワードを 1 回聞く。
set -u
SELFDIR=$(cd "$(dirname "$0")" && pwd)
REPO=$(cd "${SELFDIR}/.." && pwd)
[ -f "$(dirname "$0")/local.env" ] && . "$(dirname "$0")/local.env"
DATA="${1:-${BENCH_DATA:-}}"
[ -n "${DATA}" ] || { echo "使い方: tools/bench_mac.sh <試験データ 第 1 版のフォルダ> [大きさ...]（または tools/local.env に BENCH_DATA=…）"; exit 2; }
shift || true
SIZES="${*:-100MB 1GB 5GB 10GB}"
OUT="$(cd "${DATA}/.." && pwd)/out/uvd"
PURGE="${PURGE:-1}"
SETTLE="${SETTLE:-10}"
mkdir -p "${OUT}"
LOG="${OUT}/bench.log"

echo "=== $(date '+%F %T') 段階 0 の計測" | tee -a "${LOG}"
{ sw_vers 2>/dev/null; sysctl -n machdep.cpu.brand_string hw.memsize 2>/dev/null; dotnet --version; } | tee -a "${LOG}"

# 1) ビルド
( cd "${REPO}" && dotnet build src/Uvd.Cli/Uvd.Cli.csproj -c Release -v q -nologo ) || { echo "ビルドに失敗" | tee -a "${LOG}"; exit 1; }
UVD="${REPO}/src/Uvd.Cli/bin/Release/net10.0/uvd"
[ -x "${UVD}" ] || { echo "uvd が見つからない: ${UVD}" | tee -a "${LOG}"; exit 1; }
echo "uvd: ${UVD}（コミット $(git -C "${REPO}" --no-optional-locks rev-parse --short HEAD 2>/dev/null || echo なし)）" | tee -a "${LOG}"

if [ "${PURGE}" = "1" ]; then sudo -v || exit 1; fi
drop_cache() {
  if [ "${PURGE}" = "1" ]; then sync; sudo purge; sleep "${SETTLE}"; fi
}

run() { # 名前 旧 新 追加オプション...
  local NAME="$1" OLD="$2" NEW="$3"; shift 3
  /usr/bin/time -l "${UVD}" "${OLD}" "${NEW}" -key 点名 -q -json "${OUT}/${NAME}.json" "$@" > "${OUT}/${NAME}.txt" 2> "${OUT}/${NAME}.time"
  local RC=$?
  local T M
  T=$(grep -E ' real' "${OUT}/${NAME}.time" | awk '{print $1}')
  M=$(grep -E 'maximum resident' "${OUT}/${NAME}.time" | awk '{print $1}')
  echo "${NAME},終了コード=${RC},秒=${T},ピークメモリB=${M}" | tee -a "${LOG}"
  grep -E '^(時間|待ち行|内訳)' "${OUT}/${NAME}.txt" | sed 's/^/    /' | tee -a "${LOG}"
}

# 2) 読み込みだけ（10GB があれば）
if [ -f "${DATA}/10GB/uwt_10GB_old_utf8.csv" ]; then
  O="${DATA}/10GB/uwt_10GB_old_utf8.csv"; N="${DATA}/10GB/uwt_10GB_new_utf8.csv"
  drop_cache; run "readonly_10GB_concurrent_cold" "${O}" "${N}" -parseonly -nodup
  drop_cache; run "readonly_10GB_serial_cold" "${O}" "${N}" -parseonly -nodup -serial
fi

# 3) 照合
for S in ${SIZES}; do
  for E in utf8 cp932; do
    O="${DATA}/${S}/uwt_${S}_old_${E}.csv"; N="${DATA}/${S}/uwt_${S}_new_${E}.csv"
    [ -f "${O}" ] || { echo "${S} ${E}: データなし" | tee -a "${LOG}"; continue; }
    drop_cache; run "diff_${S}_${E}_cold" "${O}" "${N}" -o "${OUT}/diff_${S}_${E}.csv"
    run "diff_${S}_${E}_hot" "${O}" "${N}" -o "${OUT}/diff_${S}_${E}_hot.csv"
  done
done
echo "完了: ${LOG}" | tee -a "${LOG}"
exit
}
