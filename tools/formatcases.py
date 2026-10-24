# > パス: tools/formatcases.py
# UwDiff 段階 3: 形式（圧縮 7 形式・Word・Excel・PDF）とフォルダ対フォルダを、小さいデータで確かめる。
#   - 圧縮の中身の比較が、展開したファイルどうしの比較と同じ差分になるか（テキスト・表）
#   - Word・PDF は取り出した文字の差分が正しいか（check_unified.py で、正解の文字の並びに当てて確かめる）
#   - Excel はシートごとにキーで照合し、正解（Python で作る）と合うか
#   - フォルダ: 旧だけ・新だけ・違う・同じの一覧、除く・含める指定、明細（.diff・.csv）
# 外部の道具: gzip・bzip2・xz・lzma・zstd・lz4・brotli（無い形式は飛ばす）
# 使い方: python3 -I tools/formatcases.py <uvd の実行ファイル> <作業フォルダ>
import csv, io, json, os, random, shutil, subprocess, sys, zipfile
from xml.sax.saxutils import escape

UVD, WORK = sys.argv[1], sys.argv[2]
SELF = os.path.dirname(os.path.abspath(__file__))
CHECK = os.path.join(SELF, 'testdata', 'check_unified.py')
shutil.rmtree(WORK, ignore_errors=True)
os.makedirs(WORK)
rnd = random.Random(20261025)
results = []

def run(*args):
    return subprocess.run([UVD, *args, '-q'], capture_output=True, text=True)

def report(name, ok, msg=''):
    print(f'{"○" if ok else "×"} {name}' + (f': {msg}' if msg else ''))
    results.append(ok)

def body(diff_path):
    """unified diff の見出し（--- / +++）を除いた中身。"""
    with open(diff_path, 'rb') as f:
        return [l for l in f if not (l.startswith(b'--- ') or l.startswith(b'+++ '))]

def check_unified(old_txt, new_txt, diff):
    p = subprocess.run([sys.executable, '-I', CHECK, old_txt, new_txt, diff], capture_output=True, text=True)
    return p.returncode == 0, p.stdout.strip()

# ---------- 圧縮 ----------
lines = [f'{i:06d} 杭 {rnd.random():.5f} ﾊﾝｶｸ ①' for i in range(20000)]
new_lines = [l + ' 改' if rnd.random() < 0.02 else l for l in lines if rnd.random() > 0.01] + ['追加の行']
def wtext(p, ls, enc='utf-8'):
    with open(p, 'wb') as f: f.write(('\n'.join(ls) + '\n').encode(enc))
wtext(f'{WORK}/old.txt', lines); wtext(f'{WORK}/new.txt', new_lines)
wtext(f'{WORK}/old_cp932.txt', lines, 'cp932'); wtext(f'{WORK}/new_cp932.txt', new_lines, 'cp932')
run(f'{WORK}/old.txt', f'{WORK}/new.txt', '-o', f'{WORK}/plain.diff')
run(f'{WORK}/old_cp932.txt', f'{WORK}/new_cp932.txt', '-o', f'{WORK}/plain_cp932.diff')
TOOLS = {'gz': ['gzip', '-kf'], 'bz2': ['bzip2', '-kf'], 'xz': ['xz', '-kf'], 'lzma': ['lzma', '-kf'], 'zst': ['zstd', '-qf'], 'lz4': ['lz4', '-qf'], 'br': ['brotli', '-f']}
def compress(src, ext):
    tool = TOOLS[ext]
    if shutil.which(tool[0]) is None: return None
    dst = f'{src}.{ext}'
    if ext == 'lz4': subprocess.run(tool + [src, dst], check=True)
    elif ext == 'br': subprocess.run(tool + ['-o', dst, src], check=True)
    else: subprocess.run(tool + [src], check=True)
    return dst
for ext in TOOLS:
    for tag, o, n, ref in (('utf8', 'old.txt', 'new.txt', 'plain.diff'), ('cp932', 'old_cp932.txt', 'new_cp932.txt', 'plain_cp932.diff')):
        co, cn = compress(f'{WORK}/{o}', ext), compress(f'{WORK}/{n}', ext)
        if co is None: report(f'圧縮 {ext} {tag}', True, '道具が無いので飛ばした'); continue
        d = f'{WORK}/{ext}_{tag}.diff'
        p = run(co, cn, '-o', d)
        ok = p.returncode == 1 and body(d) == body(f'{WORK}/{ref}')
        report(f'圧縮 {ext} {tag}', ok, '展開したものどうしと同じ差分' if ok else f'終了コード {p.returncode} {p.stderr.strip()}')
# 片方だけ圧縮
d = f'{WORK}/mixed.diff'
p = run(f'{WORK}/old.txt', f'{WORK}/new.txt.zst', '-o', d)
report('片方だけ圧縮（旧 そのまま・新 zst）', p.returncode == 1 and body(d) == body(f'{WORK}/plain.diff'))

# 圧縮した CSV の照合
hdr = ['点名', 'X', '備考']
rows = [[f'P{i:05d}', f'{rnd.uniform(0, 999):.3f}', rnd.choice(['', '杭', '"引用", 改行\nあり'])] for i in range(5000)]
nrows = [r if rnd.random() > 0.03 else [r[0], r[1] + '1', r[2]] for r in rows if rnd.random() > 0.02] + [['N1', '1.000', '追加']]
def wcsv(p, h, rs, enc='utf-8', delim=','):
    b = io.StringIO(); w = csv.writer(b, lineterminator='\n', delimiter=delim); w.writerow(h); w.writerows(rs)
    with open(p, 'wb') as f: f.write(b.getvalue().encode(enc))
wcsv(f'{WORK}/t_old.csv', hdr, rows); wcsv(f'{WORK}/t_new.csv', hdr, nrows, 'cp932')
run(f'{WORK}/t_old.csv', f'{WORK}/t_new.csv', '-key', '点名', '-o', f'{WORK}/t_plain.csv')
ref = sorted(open(f'{WORK}/t_plain.csv', encoding='utf-8').read().splitlines()[1:])
for ext in ('gz', 'xz', 'zst', 'br'):
    co, cn = compress(f'{WORK}/t_old.csv', ext), compress(f'{WORK}/t_new.csv', ext)
    if co is None: continue
    p = run(co, cn, '-key', '点名', '-o', f'{WORK}/t_{ext}.csv')
    got = sorted(open(f'{WORK}/t_{ext}.csv', encoding='utf-8').read().splitlines()[1:]) if p.returncode in (0, 1) else None
    report(f'圧縮した CSV の照合 {ext}（旧 UTF-8・新 CP932）', got == ref, f'差分 {len(ref)} 行' if got == ref else f'終了コード {p.returncode} {p.stderr.strip()}')

# ---------- Word ----------
def docx(path, paras, table=None):
    ns = 'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"'
    ps = ''.join(f'<w:p><w:r><w:t xml:space="preserve">{escape(t)}</w:t></w:r></w:p>' for t in paras)
    if table:
        ps += '<w:tbl>' + ''.join('<w:tr>' + ''.join(f'<w:tc><w:p><w:r><w:t>{escape(c)}</w:t></w:r></w:p></w:tc>' for c in r) + '</w:tr>' for r in table) + '</w:tbl>'
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('[Content_Types].xml', '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                   '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>'
                   '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
        z.writestr('_rels/.rels', '<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                   '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
        z.writestr('word/document.xml', f'<?xml version="1.0"?><w:document {ns}><w:body>{ps}</w:body></w:document>')
paras = [f'第{i}条 この仕様は {rnd.choice(["杭", "基準点", "測量"])} について定める。' for i in range(1, 400)]
paras_new = [p.replace('定める', '定めます') if rnd.random() < 0.05 else p for p in paras if rnd.random() > 0.02]
paras_new[100:100] = ['第100条の2 追加の条文。']
docx(f'{WORK}/spec_v1.docx', paras); docx(f'{WORK}/spec_v2.docx', paras_new)
wtext(f'{WORK}/spec_v1.expected.txt', paras); wtext(f'{WORK}/spec_v2.expected.txt', paras_new)
p = run(f'{WORK}/spec_v1.docx', f'{WORK}/spec_v2.docx', '-o', f'{WORK}/spec.diff')
ok, msg = check_unified(f'{WORK}/spec_v1.expected.txt', f'{WORK}/spec_v2.expected.txt', f'{WORK}/spec.diff') if p.returncode == 1 else (False, p.stderr.strip())
report('Word の本文の比較', ok, msg)
docx(f'{WORK}/same_a.docx', paras); docx(f'{WORK}/same_b.docx', paras)
p = run(f'{WORK}/same_a.docx', f'{WORK}/same_b.docx')
report('Word 同じ本文（終了コード 0）', p.returncode == 0, p.stdout.strip().splitlines()[-2] if p.stdout else p.stderr)

# ---------- Excel ----------
def col(n):
    s = ''
    while n: n, r = divmod(n - 1, 26); s = chr(65 + r) + s
    return s
def xlsx(path, sheets):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('[Content_Types].xml', '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                   '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>'
                   '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
                   + ''.join(f'<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>' for i in range(1, len(sheets) + 1))
                   + '</Types>')
        z.writestr('_rels/.rels', '<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                   '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>')
        z.writestr('xl/workbook.xml', '<?xml version="1.0"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>'
                   + ''.join(f'<sheet name="{escape(n)}" sheetId="{i}" r:id="rId{i}"/>' for i, (n, _) in enumerate(sheets, 1)) + '</sheets></workbook>')
        z.writestr('xl/_rels/workbook.xml.rels', '<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                   + ''.join(f'<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>' for i in range(1, len(sheets) + 1))
                   + '</Relationships>')
        for i, (_, rs) in enumerate(sheets, 1):
            data = ''.join(f'<row r="{ri}">' + ''.join(f'<c r="{col(ci)}{ri}" t="inlineStr"><is><t xml:space="preserve">{escape(v)}</t></is></c>' for ci, v in enumerate(r, 1) if v != '') + '</row>'
                           for ri, r in enumerate(rs, 1))
            z.writestr(f'xl/worksheets/sheet{i}.xml', f'<?xml version="1.0"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>{data}</sheetData></worksheet>')
H = ['点名', '種別', 'X', '備考']
s1 = [[f'P{i:04d}', rnd.choice(['基準点', 'BM']), f'{rnd.uniform(0, 99):.3f}', rnd.choice(['', '確認済', '0012'])] for i in range(800)]
s1n = [[r[0], r[1], r[2] + ('1' if rnd.random() < 0.05 else ''), r[3]] for r in s1 if rnd.random() > 0.03] + [['P9999', 'BM', '1.000', '追加']]
rnd.shuffle(s1n)
s2 = [[f'K{i:03d}', '杭', f'{i}.000', ''] for i in range(100)]
s2n = [r if r[0] != 'K050' else [r[0], r[1], '50.500', '移設'] for r in s2]
xlsx(f'{WORK}/ledger_09.xlsx', [('9月', [H] + s1), ('杭', [H] + s2), ('旧だけ', [H, ['A1', 'x', '1', '']])])
xlsx(f'{WORK}/ledger_10.xlsx', [('9月', [H] + s1n), ('杭', [H] + s2n)])
p = run(f'{WORK}/ledger_09.xlsx', f'{WORK}/ledger_10.xlsx', '-key', '点名', '-o', f'{WORK}/ledger.csv')
def expect(sheet, o, n):
    od = {r[0]: dict(zip(H, r)) for r in o}; nd = {r[0]: dict(zip(H, r)) for r in n}
    e = set()
    for k in od.keys() - nd.keys(): e.add(('削除', sheet, k, '', '', ''))
    for k in nd.keys() - od.keys(): e.add(('追加', sheet, k, '', '', ''))
    for k in od.keys() & nd.keys():
        for c in H:
            if od[k][c] != nd[k][c]: e.add(('変更', sheet, k, c, od[k][c], nd[k][c]))
    return e
want = expect('9月', s1, s1n) | expect('杭', s2, s2n) | expect('旧だけ', [['A1', 'x', '1', '']], [])
got = set()
if p.returncode in (0, 1):
    with open(f'{WORK}/ledger.csv', encoding='utf-8', newline='') as f:
        r = csv.reader(f); hh = next(r); got = {tuple(x) for x in r}
ok = got == want and p.returncode == 1 and hh == ['区分', 'シート', '点名', '列', '旧値', '新値']
report('Excel のシートごとの照合（3 シート・1 枚は旧だけ）', ok, f'差分 {len(got)}/{len(want)}' if p.returncode in (0, 1) else p.stderr.strip())
p = run(f'{WORK}/ledger_09.xlsx', f'{WORK}/ledger_10.xlsx', '-o', f'{WORK}/ledger.diff')
report('Excel をテキストとして比べる（-key なし）', p.returncode == 1 and os.path.getsize(f'{WORK}/ledger.diff') > 0, p.stderr.strip())

# ---------- PDF ----------
def pdf(path, pages):
    objs = []
    kids = ' '.join(f'{4 + 2 * i} 0 R' for i in range(len(pages)))
    objs.append('<< /Type /Catalog /Pages 2 0 R >>')
    objs.append(f'<< /Type /Pages /Kids [{kids}] /Count {len(pages)} >>')
    objs.append('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>')
    for i, ls in enumerate(pages):
        stream = 'BT /F1 11 Tf 14 TL 72 760 Td ' + ' '.join(f'({l}) Tj T*' for l in ls) + ' ET'
        objs.append(f'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + 2 * i} 0 R >>')
        objs.append(f'<< /Length {len(stream)} >>\nstream\n{stream}\nendstream')
    out = b'%PDF-1.4\n'
    offs = []
    for i, o in enumerate(objs, 1):
        offs.append(len(out)); out += f'{i} 0 obj\n{o}\nendobj\n'.encode('latin-1')
    x = len(out)
    out += f'xref\n0 {len(objs) + 1}\n0000000000 65535 f \n'.encode() + b''.join(f'{o:010d} 00000 n \n'.encode() for o in offs)
    out += f'trailer\n<< /Size {len(objs) + 1} /Root 1 0 R >>\nstartxref\n{x}\n%%EOF\n'.encode()
    open(path, 'wb').write(out)
pg = [[f'Section {p}.{i} survey point P{p}{i:02d} checked' for i in range(40)] for p in range(1, 4)]
pgn = [list(x) for x in pg]; pgn[1][5] = 'Section 2.5 survey point moved to P2-05b'; del pgn[2][10]; pgn[0].insert(3, 'Inserted line on page one')
pdf(f'{WORK}/report_v1.pdf', pg); pdf(f'{WORK}/report_v2.pdf', pgn)
def pdf_text(pages): return [l for i, ls in enumerate(pages, 1) for l in [f'## page {i}'] + ls]
wtext(f'{WORK}/report_v1.expected.txt', pdf_text(pg)); wtext(f'{WORK}/report_v2.expected.txt', pdf_text(pgn))
p = run(f'{WORK}/report_v1.pdf', f'{WORK}/report_v2.pdf', '-o', f'{WORK}/report.diff')
ok, msg = check_unified(f'{WORK}/report_v1.expected.txt', f'{WORK}/report_v2.expected.txt', f'{WORK}/report.diff') if p.returncode == 1 else (False, p.stderr.strip())
report('PDF のページごとの文字の比較', ok, msg)

# ---------- フォルダ ----------
A, B = f'{WORK}/dir_old', f'{WORK}/dir_new'
def put(root, rel, data):
    path = os.path.join(root, rel); os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, 'wb').write(data if isinstance(data, bytes) else data.encode('utf-8'))
for i in range(300):
    put(A, f'logs/{i // 50:02d}/app_{i:03d}.log', '\n'.join(f'{i} line {k}' for k in range(200)) + '\n')
    put(B, f'logs/{i // 50:02d}/app_{i:03d}.log', '\n'.join(f'{i} line {k}' + (' X' if i % 37 == 0 and k == 5 else '') for k in range(200)) + '\n')
put(A, 'same_size.txt', 'abcdef\n'); put(B, 'same_size.txt', 'abcxef\n')
put(A, 'only_old.txt', 'x'); put(B, 'sub/only_new.txt', 'y')
put(A, '.git/HEAD', 'ref'); put(B, '.git/HEAD', 'other')
put(A, 'skip.tmp', '1'); put(B, 'skip.tmp', '2')
put(A, 'table/points.csv', '点名,X\nP1,1\nP2,2\n'); put(B, 'table/points.csv', '点名,X\nP2,2.5\nP1,1\nP3,3\n')
p = run(A, B, '-exclude', '.git,*.tmp', '-o', f'{WORK}/folder.csv', '-json', f'{WORK}/folder.json', '-detail', f'{WORK}/detail', '-key', '点名')
want = {('違う', f'logs/{i // 50:02d}/app_{i:03d}.log') for i in range(300) if i % 37 == 0}
want |= {('違う', 'same_size.txt'), ('旧だけ', 'only_old.txt'), ('新だけ', 'sub/only_new.txt'), ('違う', 'table/points.csv')}
got = set()
if p.returncode in (0, 1):
    with open(f'{WORK}/folder.csv', encoding='utf-8', newline='') as f:
        r = csv.reader(f); next(r); allrows = list(r)
    got = {(x[0], x[1]) for x in allrows if x[0] != '同じ'}
    same = sum(1 for x in allrows if x[0] == '同じ')
j = json.load(open(f'{WORK}/folder.json', encoding='utf-8')) if p.returncode in (0, 1) else {}
ok = got == want and j.get('same') == 300 - len([i for i in range(300) if i % 37 == 0]) and p.returncode == 1
report('フォルダ（除く .git・*.tmp）', ok, f'違い {len(got)}/{len(want)}・同じ {j.get("same")}' if p.returncode in (0, 1) else p.stderr.strip())
# 明細: テキストは .diff（旧に当てると新になる）、-key のときの表は .csv
ok1, _ = check_unified(f'{A}/logs/00/app_037.log', f'{B}/logs/00/app_037.log', f'{WORK}/detail/logs/00/app_037.log.diff')
ok2 = os.path.exists(f'{WORK}/detail/table/points.csv.csv') and len(open(f'{WORK}/detail/table/points.csv.csv', encoding='utf-8').read().splitlines()) == 1 + 2
report('フォルダの明細（.diff と表の .csv）', ok1 and ok2 and j.get('detailed') == len([x for x in want if x[0] == '違う']))
p = run(A, B, '-include', '**/*.csv', '-o', f'{WORK}/folder_csv.csv')
rows_inc = list(csv.reader(open(f'{WORK}/folder_csv.csv', encoding='utf-8')))[1:] if p.returncode in (0, 1) else []
report('フォルダ（含める **/*.csv）', [(x[0], x[1]) for x in rows_inc] == [('違う', 'table/points.csv')])

# 含める・除くの書き方（uvf / uvp と同じ。含める＝ファイルの指定、除く＝.gitignore）
C, D = f'{WORK}/spec_old', f'{WORK}/spec_new'
for root in (C, D):
    for rel in ('a.csv', 'b.log', 'c.txt', 'sub/d.csv', 'sub/e.log', 'sub/deep/f.csv', 'build/g.csv', 'keep/build', 'my dir/h.csv', '.git/HEAD'):
        put(root, rel, root + rel)       # 旧と新で中身が違う（どれも「違う」になる）
def listed(*args):
    p = run(C, D, '-o', f'{WORK}/spec.csv', *args)
    if p.returncode not in (0, 1): return [f'終了コード {p.returncode} {p.stderr.strip()}']
    return sorted(x[1] for x in list(csv.reader(open(f'{WORK}/spec.csv', encoding='utf-8')))[1:] if x[0] != '同じ')
cases = [
    ('含める *.csv は直下だけ', ['-include', '*.csv'], ['a.csv']),
    ('含める **/*.csv は下の階層も', ['-include', '**/*.csv'], ['a.csv', 'build/g.csv', 'my dir/h.csv', 'sub/d.csv', 'sub/deep/f.csv']),
    ('含める {a,b}（*.{csv,log}）', ['-include', '*.{csv,log}'], ['a.csv', 'b.log']),
    ('含める 空白区切り', ['-include', '*.csv sub/*.log'], ['a.csv', 'sub/e.log']),
    ('含める カンマ区切り（空白を含む名前）', ['-include', 'my dir/*.csv,c.txt'], ['c.txt', 'my dir/h.csv']),
    ('含める ; 区切り（以前の書き方）', ['-include', '*.csv;*.txt'], ['a.csv', 'c.txt']),
    ('含める sub/** はフォルダの下すべて', ['-include', 'sub/**'], ['sub/d.csv', 'sub/deep/f.csv', 'sub/e.log']),
    ('除く 名前はどの深さにも（*.log）', ['-include', '**/*', '-exclude', '*.log,.git'], ['a.csv', 'build/g.csv', 'c.txt', 'keep/build', 'my dir/h.csv', 'sub/d.csv', 'sub/deep/f.csv']),
    ('除く 末尾 / はフォルダだけ（build/）', ['-exclude', 'build/,.git'], ['a.csv', 'b.log', 'c.txt', 'keep/build', 'my dir/h.csv', 'sub/d.csv', 'sub/deep/f.csv', 'sub/e.log']),
    ('除く 先頭 / は直下から（/sub/deep）', ['-exclude', '/sub/deep .git'], ['a.csv', 'b.log', 'build/g.csv', 'c.txt', 'keep/build', 'my dir/h.csv', 'sub/d.csv', 'sub/e.log']),
    ('除く ! で取り消し（*.csv を除くが a.csv は残す）', ['-exclude', '*.csv,!a.csv,.git'], ['a.csv', 'b.log', 'c.txt', 'keep/build', 'sub/e.log']),
]
for name, args, want in cases:
    got = listed(*args)
    report(f'フォルダの指定: {name}', got == want, '' if got == want else f'得た {got}')
ign = f'{WORK}/my.ignore'
open(ign, 'w', encoding='utf-8').write('# uvf の --ignore-file と同じ書き方\n*.log\nsub/\n.git\n')
got = listed('--ignore-file', ign)
want = ['a.csv', 'build/g.csv', 'c.txt', 'keep/build', 'my dir/h.csv']
report('フォルダの指定: --ignore-file（.gitignore の書き方のファイル）', got == want, '' if got == want else f'得た {got}')

print('すべて一致' if all(results) else '不一致あり')
sys.exit(0 if all(results) else 1)
