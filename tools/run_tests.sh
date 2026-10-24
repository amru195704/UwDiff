#!/usr/bin/env bash
# > パス: tools/run_tests.sh
{ # ← 全体を { } で囲む（走っている最中に書き換えても壊れないように）
# UwDiff の試験を自動で走らせ、合否をまとめる（Mac・Linux 共通）。
#   1) uvd を Release でビルド
#   2) 小さい試験（tools/edgecases.py）: 引用符の中の改行・キーの重複・文字コードの違い・列の並び ほか
#      … 3,000 行と、10 万行を塊 1MB で（塊の境目をまたぐ行を多くする）
#   3) 試験データ 第 2 版（tools/testdata/uwdiff_gen2.c）で照合:
#      削除・追加・修正（1〜7 セル・全列・値→空）・続いた行の塊・点名の変更・引用符の中の改行
#      × 新の行順 keep／local／shuffle × UTF-8／CP932 × 大きさ
#      - 生成器そのものが正しいかを、小さい大きさ（100MB 以下）では Python の csv（reference_diff.py）でも確かめる
#      - uvd の出力を正解と突き合わせる（区分・点名・列・旧値・新値）
#      - 待ち行の一時ファイルへの逃がし（-mem をファイルの 5% にして、行順ばらばらで照合し、正解と突き合わせる）
#      - 反映（uvd apply）: 正解の差分を全部旧に当て、新と照合し直して差分 0・文字コードと改行が旧のまま
#      - テキストの比較（段階 2）: 同じデータをテキストとして比べ、unified diff を旧に当てると新になるか（check_unified.py）
#   4) テキストの比較の危ないところ（tools/textcases.py）: patch で新に戻るか・差分が diff と同じくらい小さいか ほか
#   5) 形式とフォルダ（tools/formatcases.py）: 圧縮 7 形式・Word・Excel・PDF・フォルダ対フォルダ
#   7) 元のファイルを壊さないか（tools/safetycases.py）: 出力先が入力と同じ・リンク経由・失敗しても元のまま・CP932 の -i・改行なし
#   6) 画面（src/UwDiff.UiTests）: テキスト・表・フォルダの画面、採った変更の反映、比べた索引の一致
# 使い方: ./tools/run_tests.sh [大きさ...]          既定: 10MB 100MB
#   例: ./tools/run_tests.sh 10MB 100MB 1GB 10GB
#   DATA=…（試験データの置き場所。既定 ./out/testdata。tools/local.env に DATA=… と書いておけばそれを使う。local.env は Git に入れない）
#   ORDERS="keep local shuffle"（新の行順）  PURGE=1（各照合の前に purge。Mac のみ・sudo が要る）
# 終了コード: 全部合格なら 0、ひとつでも不合格なら 1。結果は ${DATA}/../out/uvd-tests/ の summary.tsv と run.log。
set -u
SELFDIR=$(cd "$(dirname "$0")" && pwd)
REPO=$(cd "${SELFDIR}/.." && pwd)
SIZES="${*:-10MB 100MB}"
ORDERS="${ORDERS:-keep local shuffle}"
PURGE="${PURGE:-0}"
if [ -z "${DATA:-}" ] && [ -f "${SELFDIR}/local.env" ]; then . "${SELFDIR}/local.env"; fi
DATA="${DATA:-${REPO}/out/testdata}"
mkdir -p "${DATA}"
OUT="$(cd "${DATA}/.." && pwd)/out/uvd-tests"
mkdir -p "${OUT}"
STAMP=$(date '+%Y%m%d-%H%M%S')
LOG="${OUT}/run-${STAMP}.log"
SUM="${OUT}/summary-${STAMP}.tsv"
PY=$(command -v python3 || true)
[ -n "${PY}" ] || { echo "python3 が無い"; exit 2; }
OS=$(uname -s)
FAILS=0

log() { echo "$*" | tee -a "${LOG}"; }
result() { # 試験名 合否 秒 メモリ 備考
  printf '%s\t%s\t%s\t%s\t%s\n' "$1" "$2" "$3" "$4" "$5" >> "${SUM}"
  log "$( [ "$2" = "合格" ] && echo '✅' || echo '❌' ) $1 … $2  ${3}秒  ${4}  $5"
  [ "$2" = "合格" ] || FAILS=$((FAILS + 1))
}
printf '試験\t合否\t秒\tピークメモリ\t備考\n' > "${SUM}"

log "=== $(date '+%F %T') UwDiff の試験（大きさ: ${SIZES}・行順: ${ORDERS}）"
log "データ: ${DATA}"
{ uname -a; dotnet --version; } >> "${LOG}" 2>&1

# 1) ビルド
if ! ( cd "${REPO}" && dotnet build src/Uvd.Cli/Uvd.Cli.csproj -c Release -v q -nologo ) >> "${LOG}" 2>&1; then
  result "ビルド" "不合格" "-" "-" "dotnet build に失敗（${LOG}）"
  exit 1
fi
UVD="${REPO}/src/Uvd.Cli/bin/Release/net10.0/uvd"
result "ビルド" "合格" "-" "-" "コミット $(git -C "${REPO}" --no-optional-locks rev-parse --short HEAD 2>/dev/null || echo なし)"

# 生成器
GEN="${OUT}/uwdiff_gen2"
if [ ! -x "${GEN}" ] || [ "${SELFDIR}/testdata/uwdiff_gen2.c" -nt "${GEN}" ]; then
  cc -O2 -o "${GEN}" "${SELFDIR}/testdata/uwdiff_gen2.c" -liconv 2>/dev/null || cc -O2 -o "${GEN}" "${SELFDIR}/testdata/uwdiff_gen2.c" || { result "生成器のビルド" "不合格" "-" "-" "cc に失敗"; exit 1; }
fi

timed() { # 名前 コマンド...  → 秒とピークメモリを T・M に入れる
  local NAME="$1"; shift
  local TF="${OUT}/${NAME}.time"
  if [ "${OS}" = "Darwin" ]; then
    /usr/bin/time -l "$@" > "${OUT}/${NAME}.txt" 2> "${TF}"; RC=$?
    T=$(grep -E ' real' "${TF}" | awk '{print $1}')
    M=$(grep -E 'maximum resident' "${TF}" | awk '{printf "%.2fGiB", $1/1073741824}')
  else
    if [ -x /usr/bin/time ]; then
      /usr/bin/time -f '%e %M' "$@" > "${OUT}/${NAME}.txt" 2> "${TF}"; RC=$?
      T=$(tail -1 "${TF}" | awk '{print $1}'); M=$(tail -1 "${TF}" | awk '{printf "%.2fGiB", $2/1048576}')
    else
      local S0 S1
      S0=$(date +%s.%N); "$@" > "${OUT}/${NAME}.txt" 2> "${TF}"; RC=$?; S1=$(date +%s.%N)
      T=$(echo "${S1} ${S0}" | awk '{printf "%.2f", $1-$2}'); M="-"
    fi
  fi
}

# 2) 小さい試験
timed "edge_3000" "${PY}" -I "${SELFDIR}/edgecases.py" "${UVD}" "${OUT}/edge_3000" 3000
result "危ないところ（3,000 行）" "$( [ ${RC} -eq 0 ] && echo 合格 || echo 不合格 )" "${T}" "-" "$(grep -c '^○' "${OUT}/edge_3000.txt") 件合格・$(grep -c '^×' "${OUT}/edge_3000.txt") 件不合格"
timed "edge_100k_block1" "${PY}" -I "${SELFDIR}/edgecases.py" "${UVD}" "${OUT}/edge_100k" 100000 -block 1
result "危ないところ（10 万行・塊 1MB）" "$( [ ${RC} -eq 0 ] && echo 合格 || echo 不合格 )" "${T}" "-" "$(grep -c '^○' "${OUT}/edge_100k_block1.txt") 件合格・$(grep -c '^×' "${OUT}/edge_100k_block1.txt") 件不合格"

timed "text_5000" "${PY}" -I "${SELFDIR}/textcases.py" "${UVD}" "${OUT}/text_5000" 5000
result "テキストの危ないところ（5,000 行）" "$( [ ${RC} -eq 0 ] && echo 合格 || echo 不合格 )" "${T}" "-" "$(grep -c '^○' "${OUT}/text_5000.txt") 件合格・$(grep -c '^×' "${OUT}/text_5000.txt") 件不合格"

timed "formats" "${PY}" -I "${SELFDIR}/formatcases.py" "${UVD}" "${OUT}/formats"
result "形式とフォルダ（圧縮 7 形式・Word・Excel・PDF・フォルダ）" "$( [ ${RC} -eq 0 ] && echo 合格 || echo 不合格 )" "${T}" "-" "$(grep -c '^○' "${OUT}/formats.txt") 件合格・$(grep -c '^×' "${OUT}/formats.txt") 件不合格"

timed "safety" "${PY}" -I "${SELFDIR}/safetycases.py" "${UVD}" "${OUT}/safety"
result "元のファイルを壊さないか（出力先・リンク・CP932 の -i・改行なし）" "$( [ ${RC} -eq 0 ] && echo 合格 || echo 不合格 )" "${T}" "-" "$(grep -c '^○' "${OUT}/safety.txt") 件合格・$(grep -c '^×' "${OUT}/safety.txt") 件不合格"

# 画面（段階 4）: Avalonia Headless で本物の窓を組み立てて操作する（写しは out/uvd-tests/shots）
if UVD_SHOTS="${OUT}/shots" dotnet test "${REPO}/src/UwDiff.UiTests" -c Release -v q -nologo > "${OUT}/ui.txt" 2>&1; then UIR=合格; else UIR=不合格; fi
result "画面（UwDiff.UiTests）" "${UIR}" "-" "-" "$(grep -E '合格:|Passed' "${OUT}/ui.txt" | tail -1 | sed -E 's/^ *//')"

# 3) 試験データ 第 2 版
bytes_of() { case "$1" in *MB) echo $(( ${1%MB} * 1000000 ));; *GB) echo $(( ${1%GB} * 1000000000 ));; *) echo "$1";; esac; }
for S in ${SIZES}; do
  D="${DATA}/${S}"
  mkdir -p "${D}"
  N=$("${GEN}" count "$(bytes_of "${S}")")
  if [ ! -f "${D}/answer.csv" ] || [ "$(cat "${D}/rows.txt" 2>/dev/null)" != "${N}" ] || [ "${GEN}" -nt "${D}/answer.csv" ]; then
    log "--- ${S}: 試験データを作る（旧 ${N} 行）"
    for E in utf8 cp932; do
      "${GEN}" old "${N}" "${E}" "${D}/old_${E}.csv.part" && mv "${D}/old_${E}.csv.part" "${D}/old_${E}.csv"
      for O in ${ORDERS}; do "${GEN}" new "${N}" "${E}" "${O}" "${D}/new_${O}_${E}.csv.part" && mv "${D}/new_${O}_${E}.csv.part" "${D}/new_${O}_${E}.csv"; done
    done
    "${GEN}" answer "${N}" "${D}/answer.csv"
    "${GEN}" stats "${N}" > "${D}/stats.txt"
    echo "${N}" > "${D}/rows.txt"
  else
    for E in utf8 cp932; do for O in ${ORDERS}; do
      [ -f "${D}/new_${O}_${E}.csv" ] || "${GEN}" new "${N}" "${E}" "${O}" "${D}/new_${O}_${E}.csv"
    done; done
  fi
  sed 's/^/    /' "${D}/stats.txt" | tee -a "${LOG}"

  # 生成器の確かめ（100MB 以下）
  case "${S}" in *GB) ;; *)
    for E in utf8 cp932; do
      PE=utf-8; [ "${E}" = "cp932" ] && PE=cp932
      O1=$(echo "${ORDERS}" | awk '{print $1}')
      if "${PY}" -I "${SELFDIR}/testdata/reference_diff.py" "${D}/old_${E}.csv" "${D}/new_${O1}_${E}.csv" "${D}/answer.csv" "${PE}" >> "${LOG}" 2>&1; then
        result "生成器の正解 ${S} ${E}" "合格" "-" "-" "Python の csv で照合して正解と一致"
      else
        result "生成器の正解 ${S} ${E}" "不合格" "-" "-" "生成器の正解が Python の照合と合わない"
      fi
    done
  ;; esac

  for E in utf8 cp932; do
    for O in ${ORDERS}; do
      NAME="diff_${S}_${O}_${E}"
      if [ "${PURGE}" = "1" ] && [ "${OS}" = "Darwin" ]; then sync; sudo purge; sleep 10; fi
      timed "${NAME}" "${UVD}" "${D}/old_${E}.csv" "${D}/new_${O}_${E}.csv" -key 点名 -q -o "${OUT}/${NAME}.csv" -json "${OUT}/${NAME}.json"
      if [ ${RC} -gt 1 ]; then
        result "照合 ${S} ${O} ${E}" "不合格" "${T}" "${M}" "uvd の終了コード ${RC}"
        continue
      fi
      CHK=$("${PY}" -I "${SELFDIR}/testdata/check_answer.py" "${OUT}/${NAME}.csv" "${D}/answer.csv" 2>&1); CRC=$?
      PEND=$(grep -E '^待ち行' "${OUT}/${NAME}.txt" | sed 's/.*最大: //; s/／.*//')
      result "照合 ${S} ${O} ${E}" "$( [ ${CRC} -eq 0 ] && echo 合格 || echo 不合格 )" "${T}" "${M}" "${CHK}／待ち行 ${PEND}"
    done
  done

  # テキストの比較（段階 2）: 同じ試験データをテキストとして比べ、unified diff を旧に当てると新になるかを確かめる（行順 keep・local）
  for E in utf8 cp932; do
    for O in ${ORDERS}; do
      [ "${O}" = "shuffle" ] && continue
      NAME="text_${S}_${O}_${E}"
      timed "${NAME}" "${UVD}" "${D}/old_${E}.csv" "${D}/new_${O}_${E}.csv" -q -o "${OUT}/${NAME}.diff" -json "${OUT}/${NAME}.json"
      if [ ${RC} -gt 1 ]; then result "テキスト ${S} ${O} ${E}" "不合格" "${T}" "${M}" "uvd の終了コード ${RC}"; continue; fi
      CHK=$("${PY}" -I "${SELFDIR}/testdata/check_unified.py" "${D}/old_${E}.csv" "${D}/new_${O}_${E}.csv" "${OUT}/${NAME}.diff" 2>&1); CRC=$?
      SUMM=$("${PY}" -I -c 'import json,sys; j=json.load(open(sys.argv[1])); print("目印の範囲の最大 %d 行・置き換え %d か所" % (j["peakWindowLines"], j["fallbacks"]))' "${OUT}/${NAME}.json")
      result "テキスト ${S} ${O} ${E}" "$( [ ${CRC} -eq 0 ] && echo 合格 || echo 不合格 )" "${T}" "${M}" "${CHK}／${SUMM}"
      [ ${CRC} -eq 0 ] && rm -f "${OUT}/${NAME}.diff"
    done
  done

  # 待ち行の一時ファイルへの逃がし: メモリの上限をわざと小さく（ファイルの 5%・16MB 以上）して、行順ばらばらで照合
  OL=$(echo "${ORDERS}" | awk '{print $NF}')
  MEMB=$(( $(bytes_of "${S}") / 20 )); [ ${MEMB} -lt 16777216 ] && MEMB=16777216
  for E in utf8 cp932; do
    NAME="spill_${S}_${OL}_${E}"
    timed "${NAME}" "${UVD}" "${D}/old_${E}.csv" "${D}/new_${OL}_${E}.csv" -key 点名 -q -mem "${MEMB}" -o "${OUT}/${NAME}.csv" -json "${OUT}/${NAME}.json"
    if [ ${RC} -gt 1 ]; then result "逃がし ${S} ${OL} ${E}" "不合格" "${T}" "${M}" "uvd の終了コード ${RC}"; continue; fi
    CHK=$("${PY}" -I "${SELFDIR}/testdata/check_answer.py" "${OUT}/${NAME}.csv" "${D}/answer.csv" 2>&1); CRC=$?
    SP=$(grep -E '^一時ファイルへ逃がした区画' "${OUT}/${NAME}.txt" | sed 's/^一時ファイルへ逃がした区画: //')
    [ -n "${SP}" ] || { CRC=1; SP="逃がしていない"; }
    result "逃がし ${S} ${OL} ${E}（上限 $((MEMB / 1048576))MB）" "$( [ ${CRC} -eq 0 ] && echo 合格 || echo 不合格 )" "${T}" "${M}" "${CHK}／逃がした区画 ${SP}"
  done

  # 反映: 正解の差分を全部旧に当てると、新と同じ中身になる（uvd で照合し直して差分 0）。文字コード・改行も旧のまま
  O1=$(echo "${ORDERS}" | awk '{print $1}')
  for E in utf8 cp932; do
    NAME="apply_${S}_${E}"
    timed "${NAME}" "${UVD}" apply "${D}/old_${E}.csv" "${D}/new_${O1}_${E}.csv" "${D}/answer.csv" -o "${OUT}/${NAME}.csv" -q
    if [ ${RC} -ne 0 ]; then result "反映 ${S} ${E}" "不合格" "${T}" "${M}" "uvd apply の終了コード ${RC}"; continue; fi
    AT="${T}"; AM="${M}"
    timed "${NAME}_re" "${UVD}" "${OUT}/${NAME}.csv" "${D}/new_${O1}_${E}.csv" -key 点名 -q -json "${OUT}/${NAME}_re.json"
    RE=$("${PY}" -I -c 'import json,sys; j=json.load(open(sys.argv[1])); print(j["added"]+j["deleted"]+j["changedCells"], j["oldEncoding"], j["oldRows"], j["newRows"])' "${OUT}/${NAME}_re.json")
    WANT_ENC=UTF-8; [ "${E}" = "cp932" ] && WANT_ENC=CP932
    CR=$(head -c 300 "${OUT}/${NAME}.csv" | tr -cd '\r' | wc -c | tr -d ' ')
    WANT_CR=0; [ "${E}" = "cp932" ] && WANT_CR=1
    set -- ${RE}
    HAS_CR=0; [ "${CR}" -ge 1 ] && HAS_CR=1
    OK=合格
    [ "$1" = "0" ] || OK=不合格
    [ "$2" = "${WANT_ENC}" ] || OK=不合格
    [ "${HAS_CR}" = "${WANT_CR}" ] || OK=不合格
    result "反映 ${S} ${E}" "${OK}" "${AT}" "${AM}" "照合し直した差分 $1・文字コード $2・行 $3/$4・CRLF $( [ ${HAS_CR} = 1 ] && echo あり || echo なし )"
  done
done

log "=== 結果: $( [ ${FAILS} -eq 0 ] && echo '全部合格' || echo "不合格 ${FAILS} 件" )（${SUM}）"
exit $( [ ${FAILS} -eq 0 ] && echo 0 || echo 1 )
}
