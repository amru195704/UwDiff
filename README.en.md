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
| **Size and speed** | key matching of CSV 10 GB × 2 in about 21 s, text 10 GB × 2 in about 21 s (measured on a Mac mini M4). Spills to temporary files instead of running out of memory |
| **OS** | macOS, Linux, Windows |

## Download

Get the package for your OS from [Releases](https://github.com/amru195704/UwDiff/releases) (.dmg for macOS, .tar.gz for Linux, .zip for Windows). No separate .NET installation is needed.

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
