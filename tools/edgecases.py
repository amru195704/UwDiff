# > パス: tools/edgecases.py
# UwDiff: 照合エンジンと反映（uvd apply）の「危ないところ」を小さいデータで確かめる。
#   段階 0: 引用符の中の改行・キーの重複・旧と新で文字コードが違う・列の並びが違う・BOM・CRLF と LF の混在・引用符つきのキー。
#   段階 1: EUC-JP・UTF-16（BOM あり・なし、LE・BE）と旧新の文字コード違い・TSV（引用符あり・なし）と CSV 対 TSV・
#           複数列のキー・旧だけ／新だけの列・待ち行の一時ファイルへの逃がし（-mem を小さく）・
#           反映（採る差分だけを旧に当てる。文字コード・BOM・改行・区切り・行の順番を保つか）。
# 使い方: python3 -I tools/edgecases.py <uvd の実行ファイル> <作業フォルダ> [行数=3000] [uvd に足すオプション...]
#   例: ... 300000 -block 1   （塊を 1MB にして、塊の境目をまたぐ行・引用符の中の改行を多く作る）
# 正解は Python の csv で作る（キーが重複している値は、組み合わせ方が決まらないので比べる対象から外し、重複の数だけ確かめる）。
import csv, io, json, os, random, subprocess, sys

UVD, WORK = sys.argv[1], sys.argv[2]
NROWS = int(sys.argv[3]) if len(sys.argv) > 3 else 3000
EXTRA = sys.argv[4:]
os.makedirs(WORK, exist_ok=True)
random.seed(20261023)

HEADER = ['点名', 'X', 'Y', '種別', '備考']
REMARKS = ['', '杭No.1-2 確認済', '①～③ 区画', '髙橋氏立会', 'ソ表能予 補点', 'ﾊﾝｶｸ', '既設、"移設"予定, 要確認',
           '1行目\n2行目（引用符の中の改行）', 'CRLF\r\nの改行', '末尾の空白 ', '値に\tタブ']
# EUC-JP で書ける文字だけ（髙＝IBM 拡張・～ は EUC-JP の表によって対応が違うので外す）
REMARKS_EUC = ['', '杭No.1-2 確認済', '①③ 区画', 'ソ表能予 補点', 'ﾊﾝｶｸ', '既設、"移設"予定, 要確認',
               '1行目\n2行目（引用符の中の改行）', '末尾の空白 ', '〒 ≒']
# 引用符を使わない TSV 用（タブ・改行を含まない。途中の " は普通の文字）
REMARKS_PLAIN = ['', '杭No.1-2 確認済', '5" 管', 'ソ表能予 補点', 'ﾊﾝｶｸ', '"先頭も引用符', '末尾の空白 ']
ENC = {'utf8': 'utf-8', 'cp932': 'cp932', 'eucjp': 'euc_jis_2004', 'utf16le': 'utf-16-le', 'utf16be': 'utf-16-be'}
BOMS = {'utf8': b'\xef\xbb\xbf', 'utf16le': b'\xff\xfe', 'utf16be': b'\xfe\xff'}

def make_rows(n, remarks=REMARKS):
    rows = []
    for i in range(n):
        key = f'{i:06d}' if i % 3 else f'基準{i}'
        rows.append([key, f'{random.uniform(-9999, 9999):.3f}', f'{random.uniform(-9999, 9999):.3f}',
                     random.choice(['基準点', '図根点', 'BM']), random.choice(remarks)])
    return rows

def mutate(rows, add_remark='追加\n改行入り', change_kind=True):
    new = []
    for r in rows:
        x = random.random()
        if x < 0.03:
            continue                      # 削除
        r = list(r)
        if x < 0.07:
            c = random.randrange(1, len(r))
            if c == 3 and not change_kind: c = 1
            r[c] = r[c] + '（修正）' if c == 4 else r[c] + '1'
        new.append(r)
    for i in range(len(rows), len(rows) + len(rows) // 30):
        new.append([f'追{i}', '1.000', '2.000', 'BM', add_remark])
    random.shuffle(new)
    return new

def write(path, header, rows, enc, newline='\n', bom=False, quote_all_keys=False, delim=',', plain=False):
    buf = io.StringIO()
    if plain:
        for r in [header] + rows:
            buf.write(delim.join(r) + newline)
    else:
        w = csv.writer(buf, lineterminator=newline, quoting=csv.QUOTE_MINIMAL, delimiter=delim)
        w.writerow(header)
        for r in rows:
            if quote_all_keys:
                buf.write('"' + r[0].replace('"', '""') + '"' + delim)
                w2 = csv.writer(buf, lineterminator=newline, delimiter=delim)
                w2.writerow(r[1:])
            else:
                w.writerow(r)
    data = buf.getvalue().encode(ENC[enc])
    if bom:
        data = BOMS[enc] + data
    with open(path, 'wb') as f:
        f.write(data)

def index(h, rows, keys):
    d, dup = {}, set()
    for r in rows:
        k = tuple(r[h.index(c)] for c in keys)
        if k in d:
            dup.add(k)
        d[k] = dict(zip(h, r))
    return d, dup

def expected(oh, orows, nh, nrows, keys):
    """(区分, キー…, 列, 旧値, 新値) の集合（重複キーは除く）と、重複したキーの数。旧だけ・新だけの列は相手を空として比べる。"""
    od, odup = index(oh, orows, keys)
    nd, ndup = index(nh, nrows, keys)
    skip = odup | ndup
    cols = oh + [c for c in nh if c not in oh]
    exp = set()
    for k in od.keys() - nd.keys():
        if k not in skip: exp.add(('削除',) + k + ('', '', ''))
    for k in nd.keys() - od.keys():
        if k not in skip: exp.add(('追加',) + k + ('', '', ''))
    for k in od.keys() & nd.keys():
        if k in skip: continue
        for c in cols:
            ov, nv = od[k].get(c, ''), nd[k].get(c, '')
            if ov != nv: exp.add(('変更',) + k + (c, ov, nv))
    return exp, len(odup), len(ndup), skip

def read_rows(path, enc, delim, plain=False):
    data = open(path, 'rb').read()
    for b in BOMS.values():
        if data.startswith(b) and enc in BOMS and BOMS[enc] == b:
            data = data[len(b):]
    text = data.decode(ENC[enc])
    if plain:
        lines = text.split('\n')
        if lines and lines[-1] == '': lines.pop()
        rows = [l.split(delim) for l in lines]
    else:
        rows = list(csv.reader(io.StringIO(text, newline=''), delimiter=delim))
    return rows[0], rows[1:]

def check_apply(name, op, np_, diff, oh, orows, nh, nrows, keys, oenc, odelim, plain, newline, bom, args):
    """差分の一部（約 6 割の行）を採って反映し、Python で作った正解と行の順番まで比べる。全部を採ったときは新と同じ中身になるかも見る。"""
    with open(diff, encoding='utf-8', newline='') as f:
        r = csv.reader(f); dh = next(r); drows = list(r)
    nk = len(keys)
    ok_all = True
    for label, frac in (('一部', 0.6), ('全部', 1.0)):
        rnd = random.Random(7)
        sel = [x for x in drows if rnd.random() < frac]
        sp = os.path.join(WORK, f'{name}_sel_{frac}.csv')
        with open(sp, 'w', encoding='utf-8', newline='') as f:
            w = csv.writer(f, lineterminator='\n'); w.writerow(dh); w.writerows(sel)
        out = os.path.join(WORK, f'{name}_applied_{frac}' + os.path.splitext(op)[1])
        p = subprocess.run([UVD, 'apply', op, np_, sp, '-o', out, '-q'] + args, capture_output=True, text=True)
        # 終了コード 1 は「反映しなかったものがある」。旧に無い列（新だけの列）への変更だけなら正しい動き
        only_unknown_cols = p.returncode == 1 and '旧値が違う 0・文字コードで書けない 0・キーが見つからない 0・' in p.stdout
        if p.returncode != 0 and not only_unknown_cols:
            print(f'× {name} 反映（{label}）: 終了コード {p.returncode} {p.stdout.strip()} {p.stderr.strip()}')
            return False
        # 正解: 旧の行の順番のまま、削除を抜き、変更を当て、追加を新の順番で末尾に（列は旧の並び）
        dels = {tuple(x[1:1 + nk]) for x in sel if x[0] == '削除'}
        adds = {tuple(x[1:1 + nk]) for x in sel if x[0] == '追加'}
        chg = {}
        for x in sel:
            if x[0] == '変更' and x[1 + nk] in oh:
                chg.setdefault(tuple(x[1:1 + nk]), {})[x[1 + nk]] = x[3 + nk]
        want = []
        for row in orows:
            k = tuple(row[oh.index(c)] for c in keys)
            if k in dels: continue
            row = list(row)
            for c, v in chg.get(k, {}).items(): row[oh.index(c)] = v
            want.append(row)
        for row in nrows:
            k = tuple(row[nh.index(c)] for c in keys)
            if k in adds: want.append([row[nh.index(c)] if c in nh else '' for c in oh])
        gh, got = read_rows(out, oenc, odelim, plain)
        raw = open(out, 'rb').read()
        has_bom = oenc in BOMS and raw.startswith(BOMS[oenc])
        first_nl = raw.decode(ENC[oenc]).split('\n', 1)[0].endswith('\r')
        ok = gh == oh and got == want and has_bom == bom and first_nl == (newline == '\r\n')
        if frac == 1.0 and set(nh) == set(oh):
            # 全部を採れば、中身（キー → 行）は新と同じになる
            gd, _ = index(gh, got, keys); nd, _ = index(nh, nrows, keys)
            ok = ok and gd == nd
        print(f'{"○" if ok else "×"} {name} 反映（{label}）: 行 {len(got)}/{len(want)}・BOM {has_bom}・CRLF {first_nl}')
        if not ok:
            for i, (a, b) in enumerate(zip(got, want)):
                if a != b: print('   最初の違い', i, a, b); break
        ok_all = ok_all and ok
    return ok_all

def run_case(name, oh, orows, nh, nrows, oenc, nenc, keys=('点名',), args=(), apply=False, **kw):
    ko, kn = kw.get('old', {}), kw.get('new', {})
    oext = '.tsv' if ko.get('delim') == '\t' else '.csv'
    next_ = '.tsv' if kn.get('delim') == '\t' else '.csv'
    op, np_ = os.path.join(WORK, f'{name}_old{oext}'), os.path.join(WORK, f'{name}_new{next_}')
    write(op, oh, orows, oenc, **ko)
    write(np_, nh, nrows, nenc, **kn)
    out = os.path.join(WORK, f'{name}_diff.csv')
    js = os.path.join(WORK, f'{name}.json')
    keyargs = ['-key', ','.join(keys)]
    p = subprocess.run([UVD, op, np_, *keyargs, '-o', out, '-json', js, '-q', *args] + EXTRA, capture_output=True, text=True)
    if p.returncode != 0:
        print(f'× {name}: 終了コード {p.returncode} {p.stderr.strip()}')
        return False
    exp, od, nd, skip = expected(oh, orows, nh, nrows, list(keys))
    nk = len(keys)
    with open(out, encoding='utf-8', newline='') as f:
        r = csv.reader(f); hdr = next(r)
        got = {tuple(x) for x in r if tuple(x[1:1 + nk]) not in skip}
    j = json.load(open(js, encoding='utf-8'))
    ok = got == exp and j['oldDupKeys'] == od and j['newDupKeys'] == nd and hdr == ['区分', *keys, '列', '旧値', '新値']
    if 'expect_enc' in kw:
        ok = ok and (j['oldEncoding'], j['newEncoding']) == kw['expect_enc']
    if 'expect_spill' in kw:
        ok = ok and j['spilledShards'] > 0
    print(f'{"○" if ok else "×"} {name}: 差分 {len(got)}/{len(exp)}・重複 旧{j["oldDupKeys"]}/{od} 新{j["newDupKeys"]}/{nd}'
          f'・文字コード {j["oldEncoding"]}/{j["newEncoding"]}{"（変換あり）" if j["transcoded"] else ""}'
          f'・区切り {j["oldDelimiter"]}/{j["newDelimiter"]}'
          f'{"・逃がした区画 " + str(j["spilledShards"]) if j["spilledShards"] else ""}')
    if not ok:
        for x in list(exp - got)[:3]: print('   不足', x)
        for x in list(got - exp)[:3]: print('   余分', x)
    if ok and apply:
        ok = check_apply(name, op, np_, out, oh, orows, nh, nrows, list(keys), oenc, ko.get('delim', ','), ko.get('plain', False),
                         ko.get('newline', '\n'), ko.get('bom', False), list(args))
    return ok

base = make_rows(NROWS)
new = mutate(base)
results = []
# --- 段階 0 から ---
results.append(run_case('quoted_newline_utf8', HEADER, base, HEADER, new, 'utf8', 'utf8', apply=True))
results.append(run_case('quoted_newline_cp932', HEADER, base, HEADER, new, 'cp932', 'cp932'))
results.append(run_case('mixed_old_cp932_new_utf8', HEADER, base, HEADER, new, 'cp932', 'utf8', apply=True, old={'newline': '\r\n'}))
results.append(run_case('mixed_old_utf8bom_new_cp932', HEADER, base, HEADER, new, 'utf8', 'cp932', apply=True, old={'bom': True}))
results.append(run_case('crlf_vs_lf', HEADER, base, HEADER, new, 'utf8', 'utf8', old={'newline': '\r\n'}))
results.append(run_case('quoted_keys_one_side', HEADER, base, HEADER, new, 'utf8', 'utf8', new={'quote_all_keys': True}))
# 列の並びが違う（新は X と Y を入れ替え、種別を最後へ）
perm = [0, 2, 1, 4, 3]
nh = [HEADER[i] for i in perm]
results.append(run_case('column_order', HEADER, base, nh, [[r[i] for i in perm] for r in new], 'utf8', 'utf8', apply=True))
# キーが新では 3 列目にある
perm2 = [1, 2, 0, 3, 4]
nh2 = [HEADER[i] for i in perm2]
results.append(run_case('key_column_moved', HEADER, base, nh2, [[r[i] for i in perm2] for r in new], 'cp932', 'cp932'))
# キーの重複（旧に 5 個・新に 3 個）
dup_old = base + [list(base[i]) for i in (10, 20, 30, 40, 50)]
dup_new = new + [list(new[i]) for i in (1, 2, 3)]
random.shuffle(dup_old)
results.append(run_case('duplicate_keys', HEADER, dup_old, HEADER, dup_new, 'utf8', 'utf8'))
# 片方が空（見出しだけ）
results.append(run_case('empty_new', HEADER, base[:100], HEADER, [], 'utf8', 'utf8', apply=True))

# --- 段階 1: 文字コード ---
base_e = make_rows(NROWS, REMARKS_EUC)
new_e = mutate(base_e)
results.append(run_case('eucjp_both', HEADER, base_e, HEADER, new_e, 'eucjp', 'eucjp', expect_enc=('EUC-JP', 'EUC-JP'), apply=True))
results.append(run_case('mixed_old_eucjp_new_cp932', HEADER, base_e, HEADER, new_e, 'eucjp', 'cp932', expect_enc=('EUC-JP', 'CP932'), apply=True))
results.append(run_case('utf16le_bom_both', HEADER, base, HEADER, new, 'utf16le', 'utf16le', expect_enc=('UTF-16LE', 'UTF-16LE'), apply=True,
                        old={'bom': True, 'newline': '\r\n'}, new={'bom': True}))
results.append(run_case('utf16be_bom_vs_utf8', HEADER, base, HEADER, new, 'utf16be', 'utf8', expect_enc=('UTF-16BE', 'UTF-8'), apply=True, old={'bom': True}))
results.append(run_case('utf16le_nobom_vs_eucjp', HEADER, base_e, HEADER, new_e, 'utf16le', 'eucjp', expect_enc=('UTF-16LE', 'EUC-JP'), apply=True))
results.append(run_case('mixed_old_utf8_new_utf16le', HEADER, base, HEADER, new, 'utf8', 'utf16le', expect_enc=('UTF-8', 'UTF-16LE'), new={'bom': True}))

# --- 段階 1: 区切り ---
results.append(run_case('tsv_both', HEADER, base, HEADER, new, 'utf8', 'utf8', apply=True, old={'delim': '\t'}, new={'delim': '\t'}))
results.append(run_case('csv_old_tsv_new_cp932', HEADER, base, HEADER, new, 'cp932', 'cp932', apply=True, new={'delim': '\t'}))
base_p = make_rows(NROWS, REMARKS_PLAIN)
new_p = mutate(base_p, add_remark='追加 5" 管')
results.append(run_case('tsv_quote_off', HEADER, base_p, HEADER, new_p, 'utf8', 'utf8', args=['-quote', 'off'], apply=True,
                        old={'delim': '\t', 'plain': True}, new={'delim': '\t', 'plain': True}))

# --- 段階 1: キーと列 ---
# 複数列のキー（点名＋種別）: 点名が同じで種別が違う行は別の行。種別の修正は削除＋追加になる
mk_old = base + [[r[0], r[1], r[2], '多角点', r[4]] for r in base[5:50]]
mk_new = new + [[r[0], r[1], r[2], '多角点', r[4] + '（多）'] for r in base[5:30]]
random.shuffle(mk_old)
results.append(run_case('multi_key', HEADER, mk_old, HEADER, mk_new, 'utf8', 'utf8', keys=('点名', '種別'), apply=True))
results.append(run_case('multi_key_cp932_moved', HEADER, mk_old, nh2, [[r[i] for i in perm2] for r in mk_new], 'cp932', 'cp932', keys=('種別', '点名')))
# 旧だけの列（備考）・新だけの列（標高）
nh3 = ['点名', 'X', 'Y', '種別', '標高']
new3 = [r[:4] + [random.choice(['', '12.345', '-0.500'])] for r in new]
results.append(run_case('old_only_new_only_columns', HEADER, base, nh3, new3, 'utf8', 'utf8', apply=True))

# --- 段階 1: メモリの上限を超えたら一時ファイルへ（-mem 64KB で必ず逃がす。区画を少なくして大きくする） ---
results.append(run_case('spill_utf8', HEADER, base, HEADER, new, 'utf8', 'utf8', args=['-mem', '64KB', '-shards', '4'], expect_spill=True))
results.append(run_case('spill_multi_key_dup', HEADER, mk_old + [list(base[7])], HEADER, mk_new, 'cp932', 'utf8', keys=('点名', '種別'),
                        args=['-mem', '64KB', '-shards', '3', '-workers', '2'], expect_spill=True))

print('すべて一致' if all(results) else '不一致あり')
sys.exit(0 if all(results) else 1)
