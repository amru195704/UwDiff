# UwDiff (UVD)

[日本語](README.md)

**A free tool that compares many and huge files fast — in any format, any character encoding, on any OS.** Ships with a GUI (UwDiff) and a CLI (`uvd`).

> Logs, CSV, Excel or PDF. Shift-JIS or UTF-16. One 10 GB file or a folder of 10,000 files.

| | |
|---|---|
| **Formats** | text (logs, CSV, TSV, source), the content of 7 compression formats (gz, bz2, xz, lzma, zst, lz4, br), text of Word (.docx), Excel (.xlsx) and PDF |
| **Encodings** | UTF-8, Shift-JIS (CP932), EUC-JP, UTF-16 (with or without BOM), detected automatically; old and new may differ |
| **Comparisons** | text (order-preserving, unified diff), tables (key columns, **row order may differ**, cell by cell), order-independent lines, folder vs folder |
| **Apply selected changes** | pick rows of a table diff and write a new file, keeping the old file's encoding, delimiter, line endings and quoting |
| **Size and speed** | key matching of CSV 10 GB × 2 in about 21 s, text 10 GB × 2 in about 21 s (measured on a MacBook Air M4). Spills to temporary files instead of running out of memory |
| **OS** | macOS, Linux, Windows |

## UwDiff in numbers

Two 10 GB files (about 100 million rows each), same Mac, same test data. Every time below is for a diff that **matched the expected result exactly**.

| Task | Other tools | **UwDiff** |
|---|---|---|
| **Key-match CSVs whose rows are in a different order** | qsv diff stopped after 10 min / DuckDB SQL 220.06 s | **`uvd … -key` 22.61 s** |
| **Key-match CSVs whose rows are in the same order** | qsv diff 32.56 s (1.5×) / DuckDB 124.66 s (5.8×) | **21.52 s** |
| **Compare text (same output as `diff -u`)** | GNU diff 150.93 s, 17.23 GiB / macOS diff 289.24 s, 25.86 GiB | **21.23 s, 0.41 GiB** |
| **Compare a Shift-JIS CSV with a UTF-8 one** | iconv, then GNU diff 339.66 s / iconv, then qsv diff 160.58 s | **as is, no conversion: 26.15 s (text), 23.57 s (key matching)** |
| **Compare in a GUI** | KDiff3 and P4Merge showed nothing at 1 GB after 600 s | **10 GB shown in 21.91 s** |
| **Open both in Excel** | cut off at row 1,048,576; `1-10` becomes a date | **no row limit; cells kept as written** |

**Losses and ties first.**

- **For tables in the same row order, qsv diff ties at 1 GB** (2.73 s vs 2.28 s) and is 1.5–1.6× slower at 5–10 GB. **qsv diff uses less memory at every size** (10 GB: qsv 1.7–2.1 GiB, UwDiff 4.2–4.5 GiB).
- **For tables in a shuffled order, UwDiff uses 12.7–13.4 GiB at 10 GB.** With a memory cap (e.g. `-mem 476MB`) it spills to temporary files and still finishes, in 58–60 s.
- **The diff is not always minimal.** On test data with rows shuffled in small blocks it was 0.5% larger than GNU diff's (both are correct: applying either to the old file reproduces the new file byte for byte).
- **Comparing two folders of 10,000 files ties with `diff -rq`** (3.1 s vs 3.3 s).

> Differences under 1.5× are called a tie, not a win. Times are from one Mac (conditions at [the end of this page](#conditions)). **Do not compare times across machines.**

---

## Tables by key: vs qsv diff and DuckDB

`uvd old.csv new.csv -key ID` / `qsv diff --key 0` / a DuckDB SQL join on the key. (×) is how many times longer than UwDiff.

| Size × 2 | Row order | Encoding | **UwDiff 1.0.0** | qsv diff 26.0.0 | DuckDB 2.0.0 |
|---|---|---|---|---|---|
| 1 GB (10.53 M rows) | same | UTF-8 | **2.28 s** | 2.73 s (tie) | 4.05 s (1.8×) |
| 1 GB | same | CP932 | **2.15 s** | 2.84 s (tie) | 11.39 s (5.3×) |
| 1 GB | shuffled | UTF-8 | **2.27 s** | 5.87 s (2.6×) | 4.41 s (1.9×) |
| 1 GB | shuffled | CP932 | **2.18 s** | 5.85 s (2.7×) | 11.70 s (5.4×) |
| 5 GB (52.67 M rows) | same | UTF-8 | **10.88 s** | 17.15 s (1.6×) | 23.33 s (2.1×) |
| 5 GB | same | CP932 | **10.24 s** | 15.65 s (1.5×) | 59.67 s (5.8×) |
| 5 GB | shuffled | UTF-8 | **11.11 s** | 36.01 s (3.2×) | 24.39 s (2.2×) |
| 5 GB | shuffled | CP932 | **10.57 s** | 35.76 s (3.4×) | 60.65 s (5.7×) |
| 10 GB (105.35 M rows) | same | UTF-8 | **21.52 s** | 32.56 s (1.5×) | 124.66 s (5.8×) |
| 10 GB | same | CP932 | **20.35 s** | 32.56 s (1.6×) | 188.34 s (9.3×) |
| 10 GB | shuffled | UTF-8 | **22.61 s** | stopped after 10 min | 220.06 s (9.7×) |
| 10 GB | shuffled | CP932 | **21.90 s** | stopped after 10 min | 357.91 s (16.3×) |

- **Memory (10 GB)**: same order — UwDiff 4.2–4.5 GiB, qsv 1.7–2.1 GiB (**qsv uses less**), DuckDB about 18 GiB. Shuffled — UwDiff 12.7–13.4 GiB, DuckDB 20–21 GiB; qsv was stopped at about 18 GiB.
- UwDiff detects the encoding itself, so CP932 runs as fast as UTF-8. DuckDB needs an extension and an explicit setting for CP932, and took 1.5–2.8× its UTF-8 time.

**Beyond Compare and Excel** (for reference; 2026-10-23, same Mac, different test data)

- **Beyond Compare 5.2.6** (table compare with a key): 100 k rows (10 MB) about 145 s; 526 k rows (50 MB) 16 min 18 s (counts matched). 1.05 M rows (100 MB) was stopped after 20 min. CP932 is not detected, and even with Shift-JIS set by hand, characters such as `①` and `髙` cause errors.
- **Excel (Mac)**: cuts off at row 1,048,576; `1-10` becomes a date; UTF-8 without a BOM is read as Shift-JIS.

→ Read more: [Comparing a CSV too big for Excel against last month's version](https://uvp.y42u.net/en/blog/uwdiff-excel-csv-monthly-compare-en/)

---

## Text: vs GNU diff and macOS diff

`uvd old new -o changes.diff` / `diff -u old new`. Order-preserving comparison (unified diff), rows in the same order. Time and memory.

| Size × 2 | Encoding | **UwDiff 1.0.0** | GNU diff 3.12 | macOS diff |
|---|---|---|---|---|
| 1 GB | UTF-8 | **2.37 s**, 0.31 GiB | 8.24 s (3.5×), 3.15 GiB | 11.00 s (4.6×), 2.87 GiB |
| 1 GB | CP932 | **2.23 s**, 0.29 GiB | 8.50 s (3.8×), 3.04 GiB | 10.70 s (4.8×), 2.76 GiB |
| 5 GB | UTF-8 | **10.74 s**, 0.41 GiB | 41.24 s (3.8×), 13.78 GiB | 96.82 s (9.0×), 14.39 GiB |
| 5 GB | CP932 | **9.86 s**, 0.39 GiB | 42.77 s (4.3×), 13.21 GiB | 86.98 s (8.8×), 13.82 GiB |
| 10 GB | UTF-8 | **21.23 s**, 0.41 GiB | 150.93 s (7.1×), 17.23 GiB | 289.24 s (13.6×), 25.86 GiB |
| 10 GB | CP932 | **20.77 s**, 0.41 GiB | 117.92 s (5.7×), 18.54 GiB | 279.68 s (13.5×), 26.19 GiB |

- **Diff lines** (10 GB): UwDiff 2.547 M, GNU diff 2.565 M, macOS diff 2.566 M. All three reproduce the new file byte for byte when applied to the old one.
- **Why the gap grows with size**: GNU diff **reads both files into memory first**, hashes every line, and runs Myers' algorithm over all of them at once. UwDiff **compares while reading**, 8 MB at a time on a separate thread per file, and runs Myers only on a window around each difference. Memory stays around 0.4 GiB and the time is "reading time plus a little". The trade-off: the diff is not always minimal (see the losses above).
- Since 1.1.0, a common prefix is skipped as raw bytes without splitting it into lines (the same idea as GNU diff's prefix trimming). At 1 GB with a long common prefix it is about 3× faster; at 10 GB the time is set by reading and does not change ([docs/benchmarks.md](docs/benchmarks.md), Japanese).

→ Read more: [Why GNU diff takes 150 s on 10 GB — I read the source](https://uvp.y42u.net/en/blog/uwdiff-gnu-diff-grep-source-en/)

---

## Two files in different encodings: vs converting first

Old is UTF-8 (LF), new is Shift-JIS (CRLF). `diff` compares bytes, so almost every line containing Japanese shows up as "different". The usual fix is to convert with `iconv -f CP932` first.

| 10 GB (105.34 M rows) × 2 | Convert | Compare | Total |
|---|---|---|---|
| iconv + GNU diff (`-u --strip-trailing-cr`) | 126.11 s | 213.55 s | 339.66 s |
| iconv + qsv diff (key matching) | 126.11 s | 34.47 s | 160.58 s |
| **UwDiff (as is, text)** | — | — | **26.15 s** |
| **UwDiff (as is, key matching)** | — | — | **23.57 s** |

- UwDiff converts to UTF-8 while reading, so there is no 9.4 GB converted file to write and no disk space to free for it.
- **It is 1.1–1.3× slower than comparing two files in the same encoding.** If you compare the same pair many times, converting once up front and using qsv diff is just as good.
- GUI comparison tools garble Shift-JIS by default too: KDiff3 1.12.6 reads it as UTF-8, and P4Merge 2026.3 stays on "System". Both need the encoding set before opening.

---

## GUI: vs KDiff3 and P4Merge

Text, UTF-8, from "Compare" until the result is displayed.

| GUI comparison tool | 100 MB | 1 GB | 5 GB | 10 GB |
|---|---|---|---|---|
| **UwDiff 1.0.0** | **2.22 s** | **3.22 s** | **11.23 s** | **21.91 s, 1.37 GiB** |
| P4Merge 2026.3 | 12.61 s, 3.77 GiB | not shown in 600 s | — | — |
| KDiff3 1.12.6 | 59.26 s, 1.27 GiB | not shown in 600 s | — | — |

- In CP932, UwDiff took 1.35 → 3.27 → 10.83 → 19.51 s and read the files with no encoding setting.
- After opening 10 GB (1.16 M changes), jumping to the last change takes 27 ms.
- A GUI gives no outside signal that it is done, so the end time is when the app stopped using CPU after drawing, sampled every 0.2 s (error about ±0.2 s).

**Compare on the command line and open the result straight in the GUI:**

```
uvd old.log.gz new.log.gz -open
```

→ Read more: [Why I built UwDiff](https://uvp.y42u.net/en/blog/uwdiff-why-built-en/)

---

## What each tool can do

| | **UwDiff** | Beyond Compare 5 | qsv diff | DuckDB | GNU diff |
|---|---|---|---|---|---|
| GUI | yes | yes | no (separate product) | partly (an SQL UI) | no |
| CLI | yes, `uvd` | yes (scripts) | yes | yes (SQL) | yes |
| CLI result straight into the GUI | yes, `-open` | — | — | — | — |
| Encodings | **auto-detected**; old and new may differ | set by hand | assumes UTF-8 | UTF-8, UTF-16, Latin-1 (others via extension) | raw bytes |
| Tables by key (row order may differ) | yes (CSV, TSV, Excel) | yes | yes | yes (write SQL) | no |
| Text, order-preserving | yes | yes | no | no | yes |
| Folder vs folder | yes | yes | no | no | yes (`-r`) |
| Inside compressed files | **7 formats** | gz, bz2, xz, zip, 7z and more | Snappy | gzip, zstd | no |
| Word, Excel, PDF | yes | yes (read-only) | exports xlsx to CSV | reads xlsx | no |
| Apply only the picked changes | yes (keeps encoding, delimiter, line endings) | yes (copy in the GUI) | no | write SQL | `patch` applies all |

Each tool has strengths not listed here (Beyond Compare's image and binary compare and 3-way merge, qsv's many CSV commands, DuckDB's analytical SQL). Sources and the dates they were checked are on the [benchmarks page](https://uvp.y42u.net/en/uwdiff-benchmarks-en/).

→ Read more: [Choosing a comparison tool on six axes](https://uvp.y42u.net/en/blog/uwdiff-six-axes-en/)

---

## Which one to use

| You want to | Use |
|---|---|
| **Compare CSVs or logs of several GB or more** | **`uvd`** (tables by key, even in a different row order) |
| **Compare a Shift-JIS file with a UTF-8 one, once** | **`uvd`** (as is, no conversion) |
| **Compare the same pair many times, with little memory** | convert to UTF-8 once, then **qsv diff** is a solid choice (a tie in speed when rows are in order, and less memory) |
| **See the result in a GUI** | **`uvd … -open`**, or the UwDiff GUI |
| **Apply only the picked changes from a table diff** | **`uvd apply`** (keeps the old file's encoding, delimiter, line endings and quoting) |
| **Get a minimal diff of text that fits easily in memory** | **GNU diff** (`--minimal`) |
| **Compare images or binaries, or do a 3-way merge** | a dedicated tool such as Beyond Compare |
| **Aggregate or analyse with SQL after comparing** | **DuckDB** |

---

## Download

**With a package manager** (installs both the UwDiff GUI and the `uvd` CLI)

**macOS (Homebrew)**
```bash
brew install --cask amru195704/uwview/uwdiff
```
**Windows (Scoop)**
```powershell
scoop bucket add uwview https://github.com/amru195704/scoop-uwview   # skip if already added
scoop install uwdiff
```

**If Scoop is not installed yet on Windows (first time only)**
Run these in **PowerShell**, not Command Prompt. Search the Start menu for "PowerShell" and open it as a normal user (**not** as administrator). The prompt starts with `PS C:\Users\...>` in PowerShell.
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser   # answer Y if asked
Invoke-RestMethod -Uri https://get.scoop.sh | Invoke-Expression        # installs Scoop
scoop install git                                                        # adding a bucket needs git
```
Then run `scoop bucket add …` and `scoop install uwdiff` above. If `Set-ExecutionPolicy` is "not recognized", you are in Command Prompt.
**Linux (Snap)**
```bash
sudo snap install uwdiff
```
In the Snap the CLI is called `uwdiff.uvd` (run `sudo snap alias uwdiff.uvd uvd` to type `uvd`).

Homebrew and Scoop both download the packages from these [Releases](https://github.com/amru195704/UwDiff/releases) and check their SHA256.
Update with `brew upgrade --cask uwdiff` / `scoop update uwdiff` / `sudo snap refresh uwdiff`.

**Manual install**: get the package for your OS from [Releases](https://github.com/amru195704/UwDiff/releases) (.dmg for macOS, .tar.gz for Linux, .zip for Windows). No separate .NET installation is needed. Also on the site's [download page](https://uvp.y42u.net/en/uwdiff-download-en/).

## Usage

GUI: open UwDiff, choose (or drop) the old and new files or folders, and click Compare.

CLI:

```
uvd old.log new.log -o changes.diff               # text (same format as diff -u)
uvd old.log.gz new.log.gz -open                   # compare compressed files, open the result in the GUI
uvd old.csv new.csv -key ID -o diff.csv           # tables by key
uvd ledger_09.xlsx ledger_10.xlsx -key ID         # Excel, sheet by sheet
uvd release_prev/ release_now/ -o list.csv        # folder vs folder
uvd apply old.csv new.csv picked.csv -o out.csv   # apply only the picked changes
```

Exit codes follow diff (0 = same, 1 = different, 2 = error). See the [user manual](docs/manual.en.md) ([日本語](docs/manual.ja.md)).

---

## More

| | |
|---|---|
| 🏠 **Product page** | [UwDiff](https://uvp.y42u.net/en/uwdiff-en/) ([日本語](https://uvp.y42u.net/uwdiff/)) |
| 📖 **User manual** | [docs/manual.en.md](docs/manual.en.md) · [on the site](https://uvp.y42u.net/en/uwdiff-manual-en/) |
| 📊 **All measurements and conditions** | [Benchmarks page](https://uvp.y42u.net/en/uwdiff-benchmarks-en/). **Losses are listed as they are** |
| 🧾 **Release notes** | [v2.0.0](https://github.com/amru195704/UwDiff/releases/tag/v2.0.0) (previous: [v1.0.0](https://github.com/amru195704/UwDiff/releases/tag/v1.0.0)) |
| ▶ **Video (YouTube)** | [Demo (English)](https://www.youtube.com/watch?v=TeDYh7_MsjU) · [日本語](https://www.youtube.com/watch?v=qc1sY8wmOTc) |
| 📖 **Choosing a comparison tool on six axes** | [Article](https://uvp.y42u.net/en/blog/uwdiff-six-axes-en/) |
| 📖 **Why I built UwDiff** | [Article](https://uvp.y42u.net/en/blog/uwdiff-why-built-en/) |
| 📖 **Reading the GNU diff and grep source** | [Article](https://uvp.y42u.net/en/blog/uwdiff-gnu-diff-grep-source-en/) |
| 📖 **A CSV too big for Excel vs last month's version** | [Article](https://uvp.y42u.net/en/blog/uwdiff-excel-csv-monthly-compare-en/) · [Hashnode](https://hugefiles.hashnode.dev/comparing-a-csv-too-big-for-excel-against-last-month-s-version-key-matching-on-10-gb-with-excel-qsv-diff-and-duckdb) |
| 🔎 **To search and read huge files** | the sister tool [UwView](https://github.com/amru195704/UwView) (search command `uvf` and a GUI) |

---

## Conditions

- **Machine**: MacBook Air (Apple M4, 10 cores, 32 GB), macOS 26.3.1, data on an external USB SSD. **Times depend on the machine. Do not compare times across machines.**
- **CLI comparisons (tables, text)**: uvd 1.0.0, qsv 26.0.0, DuckDB 2.0.0, GNU diff 3.12, macOS diff, 2026-10-26. Cache purged before each run (`sudo purge`), one run each. Time is `/usr/bin/time` real; memory is maximum resident set size. Runs over 10 minutes were stopped. Script: `tools/bench_compare_mac.sh`.
- **Different encodings**: same machine and method, macOS's own iconv, 2026-11-04.
- **GUI**: UwDiff 1.0.0, KDiff3 1.12.6, P4Merge 2026.3, 2026-10-27. Script: `tools/bench_gui_mac.sh`.
- **Beyond Compare and Excel**: 2026-10-23, different test data (for reference).
- **Test data**: CSV (columns: ID, X, Y, Z, type, station no., date, note). The new file is made from the old one with deletions, additions, edits, renamed keys and newlines inside quotes (generators in `tools/testdata/`, fixed seed). Same order = as old; shuffled = fully reordered. UTF-8 (LF) and CP932 (CRLF).
- **v2.0.0 compares exactly as 1.x does** (2.0.0 added a support notice and small fixes).
- Differences under 1.5× are called a tie.

---

## Building from source

Requires the .NET 10 SDK.

```
dotnet build -c Release
src/Uvd.Cli/bin/Release/net10.0/uvd -h          # CLI
dotnet run -c Release --project src/UwDiff.App  # GUI
tools/package.sh osx-arm64                      # packages (out/dist/)
tools/run_tests.sh                              # tests
```

## License

[UwDiff License](LICENSE) (PolyForm Internal Use 1.0.0 plus a permission to redistribute the official binaries as packages).
Bundled components: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Questions: [Issues](https://github.com/amru195704/UwDiff/issues).
**Results measured on your own huge files are welcome — especially when UwDiff lost.**
