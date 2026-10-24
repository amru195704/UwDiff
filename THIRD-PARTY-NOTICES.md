# Third-party notices / 同梱しているもののライセンス

UwDiff includes the following open-source components. Their license texts are distributed with each package (NuGet) and summarized here.
UwDiff は次のオープンソースの部品を含みます。

| Component | Used for / 用途 | License |
|---|---|---|
| .NET runtime and libraries | runtime / 実行環境 | MIT |
| Avalonia (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, Avalonia.Skia ほか) | GUI / 画面 | MIT |
| Inter font (via Avalonia.Fonts.Inter) | GUI font / 画面の字体 | SIL Open Font License 1.1 |
| SkiaSharp, HarfBuzzSharp | drawing, text shaping / 描画・文字の配置 | MIT |
| Tmds.DBus.Protocol (Linux, via Avalonia) | desktop integration / デスクトップとの連携 | MIT |
| SharpCompress | bzip2 / xz / lzma decoding / 展開 | MIT |
| ZstdSharp.Port | zstd decoding / 展開 | MIT |
| K4os.Compression.LZ4.Streams | lz4 decoding / 展開 | MIT |
| PdfPig (UglyToad.PdfPig) | PDF text extraction / PDF の文字の取り出し | Apache License 2.0 (full text: `third-party/PdfPig-LICENSE.txt`) |
| UwView.Core (parts, copied in `src/UwView.Formats/`) | compressed files, Word / Excel / PDF text / 圧縮・Word・Excel・PDF | same license as UwDiff (same author) |

On macOS and Linux, UwDiff also uses the operating system's zlib / libbz2 / liblzma (and liblz4 on Linux) when they are available; otherwise it uses the managed decoders above.
macOS・Linux では、OS の zlib・libbz2・liblzma（Linux は liblz4 も）があれば使い、無ければ上の部品で展開します。
