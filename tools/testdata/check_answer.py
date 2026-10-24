# > パス: tools/testdata/check_answer.py
# 「区分,点名,列,旧値,新値」形式の差分（uvd の出力）を正解（answer.csv）と突き合わせる。
# 使い方: python3 check_answer.py 差分.csv 正解.csv
# 大きいファイルでもメモリを抑えるため、行ごとのハッシュ値の集合で比べる。
import csv, sys, hashlib

def keys(path):
    s = set()
    with open(path, encoding='utf-8', newline='') as f:
        r = csv.reader(f); next(r)
        for row in r:
            s.add(hashlib.blake2b('\x1f'.join(row).encode(), digest_size=12).digest())
    return s

got, want = keys(sys.argv[1]), keys(sys.argv[2])
miss, extra = len(want - got), len(got - want)
print(f'出力 {len(got)} 行 / 正解 {len(want)} 行 / 不足 {miss} 余分 {extra} → {"一致" if miss == 0 and extra == 0 else "不一致"}')
sys.exit(0 if miss == 0 and extra == 0 else 1)
