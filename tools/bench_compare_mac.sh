#!/usr/bin/env bash
# > パス: tools/bench_compare_mac.sh
{ # ← 全体を { } で囲む（走っている最中に書き換えても壊れないように）
# UwDiff と他の道具を、同じ Mac・同じ試験データ（第 2 版）・同じ条件で測る（宣伝の比較表の材料）。
#   表のキー照合: uvd ／ qsv diff ／ DuckDB（SQL）   … 正解（answer.csv）と突き合わせる
#   テキストの比較: uvd ／ GNU diff（gdiff）／ macOS の diff … unified diff を旧に当てて新になるか確かめる
# 決まり: 各回の前に purge（cold）・/usr/bin/time -l で秒とピークメモリ・1 回 20 分で打ち切り（TIMEOUT）。
# 使い方: tools/bench_compare_mac.sh [大きさ...]   既定: 1GB 10GB（試験データが無い大きさは、ここで作る）
#   データは tools/local.env の DATA（run_tests.sh と同じ。中に 1GB/ 10GB/ がある）。
#   ENCS="utf8 cp932"  TABLE_ORDERS="keep shuffle"  TEXT_ORDERS="keep"  TOOLS="uvd qsv duckdb gdiff diff"
#   PURGE=0 で purge しない。TIMEOUT=1200（秒）。最初に sudo のパスワードを 1 回聞く。
#   入っていない道具は飛ばす（brew install qsv duckdb diffutils で入る。uvd は brew install --cask amru195704/uwview/uwdiff）。
set -u
SELFDIR=$(cd "$(dirname "$0")" && pwd)
[ -f "${SELFDIR}/local.env" ] && . "${SELFDIR}/local.env"
DATA="${DATA:?tools/local.env に DATA=… を書いてください}"
SIZES="${*:-1GB 10GB}"
ENCS="${ENCS:-utf8 cp932}"
TABLE_ORDERS="${TABLE_ORDERS:-keep shuffle}"
TEXT_ORDERS="${TEXT_ORDERS:-keep}"
TOOLS="${TOOLS:-uvd qsv duckdb gdiff diff}"
PURGE="${PURGE:-1}"
TIMEOUT="${TIMEOUT:-1200}"
PY=$(command -v python3) || { echo "python3 が要る"; exit 1; }
OUT="$(cd "${DATA}/.." && pwd)/out/compare-$(date +%Y%m%d-%H%M%S)"
mkdir -p "${OUT}"
LOG="${OUT}/run.log"; RES="${OUT}/results.tsv"
printf '道具\t版\t比べ方\t大きさ\t行順\t文字コード\t秒\tピークメモリGiB\t終了コード\t正しさ\n' > "${RES}"
log() { echo "$*" | tee -a "${LOG}"; }

UVD=$(command -v uvd || true); [ -z "${UVD}" ] && [ -x /Applications/UwDiff.app/Contents/MacOS/uvd ] && UVD=/Applications/UwDiff.app/Contents/MacOS/uvd
QSV=$(command -v qsv || true); DUCK=$(command -v duckdb || true); BDIFF=/usr/bin/diff
# GNU diff: gdiff の名前か、Homebrew の diffutils（diff の名前で入る）。GDIFF=… で指定もできる
GDIFF="${GDIFF:-$(command -v gdiff || true)}"
for g in /opt/homebrew/opt/diffutils/bin/diff /usr/local/opt/diffutils/bin/diff; do [ -z "${GDIFF}" ] && [ -x "$g" ] && GDIFF="$g"; done
has() { case " ${TOOLS} " in *" $1 "*) return 0;; esac; return 1; }
ver() { case "$1" in
  uvd) "${UVD}" --version 2>&1 | head -1;; qsv) "${QSV}" --version 2>&1 | head -1 | cut -c1-40;;
  duckdb) "${DUCK}" --version 2>&1 | head -1;; gdiff) "${GDIFF}" --version 2>&1 | head -1;;
  diff) "${BDIFF}" --version 2>&1 | head -1;; esac; }

log "=== $(date '+%F %T') 他の道具との比較（データ: ${DATA}）"
{ sw_vers; sysctl -n machdep.cpu.brand_string; echo "メモリ $(( $(sysctl -n hw.memsize) / 1073741824 ))GiB"; df -h "${DATA}" | tail -1; } 2>/dev/null | tee -a "${LOG}"
for t in uvd qsv duckdb gdiff diff; do
  case "$t" in uvd) p="${UVD}";; qsv) p="${QSV}";; duckdb) p="${DUCK}";; gdiff) p="${GDIFF}";; diff) p="${BDIFF}";; esac
  if has "$t" && [ -n "${p}" ] && [ -x "${p}" ]; then log "  $t: ${p}（$(ver "$t")）"; else log "  $t: 無し・または対象外 → 飛ばす"; TOOLS=$(echo " ${TOOLS} " | sed "s/ $t / /"); fi
done
[ "${PURGE}" = "1" ] && { sudo -v || exit 1; ( while true; do sudo -n true; sleep 50; done ) 2>/dev/null & KEEP=$!; trap 'kill ${KEEP} 2>/dev/null' EXIT; }

cold() { if [ "${PURGE}" = "1" ]; then sync; sudo purge; sleep 10; fi; }
# 秒とピークメモリ。TIMEOUT 秒で打ち切る（perl の alarm）
timed() { local NAME="$1"; shift; local TF="${OUT}/${NAME}.time"
  /usr/bin/time -l perl -e '$t=shift; alarm $t; exec @ARGV or die' "${TIMEOUT}" "$@" > "${OUT}/${NAME}.stdout" 2> "${TF}"; RC=$?
  T=$(grep -E ' real' "${TF}" | awk '{print $1}'); M=$(grep -E 'maximum resident' "${TF}" | awk '{printf "%.2f", $1/1073741824}')
  [ ${RC} -eq 142 ] && T=">${TIMEOUT}"; }
row() { printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$1" "$(ver "$1")" "$2" "$3" "$4" "$5" "${T:-}" "${M:-}" "${RC:-}" "$6" >> "${RES}"
  log "  $1 $2 $3 $4 $5: ${T:-}秒 ${M:-}GiB rc=${RC:-} $6"; }

DKSQL="${SELFDIR}/testdata/duckdb_keydiff.sql.in"
for S in ${SIZES}; do
  D="${DATA}/${S}"
  if [ ! -f "${D}/answer.csv" ]; then
    # 試験データが無ければ作る（run_tests.sh と同じ生成器・同じ種。計測に使う行順だけ）
    GEN="${OUT}/uwdiff_gen2"
    [ -x "${GEN}" ] || cc -O2 -o "${GEN}" "${SELFDIR}/testdata/uwdiff_gen2.c" -liconv 2>/dev/null || cc -O2 -o "${GEN}" "${SELFDIR}/testdata/uwdiff_gen2.c" || { log "--- ${S}: 生成器のビルドに失敗 → 飛ばす"; continue; }
    case "${S}" in *MB) B=$(( ${S%MB} * 1000000 ));; *GB) B=$(( ${S%GB} * 1000000000 ));; *) B="${S}";; esac
    N=$("${GEN}" count "${B}"); mkdir -p "${D}"
    log "--- ${S}: 試験データを作る（旧 ${N} 行・行順 $(echo ${TABLE_ORDERS} ${TEXT_ORDERS} | tr ' ' '\n' | sort -u | tr '\n' ' ')）"
    for E in ${ENCS}; do
      "${GEN}" old "${N}" "${E}" "${D}/old_${E}.csv.part" && mv "${D}/old_${E}.csv.part" "${D}/old_${E}.csv"
      for O in $(echo ${TABLE_ORDERS} ${TEXT_ORDERS} | tr ' ' '\n' | sort -u); do
        "${GEN}" new "${N}" "${E}" "${O}" "${D}/new_${O}_${E}.csv.part" && mv "${D}/new_${O}_${E}.csv.part" "${D}/new_${O}_${E}.csv"
      done
    done
    "${GEN}" answer "${N}" "${D}/answer.csv.part" && mv "${D}/answer.csv.part" "${D}/answer.csv"
    "${GEN}" stats "${N}" > "${D}/stats.txt"; echo "${N}" > "${D}/rows.txt"
    log "    $(du -sh "${D}" | cut -f1) 作成"
  fi
  log "--- ${S}（旧 $(cat "${D}/rows.txt" 2>/dev/null) 行）"
  for E in ${ENCS}; do
    PE=utf-8; [ "${E}" = cp932 ] && PE=cp932
    # 表のキー照合
    for O in ${TABLE_ORDERS}; do
      OLD="${D}/old_${E}.csv"; NEW="${D}/new_${O}_${E}.csv"; [ -f "${NEW}" ] || continue
      if has uvd; then N="uvd_key_${S}_${O}_${E}"; cold
        timed "${N}" "${UVD}" "${OLD}" "${NEW}" -key 点名 -q -o "${OUT}/${N}.csv"
        if [ ${RC} -le 1 ]; then C=$("${PY}" -I "${SELFDIR}/testdata/check_answer.py" "${OUT}/${N}.csv" "${D}/answer.csv" 2>&1); else C="失敗"; fi
        row uvd 表のキー照合 "${S}" "${O}" "${E}" "${C}"; rm -f "${OUT}/${N}.csv"; fi
      if has qsv; then N="qsv_key_${S}_${O}_${E}"; cold
        timed "${N}" "${QSV}" diff --key 0 -o "${OUT}/${N}.raw.csv" "${OLD}" "${NEW}"
        if [ ${RC} -le 1 ] && [ -s "${OUT}/${N}.raw.csv" ]; then
          "${PY}" -I "${SELFDIR}/testdata/qsvdiff_to_answer.py" "${OUT}/${N}.raw.csv" "${PE}" "${OUT}/${N}.csv" >> "${LOG}" 2>&1
          C=$("${PY}" -I "${SELFDIR}/testdata/check_answer.py" "${OUT}/${N}.csv" "${D}/answer.csv" 2>&1)
        else C="失敗"; fi
        row qsv 表のキー照合 "${S}" "${O}" "${E}" "${C}"; rm -f "${OUT}/${N}.csv" "${OUT}/${N}.raw.csv"; fi
      if has duckdb; then N="duckdb_key_${S}_${O}_${E}"; cold
        if [ "${E}" = cp932 ]; then LE="INSTALL encodings; LOAD encodings;"; EN="encoding='glibc-CP932-2.3.3'"; else LE=""; EN="encoding='utf-8'"; fi
        sed -e "s#@OLD@#${OLD}#; s#@NEW@#${NEW}#; s#@OUT@#${OUT}/${N}.csv#; s#@ENC@#${EN}#g; s#@LOADENC@#${LE}#" "${DKSQL}" > "${OUT}/${N}.sql"
        timed "${N}" "${DUCK}" -c ".read ${OUT}/${N}.sql"
        if [ ${RC} -eq 0 ] && [ -s "${OUT}/${N}.csv" ]; then C=$("${PY}" -I "${SELFDIR}/testdata/check_answer.py" "${OUT}/${N}.csv" "${D}/answer.csv" 2>&1)
        else C="失敗（$(grep -m1 -i 'error' "${OUT}/${N}.time" | cut -c1-100)）"; fi
        row duckdb 表のキー照合 "${S}" "${O}" "${E}" "${C}"; rm -f "${OUT}/${N}.csv"; fi
    done
    # テキストの比較（unified diff）
    for O in ${TEXT_ORDERS}; do
      OLD="${D}/old_${E}.csv"; NEW="${D}/new_${O}_${E}.csv"; [ -f "${NEW}" ] || continue
      for t in uvd gdiff diff; do has "$t" || continue
        N="${t}_text_${S}_${O}_${E}"; cold
        case "$t" in
          uvd)   timed "${N}" "${UVD}" "${OLD}" "${NEW}" -q -o "${OUT}/${N}.diff";;
          gdiff) timed "${N}" sh -c 'exec "$1" -u "$2" "$3" > "$4"' sh "${GDIFF}" "${OLD}" "${NEW}" "${OUT}/${N}.diff";;
          diff)  timed "${N}" sh -c 'exec "$1" -u "$2" "$3" > "$4"' sh "${BDIFF}" "${OLD}" "${NEW}" "${OUT}/${N}.diff";;
        esac
        if [ ${RC} -le 1 ] && [ -s "${OUT}/${N}.diff" ]; then
          L=$(grep -c '^[-+][^-+]' "${OUT}/${N}.diff" 2>/dev/null || echo 0)
          C=$("${PY}" -I "${SELFDIR}/testdata/check_unified.py" "${OLD}" "${NEW}" "${OUT}/${N}.diff" 2>&1 | tail -1)"／差分の行 ${L}"
        else C="失敗"; fi
        row "$t" テキストの比較 "${S}" "${O}" "${E}" "${C}"; rm -f "${OUT}/${N}.diff"
      done
    done
  done
done
log "=== $(date '+%F %T') 終わり。結果: ${RES}"
column -t -s $'\t' "${RES}" | tee -a "${LOG}"
exit 0
}
