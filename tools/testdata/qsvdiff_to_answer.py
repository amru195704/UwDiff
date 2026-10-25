# > パス: tools/testdata/qsvdiff_to_answer.py
# qsv diff の出力（先頭の列 diffresult が - か +・残りは行の全列）を、正解と同じ「区分,点名,列,旧値,新値」に直す。
# 使い方: python3 qsvdiff_to_answer.py qsv の出力.csv 文字コード(utf-8|cp932) 出力.csv
#   キーは 2 列目（点名）。- だけ → 削除、+ だけ → 追加、両方 → 列ごとに違う所を 変更 として出す。
import csv, sys

src, enc, dst = sys.argv[1], sys.argv[2], sys.argv[3]
old, new = {}, {}
with open(src, encoding=enc, newline='') as f:
    r = csv.reader(f)
    head = next(r)
    cols = head[1:]
    for row in r:
        if not row:
            continue
        (old if row[0] == '-' else new)[row[1]] = row[1:]
with open(dst, 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f, lineterminator='\n')
    w.writerow(['区分', '点名', '列', '旧値', '新値'])
    for k, o in old.items():
        n = new.get(k)
        if n is None:
            w.writerow(['削除', k, '', '', ''])
            continue
        for c, a, b in zip(cols[1:], o[1:], n[1:]):
            if a != b:
                w.writerow(['変更', k, c, a, b])
    for k in new:
        if k not in old:
            w.writerow(['追加', k, '', '', ''])
