#!/usr/bin/env bash
# > パス: tools/bench_gui_mac.sh
{ # ← 全体を { } で囲む（走っている最中に書き換えても壊れないように）
# 画面の比較ツールを、UwDiff の画面と同じ Mac・同じ試験データ（第 2 版）・同じ測り方で測る（宣伝の比較表の材料）。
#   相手: KDiff3 ／ P4Merge ／ DiffMerge（無料・Mac で動くもの）。UwDiff も画面で同じように測る（画面どうしで比べる）。
#   比べ方: テキストの比較（旧 old_<enc>.csv と 新 new_keep_<enc>.csv）。表のキー照合はこの 3 つに無いので測らない。
# 測り方（画面の道具は「終わった」を外から知る手段が無いので、次の約束で測る）:
#   アプリを 2 つのファイルを渡して起動 → 0.5 秒ごとにアプリ（子プロセス含む）の CPU を見る →
#   0.2 秒ごとに CPU（1 コアを 100%）を見る。「20% 未満が 5 秒続いた」ら終わったとみなし、
#   その前で最後に「60% 以上」だった時刻を「表示し終わった時刻」とする（起動からの秒数）。誤差はおよそ ±0.2 秒。
#   ピークメモリは同じ間隔で見た RSS の最大。終わった時点の画面を screencapture で残す（本当に差分が出たかの証拠）。
#   600 秒（TIMEOUT）で打ち切り。打ち切り・異常終了した大きさより上は、その道具では測らない。
# 使い方: tools/bench_gui_mac.sh [大きさ...]   既定: 100MB 1GB 5GB 10GB
#   データは tools/local.env の DATA（bench_compare_mac.sh と同じ）。
#   ENCS="utf8 cp932"  TOOLS="uwdiff kdiff3 p4merge diffmerge"  PURGE=0 で purge しない。TIMEOUT=600。
#   IDLE_PCT=20 IDLE_SEC=5（「静かになった」の判定）。TRACE=1 で 0.5 秒ごとの CPU を記録に出す。最初に sudo のパスワードを 1 回聞く。
# 準備（1 回だけ）:
#   brew install --cask kdiff3 p4v（diffmerge は Homebrew で無効になったので外した）
#   各アプリを一度ふつうに起動し、初回の確認画面（「インターネットからダウンロード」など）を閉じておく。
#   「画面収録」の許可をターミナルに与える（システム設定 → プライバシーとセキュリティ → 画面収録。screencapture 用）。
set -u
SELFDIR=$(cd "$(dirname "$0")" && pwd)
[ -f "${SELFDIR}/local.env" ] && . "${SELFDIR}/local.env"
DATA="${DATA:?tools/local.env に DATA=… を書いてください}"
SIZES="${*:-100MB 1GB 5GB 10GB}"
ENCS="${ENCS:-utf8 cp932}"
TOOLS="${TOOLS:-uwdiff kdiff3 p4merge}"   # diffmerge は 2026-09 に Homebrew で無効（Gatekeeper を通らない）。TOOLS で足せば測る
PURGE="${PURGE:-1}"
TIMEOUT="${TIMEOUT:-600}"
IDLE_PCT="${IDLE_PCT:-20}"
IDLE_SEC="${IDLE_SEC:-5}"
NOISE_MAX="${NOISE_MAX:-8}"   # 静かな間に混じってよい「20% 以上・100% 未満」の回数
STEP="${STEP:-0.2}"           # 見る間隔（秒）
BUSY_PCT="${BUSY_PCT:-60}"    # これ以上なら「重く働いている」。最後にこれ以上だった時刻を秒数にする
OUT="$(cd "${DATA}/.." && pwd)/out/compare-gui-$(date +%Y%m%d-%H%M%S)"
mkdir -p "${OUT}/shots"
LOG="${OUT}/run.log"; RES="${OUT}/results.tsv"
printf '道具\t版\t比べ方\t大きさ\t文字コード\t秒\tピークメモリGiB\t結果\t画面\n' > "${RES}"
log() { echo "$*" | tee -a "${LOG}"; }

app_bin() { case "$1" in
  uwdiff)    echo /Applications/UwDiff.app/Contents/MacOS/UwDiff;;
  kdiff3)    echo /Applications/kdiff3.app/Contents/MacOS/kdiff3;;
  p4merge)   echo /Applications/p4merge.app/Contents/MacOS/p4merge;;
  diffmerge) echo /Applications/DiffMerge.app/Contents/MacOS/DiffMerge;;
esac; }
app_args() { case "$1" in   # 旧 新 を渡す形
  diffmerge) echo "--nosplash";;
  *) echo "";;
esac; }
ver() { local b; b=$(app_bin "$1"); local p="${b%/Contents/MacOS/*}/Contents/Info.plist"
  /usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$p" 2>/dev/null | tr -d '\n'
  printf ' (%s)' "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$p" 2>/dev/null)"; }

log "=== $(date '+%F %T') 画面の比較ツールとの比較（データ: ${DATA}）"
{ sw_vers; sysctl -n machdep.cpu.brand_string; echo "メモリ $(( $(sysctl -n hw.memsize) / 1073741824 ))GiB"; df -h "${DATA}" | tail -1; } 2>/dev/null | tee -a "${LOG}"
log "測り方: ${STEP} 秒ごとに CPU を見る。${IDLE_PCT}% 未満が ${IDLE_SEC} 秒続いたら終わり、その前で最後に ${BUSY_PCT}% 以上だった時刻を秒数とする。打ち切り ${TIMEOUT} 秒"
ACTIVE=""
for t in ${TOOLS}; do b=$(app_bin "$t")
  if [ -x "$b" ]; then log "  ${t}: ${b}（$(ver "${t}")）"; ACTIVE="${ACTIVE} $t"; else log "  ${t}: 無し → 飛ばす（brew install --cask で入れる）"; fi
done
KEEP=""
[ "${PURGE}" = "1" ] && { sudo -v || exit 1; ( while true; do sudo -n true; sleep 50; done ) 2>/dev/null & KEEP=$!; }
trap '[ -n "${KEEP}" ] && kill ${KEEP} 2>/dev/null; stop_all' EXIT
trap 'log "  中断 → アプリを止める"; exit 130' INT TERM
cold() { if [ "${PURGE}" = "1" ]; then sync; sudo purge; sleep 10; fi; }

# pid とその子孫の「使った CPU 時間の累計（秒）」と RSS(KB) 合計。
# macOS の ps の %cpu はなだらかに減る平均なので、すぐには 0 に近づかない → 累計の差から今の使い方を出す
tree_pids() { local p=$1; echo "$p"; for c in $(pgrep -P "$p" 2>/dev/null); do tree_pids "$c"; done; }
sample() { local pids; pids=$(tree_pids "$1" | tr '\n' ',' | sed 's/,$//')
  ps -o time=,rss= -p "${pids}" 2>/dev/null | awk '{n=split($1,a,":"); s=0; for(i=1;i<=n;i++) s=s*60+a[i]; c+=s; r+=$2} END {printf "%.3f %d\n", c, r}'; }

# 1 回測る: measure 道具 名前 旧 新   → T（秒）・M（GiB）・ST（結果）を決める
measure() { local t=$1 name=$2 old=$3 new=$4 b; b=$(app_bin "$t")
  local t0 now el quiet=0 qstart="" noise=0 lastbusy="" maxr=0 cpu rss ct prev=0 pel=0
  t0=$(perl -MTime::HiRes=time -e 'printf "%.3f", time')
  # shellcheck disable=SC2046
  # open で起動する（ふつうにダブルクリックしたのと同じ起き方で、窓が前に出る）。pid は実行ファイルの名前で探す
  open -n -a "${b%/Contents/MacOS/*}" --args $(app_args "$t") "$old" "$new" > "${OUT}/${name}.stdout" 2>&1
  local pid="" i
  for i in $(seq 1 50); do pid=$(pgrep -n -f "$b" 2>/dev/null); [ -n "$pid" ] && break; sleep 0.1; done
  [ -z "$pid" ] && { ST="起動できない"; T="-"; M=0; return; }
  ST="打ち切り"; T=">${TIMEOUT}"
  while :; do
    sleep ${STEP}
    now=$(perl -MTime::HiRes=time -e 'printf "%.3f", time'); el=$(echo "$now - $t0" | bc)
    if ! kill -0 "$pid" 2>/dev/null; then ST="途中で終了（落ちた・または開けない）"; T="-"; break; fi
    read -r ct rss <<< "$(sample "$pid")"
    [ "${rss:-0}" -gt "$maxr" ] && maxr=${rss:-0}
    # 直前の 0.5 秒で使った CPU（1 コアを 100% として）
    cpu=$(echo "scale=1; (${ct:-0} - ${prev:-0}) * 100 / ($el - ${pel:-0})" | bc); prev=${ct:-0}; pel=$el
    [ -n "${TRACE:-}" ] && log "    t=${el} cpu=${cpu}% rss=$((rss/1024))MB"
    # 「最後に重く働いていた時刻」= 比べて表示し終わった時刻の候補（静かな間の再描画の揺れは BUSY_PCT 未満なので入らない）
    { [ -z "$qstart" ] || [ "$(echo "${cpu:-0} >= 100" | bc)" = 1 ]; } && [ "$(echo "${cpu:-0} >= ${BUSY_PCT}" | bc)" = 1 ] && lastbusy=$el
    if [ "$(echo "${cpu:-0} < ${IDLE_PCT}" | bc)" = 1 ] && [ "$(echo "$el > 2" | bc)" = 1 ]; then
      [ -z "$qstart" ] && qstart=$el; quiet=$((quiet+1))
      if [ "$(echo "$el - $qstart >= ${IDLE_SEC}" | bc)" = 1 ]; then T=$(printf '%.2f' "${lastbusy:-$qstart}"); ST="表示まで"; break; fi
    elif [ -n "$qstart" ] && [ $noise -lt ${NOISE_MAX} ] && [ "$(echo "${cpu:-0} < 100" | bc)" = 1 ]; then
      noise=$((noise+1))   # 静かになった後の短い揺れ（窓の再描画など）は数えない
    else quiet=0; qstart=""; noise=0; fi
    [ "$(echo "$el > ${TIMEOUT}" | bc)" = 1 ] && break
  done
  M=$(echo "scale=2; $maxr / 1048576" | bc)
  # 測ったアプリを前に出してから撮る（起動したアプリが他の窓の裏に隠れることがあるため。初回は「オートメーション」の許可を聞かれる）
  osascript -e "tell application \"${b%/Contents/MacOS/*}\" to activate" >/dev/null 2>&1; sleep 1.5
  screencapture -x "${OUT}/shots/${name}.png" 2>/dev/null
  stop_app "$t" "$pid"
}

# 測り終えたアプリを必ず止める（子プロセスも。名前でも探して残りを止める）→ メモリが空くのを待つ
stop_app() { local t=$1 pid=${2:-} b; b=$(app_bin "$t")
  if [ -n "$pid" ]; then local all; all=$(tree_pids "$pid" | tr '\n' ' ')
    kill $all 2>/dev/null; sleep 2; kill -9 $all 2>/dev/null; fi
  pkill -9 -f "$b" 2>/dev/null
  local i; for i in 1 2 3 4 5 6 7 8 9 10; do pgrep -f "$b" >/dev/null 2>&1 || break; sleep 1; done
  pgrep -f "$b" >/dev/null 2>&1 && log "  （注意: ${t} がまだ残っている）"
  sleep 3
}
stop_all() { local t; for t in uwdiff kdiff3 p4merge diffmerge; do b=$(app_bin "$t"); [ -n "$b" ] && pkill -9 -f "$b" 2>/dev/null; done; }

stop_all   # 前の回の残りを止めてから始める
for E in ${ENCS}; do
  for t in ${ACTIVE}; do
    for S in ${SIZES}; do
      D="${DATA}/${S}"; OLD="${D}/old_${E}.csv"; NEW="${D}/new_keep_${E}.csv"
      [ -f "${OLD}" ] && [ -f "${NEW}" ] || { log "  ${S}: 試験データが無い（bench_compare_mac.sh ${S} で作る）→ 飛ばす"; continue; }
      N="${t}_text_${S}_${E}"; cold
      log "--- ${t} ${S} ${E}"
      measure "$t" "$N" "$OLD" "$NEW"
      printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$t" "$(ver "$t")" テキストの比較 "$S" "$E" "$T" "$M" "$ST" "shots/${N}.png" >> "${RES}"
      log "  ${t} ${S} ${E}: ${T}秒 ${M}GiB ${ST}"
      [ "$ST" = "表示まで" ] || { log "  → ${t} は ${E} でこれより大きい組を測らない"; break; }
    done
  done
done
log "=== $(date '+%F %T') 終わり。結果: ${RES}（画面: ${OUT}/shots/）"
column -t -s $'\t' "${RES}" | tee -a "${LOG}"
exit 0
}
