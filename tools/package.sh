#!/usr/bin/env bash
# > パス: tools/package.sh
# UwDiff の配布物を作る（mac / Linux / Windows）。UwView の build/publish.sh と同じ作り。
#
# 生成物（out/dist/）:
#   UwDiff-<版>-mac-<arm64|x64>.dmg      UwDiff.app（中の Contents/MacOS に画面 UwDiff と CLI uvd）
#   UwDiff-<版>-linux-<x86_64|aarch64>.tar.gz   UwDiff-<版>/ に UwDiff・uvd
#   UwDiff-<版>-win-<x64|arm64>.zip      UwDiff-<版>/ に UwDiff.exe・uvd.exe
#   SHA256SUMS-<版>.txt（版数つきの名前で計算。UwView の dist と同じ名前の付け方）
#   latest/UwDiff-<mac-arm64.dmg など>・latest/SHA256SUMS・latest/VERSION（6 点が揃ったとき。tools/make_latest.sh）
# どれにも LICENSE・LICENSEjp.txt・THIRD-PARTY-NOTICES.md・third-party/ を入れる（mac は .app の Contents/Resources に）。
# 画面と CLI は同じフォルダに置く（uvd -open が隣の UwDiff を起動するため）。
#
# 署名・公証は MAC_SIGN_ID・AC_PROFILE があるときだけ（環境変数か tools/local.env）。無ければ未署名で作り、そう表示する。
#   MAC_SIGN_ID=<署名 ID> AC_PROFILE=<notarytool のプロファイル名> tools/package.sh osx-arm64
# 使い方: tools/package.sh [rid...]   既定: osx-arm64 osx-x64 linux-x64 linux-arm64 win-x64 win-arm64
set -euo pipefail
cd "$(dirname "$0")/.."
# 手元だけの設定（署名 ID・公証のプロファイルなど。Git に入れない）。環境変数で渡したものが優先
if [ -f tools/local.env ]; then
  _sign="${MAC_SIGN_ID:-}"; _ac="${AC_PROFILE:-}"
  # shellcheck disable=SC1091
  . tools/local.env
  [ -n "$_sign" ] && MAC_SIGN_ID="$_sign"
  [ -n "$_ac" ] && AC_PROFILE="$_ac"
fi

VER=$(grep -oE '<InformationalVersion>[^<]+' Directory.Build.props | sed 's/<InformationalVersion>//' | head -1)
: "${VER:=0.0.0}"
NAME=UwDiff
# 画面と CLI に同じビルド番号を入れる（Directory.Build.props が読む。無ければ今の日時）
export UVD_BUILD_NUMBER="${UVD_BUILD_NUMBER:-$(date '+%y.%m.%d.%H')}"
BUILD="${UVD_BUILD_NUMBER}"
BUNDLE_ID="${BUNDLE_ID:-net.y42u.uwdiff}"
OUT="${OUT:-out/dist}"
RIDS=("$@"); [ ${#RIDS[@]} -eq 0 ] && RIDS=(osx-arm64 osx-x64 linux-x64 linux-arm64 win-x64 win-arm64)
mkdir -p "$OUT"
echo "$NAME $VER (build $BUILD) → ${RIDS[*]}"

# 画面と CLI を、どちらも 1 本の実行ファイル（.NET 込み）にして同じフォルダに出す。
# 1 本の中身を圧縮する設定（EnableCompressionInSingleFile）は使わない: .NET 10 の osx-arm64 で、圧縮した 1 本だけが
# まれに AccessViolation で落ちた（8 本並べて約 1,000 回中 90 回。圧縮しない 1 本・開発用のビルドは 0 回。2026-10-24）。
# 配布物（DMG・zip・tar.gz）は圧縮するので、取得する大きさはほとんど変わらない
publish_one() { # $1=rid → 発行先を返す
  local rid="$1" pub="out/pub/$rid"
  rm -rf "$pub"
  for proj in src/UwDiff.App/UwDiff.App.csproj src/Uvd.Cli/Uvd.Cli.csproj; do
    dotnet publish "$proj" -c Release -r "$rid" --self-contained true \
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:DebugType=none -o "$pub" -v q -nologo 1>&2
  done
  find "$pub" \( -name '*.pdb' -o -name '*.runtimeconfig.json' \) -delete   # 1 本の中に入っている
  mkdir -p "$pub/third-party"
  cp LICENSE LICENSEjp.txt THIRD-PARTY-NOTICES.md "$pub/"
  cp third-party/* "$pub/third-party/"
  cp docs/manual.ja.md docs/manual.en.md "$pub/"
  echo "$pub"
}

pack_mac() { # $1=rid $2=arch
  local rid="$1" arch="$2" pub; pub=$(publish_one "$rid")
  local app="$OUT/$NAME.app" macos="$OUT/$NAME.app/Contents/MacOS" res="$OUT/$NAME.app/Contents/Resources"
  rm -rf "$app"; mkdir -p "$macos" "$res"
  cp "$pub/UwDiff" "$pub/uvd" "$macos/"
  cp -R "$pub/third-party" "$pub/LICENSE" "$pub/LICENSEjp.txt" "$pub/THIRD-PARTY-NOTICES.md" "$res/"
  cp "$pub"/manual.*.md "$res/" 2>/dev/null || true
  chmod +x "$macos/UwDiff" "$macos/uvd"
  cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>$NAME</string>
  <key>CFBundleDisplayName</key><string>$NAME</string>
  <key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
  <key>CFBundleExecutable</key><string>UwDiff</string>
  <key>CFBundleShortVersionString</key><string>$VER</string>
  <key>CFBundleVersion</key><string>$BUILD</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST

  if [ -n "${MAC_SIGN_ID:-}" ]; then
    echo "  codesign ($arch)…"
    # .NET の実行ファイルには entitlements が要る（JIT・実行メモリ・混在署名のライブラリ）。無いと起動時に落ちる
    local ent; ent="$(mktemp -t uwdiff-entitlements).plist"
    cat > "$ent" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict></plist>
PLIST
    # 内側から順に署名する（--deep には頼らない）
    codesign --force --timestamp --options runtime --entitlements "$ent" -s "$MAC_SIGN_ID" "$macos/uvd"
    codesign --force --timestamp --options runtime --entitlements "$ent" -s "$MAC_SIGN_ID" "$macos/UwDiff"
    codesign --force --timestamp --options runtime --entitlements "$ent" -s "$MAC_SIGN_ID" "$app"
    codesign --verify --deep --strict --verbose=2 "$app"
    rm -f "$ent"
    if [ -n "${AC_PROFILE:-}" ]; then
      local nz="$OUT/$NAME-$VER-mac-$arch-notarize.zip"
      ditto -c -k --keepParent "$app" "$nz"
      xcrun notarytool submit "$nz" --keychain-profile "$AC_PROFILE" --wait
      xcrun stapler staple "$app"
      rm -f "$nz"
    fi
  else
    echo "  ⚠ MAC_SIGN_ID が無いので未署名（配布の前に署名・公証が要る）"
  fi

  # DMG（Applications へのリンクを置き、ドラッグで入れられるように）。書き込めるイメージ → 置く → 圧縮 の順（UwView と同じ理由）
  local out="$OUT/$NAME-$VER-mac-$arch.dmg" vol="$NAME $VER ($arch)" rw mnt dev size
  rm -f "$out"
  rw=$(mktemp -u).rw.dmg; mnt=$(mktemp -d)
  size=$(( $(du -sm "$app" | cut -f1) + 60 ))
  hdiutil create -size "${size}m" -fs HFS+ -volname "$vol" -type UDIF -ov "$rw" >/dev/null
  dev=$(hdiutil attach "$rw" -nobrowse -noverify -noautoopen -mountpoint "$mnt" | grep -Eo '^/dev/disk[0-9]+' | head -1)
  ditto "$app" "$mnt/$NAME.app"
  ln -s /Applications "$mnt/Applications"
  sync
  hdiutil detach "$dev" -force >/dev/null
  rmdir "$mnt" 2>/dev/null || true
  hdiutil convert "$rw" -format UDZO -o "$out" >/dev/null
  rm -f "$rw"
  if [ -n "${MAC_SIGN_ID:-}" ]; then
    codesign --force --timestamp --sign "$MAC_SIGN_ID" "$out"
    if [ -n "${AC_PROFILE:-}" ]; then
      xcrun notarytool submit "$out" --keychain-profile "$AC_PROFILE" --wait
      xcrun stapler staple "$out"
    fi
  fi
  rm -rf "$app"
  echo "  → $out"
}

pack_dir() { # $1=rid $2=os $3=arch $4=形式(tar|zip)
  local rid="$1" os="$2" arch="$3" fmt="$4" pub; pub=$(publish_one "$rid")
  local stage="out/stage/$NAME-$VER"
  rm -rf "out/stage"; mkdir -p "$stage"
  cp -R "$pub/." "$stage/"
  local out
  if [ "$fmt" = tar ]; then
    chmod +x "$stage/UwDiff" "$stage/uvd"
    out="$OUT/$NAME-$VER-$os-$arch.tar.gz"
    COPYFILE_DISABLE=1 tar --no-xattrs -C out/stage -czf "$out" "$NAME-$VER"   # mac の拡張属性を入れない
  else
    out="$OUT/$NAME-$VER-$os-$arch.zip"
    rm -f "$out"
    (cd out/stage && zip -qr "../../$out" "$NAME-$VER")
  fi
  rm -rf out/stage
  echo "  → $out"
}

for rid in "${RIDS[@]}"; do
  case "$rid" in
    osx-arm64) pack_mac "$rid" arm64 ;;
    osx-x64) pack_mac "$rid" x64 ;;
    linux-x64) pack_dir "$rid" linux x86_64 tar ;;
    linux-arm64) pack_dir "$rid" linux aarch64 tar ;;
    win-x64) pack_dir "$rid" win x64 zip ;;
    win-arm64) pack_dir "$rid" win arm64 zip ;;
    *) echo "知らない rid: $rid" >&2; exit 2 ;;
  esac
done

(cd "$OUT" && shasum -a 256 "$NAME-$VER"-* > "SHA256SUMS-$VER.txt" && echo "SHA256SUMS-$VER.txt:" && cat "SHA256SUMS-$VER.txt")

# 6 点が揃っていれば latest/（版数を外した名前の複製・SHA256SUMS・VERSION）も作る
tools/make_latest.sh "$VER" "$OUT"
