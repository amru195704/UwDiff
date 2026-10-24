# > パス: tools/run_tests_win.ps1
# UwDiff の試験を Windows で流す（配布物の uvd.exe か、ソースからビルドしたものを使う）。
#   1) 危ないところ（表・反映）  2) テキストの比較  3) 形式とフォルダ（無い圧縮の道具は飛ばす）
#   4) 負荷（約 400 回続けて実行し、異常終了が無いか）  5) 画面の試験（.NET 10 SDK があるとき・Headless）
# 要るもの: Python 3（python か py）。.NET 10 SDK は 5) とソースからのビルドのときだけ。
# 使い方（PowerShell）:
#   tools\run_tests_win.ps1                                  # ソースからビルドして試す
#   tools\run_tests_win.ps1 -Uvd C:\UwDiff-1.0.0\uvd.exe      # 配布物の uvd.exe を試す
param([string]$Uvd = "", [string]$Work = "$env:TEMP\uvd-win-tests")
$ErrorActionPreference = "Stop"
$Repo = Split-Path -Parent $PSScriptRoot
$Py = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $Py) { $Py = (Get-Command py -ErrorAction SilentlyContinue).Source }
if (-not $Py) { Write-Error "Python 3 がありません"; exit 2 }
if (-not $Uvd) {
  dotnet build "$Repo\src\Uvd.Cli\Uvd.Cli.csproj" -c Release -v q -nologo
  $Uvd = "$Repo\src\Uvd.Cli\bin\Release\net10.0\uvd.exe"
}
New-Item -ItemType Directory -Force -Path $Work | Out-Null
$Results = @()
function Run-Case($name, [string[]]$pyargs) {
  $out = & $Py -I @pyargs 2>&1
  $ok = $LASTEXITCODE -eq 0
  $out | Set-Content -Encoding utf8 "$Work\$name.txt"
  $pass = ($out | Select-String '^○').Count; $fail = ($out | Select-String '^×').Count
  $script:Results += [pscustomobject]@{ 試験 = $name; 合否 = $(if ($ok) { "合格" } else { "不合格" }); 内訳 = "$pass 件合格・$fail 件不合格" }
  if (-not $ok) { $out | Select-String '^×' | ForEach-Object { Write-Host "  $_" } }
}
& $Uvd --version
Run-Case "危ないところ（3,000 行）" @("$Repo\tools\edgecases.py", $Uvd, "$Work\edge", "3000")
Run-Case "危ないところ（10 万行・塊 1MB）" @("$Repo\tools\edgecases.py", $Uvd, "$Work\edge100k", "100000", "-block", "1")
Run-Case "テキスト（5,000 行）" @("$Repo\tools\textcases.py", $Uvd, "$Work\text", "5000")
Run-Case "形式とフォルダ" @("$Repo\tools\formatcases.py", $Uvd, "$Work\formats")

# 負荷: 同じ比較を続けて実行し、異常終了（終了コード 2 より大きい・負）が無いか
$o = "$Work\text\small_edits_old.txt"; $n = "$Work\text\small_edits_new.txt"; $bad = 0
for ($i = 0; $i -lt 400; $i++) { & $Uvd $o $n -q | Out-Null; if ($LASTEXITCODE -gt 1 -or $LASTEXITCODE -lt 0) { $bad++ } }
$Results += [pscustomobject]@{ 試験 = "負荷（400 回）"; 合否 = $(if ($bad -eq 0) { "合格" } else { "不合格" }); 内訳 = "異常終了 $bad 回" }

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
  $env:UVD_SHOTS = "$Work\shots"
  $ui = dotnet test "$Repo\src\UwDiff.UiTests" -c Release -v q -nologo 2>&1
  $ok = $LASTEXITCODE -eq 0
  $Results += [pscustomobject]@{ 試験 = "画面（UwDiff.UiTests）"; 合否 = $(if ($ok) { "合格" } else { "不合格" }); 内訳 = ($ui | Select-String '合格:|Passed' | Select-Object -Last 1) }
}
$Results | Format-Table -AutoSize
$Results | Export-Csv -Encoding utf8 -NoTypeInformation "$Work\summary.csv"
if ($Results | Where-Object { $_.合否 -ne "合格" }) { exit 1 } else { exit 0 }
