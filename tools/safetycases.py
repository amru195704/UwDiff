# > パス: tools/safetycases.py
# UwDiff: 元のファイルを壊さないか・レビューで見つかった不具合が戻っていないかを確かめる。
#   - 書き出し先（-o・-json・apply の -o）が入力と同じファイルなら断り、入力は元のまま（名前どおり・シンボリックリンク経由・ハードリンク）
#   - 書き出しに失敗しても、書き出し先の元の中身は残る（一時ファイルに書いてから入れ替える）。一時ファイルも残らない
#   - CP932 のまま -i で比べても、2 バイト文字の 2 バイト目を変えない（「、」0x8141 と「∥」0x8161 は違う）
#   - 最後の同じ行に改行が無いとき、文脈の行にも「改行なし」を付ける（patch で新に戻る）
# 使い方: python3 -I tools/safetycases.py <uvd の実行ファイル> <作業フォルダ>
import os, shutil, subprocess, sys

UVD, WORK = sys.argv[1], sys.argv[2]
CHECK = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'testdata', 'check_unified.py')
shutil.rmtree(WORK, ignore_errors=True)
os.makedirs(WORK)
results = []

def p(name): return os.path.join(WORK, name)
def write(name, data): open(p(name), 'wb').write(data if isinstance(data, bytes) else data.encode('utf-8')); return p(name)
def read(name): return open(p(name), 'rb').read()
def run(*args): return subprocess.run([UVD, *args, '-q'], capture_output=True, text=True)
def report(name, ok, msg=''):
    print(f'{"○" if ok else "×"} {name}' + (f': {msg}' if msg else ''))
    results.append(ok)

OLD_T, NEW_T = b'a\nb\nc\n', b'a\nB\nc\n'
OLD_C, NEW_C = 'id,x\n1,a\n2,b\n'.encode(), 'id,x\n1,A\n3,c\n'.encode()

def refused(name, args, keep):
    """args を流すと終了コード 2 で断り、keep（ファイル名 → 元の中身）が変わらないこと。"""
    r = run(*args)
    intact = all(read(k) == v for k, v in keep.items())
    report(name, r.returncode == 2 and intact and '入力と同じ' in r.stderr,
           '' if r.returncode == 2 and intact else f'終了コード {r.returncode}・元のまま {intact} {r.stderr.strip()}')

def reset():
    write('old.txt', OLD_T); write('new.txt', NEW_T); write('old.csv', OLD_C); write('new.csv', NEW_C)
    run(p('old.csv'), p('new.csv'), '-key', 'id', '-o', p('diff.csv'))

reset()
refused('テキストの -o が旧と同じ名前', [p('old.txt'), p('new.txt'), '-o', p('old.txt')], {'old.txt': OLD_T})
refused('テキストの -o が新と同じ名前', [p('old.txt'), p('new.txt'), '-o', p('new.txt')], {'new.txt': NEW_T})
refused('表の -o が旧と同じ名前', [p('old.csv'), p('new.csv'), '-key', 'id', '-o', p('old.csv')], {'old.csv': OLD_C})
refused('-json が旧と同じ名前', [p('old.txt'), p('new.txt'), '-json', p('old.txt')], {'old.txt': OLD_T})
refused('apply の -o が旧と同じ名前', ['apply', p('old.csv'), p('new.csv'), p('diff.csv'), '-o', p('old.csv')], {'old.csv': OLD_C})
refused('apply の -o が差分と同じ名前', ['apply', p('old.csv'), p('new.csv'), p('diff.csv'), '-o', p('diff.csv')], {'old.csv': OLD_C})
# 同じファイルを別の書き方で（./ や .. を混ぜる）
refused('テキストの -o が旧と同じ（書き方が違う）', [p('old.txt'), p('new.txt'), '-o', os.path.join(WORK, '.', 'x', '..', 'old.txt')], {'old.txt': OLD_T})

# シンボリックリンク経由（作れない環境〔Windows の既定など〕では飛ばす）
try:
    os.symlink(p('old.csv'), p('link_old.csv'))
    os.symlink(p('old.txt'), p('link_old.txt'))
    os.makedirs(p('real_dir'))
    os.symlink(WORK, p('real_dir/up'))           # フォルダのリンクを途中に挟む
    can_link = True
except (OSError, NotImplementedError):
    can_link = False
if can_link:
    refused('apply の -o が旧へのシンボリックリンク', ['apply', p('old.csv'), p('new.csv'), p('diff.csv'), '-o', p('link_old.csv')], {'old.csv': OLD_C})
    refused('テキストの -o が旧へのシンボリックリンク', [p('old.txt'), p('new.txt'), '-o', p('link_old.txt')], {'old.txt': OLD_T})
    refused('表の -o が途中のフォルダのリンク経由で旧', [p('old.csv'), p('new.csv'), '-key', 'id', '-o', p('real_dir/up/old.csv')], {'old.csv': OLD_C})
else:
    report('シンボリックリンク経由', True, 'リンクを作れないので飛ばした')

# ハードリンク: 名前では見抜けないが、一時ファイルに書いてから入れ替えるので、入力（旧）は元のまま
try:
    os.link(p('old.txt'), p('hard_old.txt'))
    r = run(p('old.txt'), p('new.txt'), '-o', p('hard_old.txt'))
    report('テキストの -o が旧のハードリンク（旧は元のまま）', r.returncode in (1, 2) and read('old.txt') == OLD_T,
           f'終了コード {r.returncode}')
except (OSError, NotImplementedError):
    report('ハードリンク', True, '作れないので飛ばした')

# 失敗しても、書き出し先の元の中身は残る・一時ファイルも残らない（新が無い → エラー）
write('keep.diff', b'KEEP')
r = run(p('old.txt'), p('missing.txt'), '-o', p('keep.diff'))
left = [f for f in os.listdir(WORK) if '.uvd-' in f]
report('失敗しても書き出し先は元のまま・一時ファイルも残らない', r.returncode == 2 and read('keep.diff') == b'KEEP' and not left,
       f'終了コード {r.returncode}・残った一時ファイル {left}')
# 成功すれば書き出し先が入れ替わる・一時ファイルは残らない
r = run(p('old.txt'), p('new.txt'), '-o', p('keep.diff'))
left = [f for f in os.listdir(WORK) if '.uvd-' in f]
report('成功すれば書き出し先が入れ替わる', r.returncode == 1 and read('keep.diff').startswith(b'--- ') and not left, f'残った一時ファイル {left}')

# CP932 のまま -i: 2 バイト文字の 2 バイト目を変えない
write('j1.txt', 'abc、ABC\n'.encode('cp932')); write('j2.txt', 'abc∥abc\n'.encode('cp932')); write('j3.txt', 'ABC、abc\n'.encode('cp932'))
r1 = run(p('j1.txt'), p('j2.txt'), '-enc', 'cp932', '-i')
r2 = run(p('j1.txt'), p('j3.txt'), '-enc', 'cp932', '-i')
report('CP932 で -i:「、」と「∥」は違う', r1.returncode == 1, f'終了コード {r1.returncode}')
report('CP932 で -i: 英字の大小だけの違いは同じ', r2.returncode == 0, f'終了コード {r2.returncode}')
write('j4.txt', 'ソ表ＡＢＣ abc\n'.encode('cp932')); write('j5.txt', 'ソ表ＡＢＣ ABC\n'.encode('cp932'))
r3 = run(p('j4.txt'), p('j5.txt'), '-enc', 'cp932', '-i')
report('CP932 で -i: 2 バイト目が \\（ソ表）・全角の英字はそのまま、半角の大小は同じ', r3.returncode == 0, f'終了コード {r3.returncode}')

# 最後の同じ行に改行が無いとき（文脈の行にも「改行なし」）
for name, o, n in (('改行の無い同じ最終行（文脈）', b'old\nlast', b'new\nlast'),
                   ('改行の無い同じ最終行（前後 3 行の外）', b'x\n1\n2\n3\n4\nlast', b'y\n1\n2\n3\n4\nlast')):
    write('e1.txt', o); write('e2.txt', n)
    run(p('e1.txt'), p('e2.txt'), '-o', p('e.diff'))
    c = subprocess.run([sys.executable, '-I', CHECK, p('e1.txt'), p('e2.txt'), p('e.diff')], capture_output=True, text=True)
    ok = c.returncode == 0
    if ok and shutil.which('patch'):
        shutil.copy(p('e1.txt'), p('ep.txt'))
        pr = subprocess.run(['patch', '-s', '-f', p('ep.txt'), p('e.diff')], capture_output=True, text=True)
        ok = pr.returncode == 0 and read('ep.txt') == n
    report(name, ok, c.stdout.strip())

print('すべて一致' if all(results) else '不一致あり')
sys.exit(0 if all(results) else 1)
