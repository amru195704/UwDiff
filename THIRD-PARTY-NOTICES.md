# Third-party notices / 同梱しているもののライセンス

UwDiff includes the following open-source components. The copyright notices and full license texts are in the `third-party/` folder that comes with every package (on macOS: `UwDiff.app/Contents/Resources/third-party/`).
UwDiff は次のオープンソースの部品を含みます。著作権表示とライセンスの全文は、配布物に同梱の `third-party/` フォルダー（macOS は `UwDiff.app/Contents/Resources/third-party/`）にあります。

| Component | Used for / 用途 | License | Full text / 全文 |
|---|---|---|---|
| .NET runtime and libraries 10.0 | runtime / 実行環境 | MIT (+ third-party notices) | `dotnet-LICENSE.txt`, `dotnet-THIRD-PARTY-NOTICES.txt` |
| Avalonia 12.0.5 (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, Avalonia.Skia, Avalonia.HarfBuzz, Avalonia.Native, Avalonia.Win32, Avalonia.X11, Avalonia.FreeDesktop ほか) | GUI / 画面 | MIT | `Avalonia-LICENSE.txt` |
| Inter font (via Avalonia.Fonts.Inter) | GUI font / 画面の字体 | SIL Open Font License 1.1 | `Inter-font-OFL.txt` |
| ANGLE (Avalonia.Angle.Windows.Natives, Windows only / Windows 版のみ) | GUI drawing on Windows / Windows の描画 | BSD-style / BSD 系 | `ANGLE-LICENSE.txt` |
| SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.3 | drawing, text shaping / 描画・文字の配置 | MIT | `SkiaSharp-LICENSE.txt` |
| Skia, HarfBuzz, FreeType, libpng and others (native libraries of SkiaSharp / HarfBuzzSharp) | 同上の中身 | BSD 3-Clause (Skia) and others / ほか | `SkiaSharp-THIRD-PARTY-NOTICES.txt` |
| MicroCom.Runtime (via Avalonia) | GUI / 画面 | MIT | `MicroCom-LICENSE.txt` |
| Tmds.DBus.Protocol (Linux, via Avalonia) | desktop integration / デスクトップとの連携 | MIT | `Tmds.DBus-LICENSE.txt` |
| SharpCompress | bzip2 / xz / lzma decoding / 展開 | MIT | `SharpCompress-LICENSE.txt` |
| ZstdSharp.Port (port of Zstandard) | zstd decoding / 展開 | MIT (+ Zstandard: BSD) | `ZstdSharp-LICENSE.txt` |
| K4os.Compression.LZ4(.Streams), K4os.Hash.xxHash (ports of LZ4 / xxHash) | lz4 decoding / 展開 | MIT (+ LZ4 / xxHash: BSD 2-Clause) | `K4os-LICENSE.txt` |
| PdfPig (UglyToad.PdfPig) | PDF text extraction / PDF の文字の取り出し | Apache License 2.0 | `PdfPig-LICENSE.txt` |
| UwView.Core (parts, copied in `src/UwView.Formats/`) | compressed files, Word / Excel / PDF text / 圧縮・Word・Excel・PDF | same license as UwDiff (same author) | `LICENSE` |

On macOS and Linux, UwDiff also uses the operating system's zlib / libbz2 / liblzma (and liblz4 on Linux) when they are available; otherwise it uses the managed decoders above. These OS libraries are not included in the packages.
macOS・Linux では、OS の zlib・libbz2・liblzma（Linux は liblz4 も）があれば使い、無ければ上の部品で展開します（OS のライブラリは配布物に含みません）。
