# UwView.Formats

[UwView](https://github.com/amru195704/UwView) の `UwView.Core`（無料版の公開部品）から、圧縮ファイルの展開と Word・Excel・PDF の文字の取り出し、CLI をコマンドラインで使えるようにする設定（`Cli/CliCommandSetup.cs`）、ファイルの指定と除外の書き方（`Cli/Braces.cs`・`Cli/IgnoreRules.cs`。uvf と同じ書き方にするため）に要るファイルだけを写したもの。

- **ファイルは変えない**（直すときは UwView で直してから写し直す）。名前空間も `UwView.Core` のまま。
- 写した後に外すもの（`tools/sync_uwview.sh` が外す）: `OfficeDocumentFile.cs` の `UnsupportedText`（UwView Pro の案内。UwDiff では使わない）。
- 写し直し: `tools/sync_uwview.sh <UwView の作業ツリー>`
- ライセンス: UwView と同じ（UwDiff と同じ）。使っているパッケージ: SharpCompress（MIT）・ZstdSharp.Port（MIT）・K4os.Compression.LZ4.Streams（MIT）・PdfPig（Apache-2.0）

## 元

- リポジトリ: https://github.com/amru195704/UwView
- commit: `58f54146736c383ce6b21164e0f4ea8ab733db02`
- 写したファイル（`UwView.Core/` からの場所）:

  - `Cli/Braces.cs`
  - `Cli/CliCommandSetup.cs`
  - `Cli/IgnoreRules.cs`
  - `CompressedFormats.cs`
  - `CompressedInput.cs`
  - `Crc32.cs`
  - `Documents/CjkRadicals.cs`
  - `Documents/DocxText.cs`
  - `Documents/ExcelNumberFormat.cs`
  - `Documents/OfficeText.cs`
  - `Documents/PdfText.cs`
  - `Documents/XlsxText.cs`
  - `GzipDecoder.cs`
  - `LinkedFile.cs`
  - `NativeCompression.cs`
  - `OfficeDocumentFile.cs`
  - `TextProbe.cs`
  - `ThreadBudget.cs`
