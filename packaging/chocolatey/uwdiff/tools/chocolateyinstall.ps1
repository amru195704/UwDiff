$ErrorActionPreference = 'Stop'
$toolsDir = "$(Split-Path -Parent $MyInvocation.MyCommand.Definition)"

# Downloads the official, unmodified zip from GitHub Releases (not embedded).
$packageArgs = @{
  packageName    = $env:ChocolateyPackageName
  unzipLocation  = $toolsDir
  url64bit       = 'https://github.com/amru195704/UwDiff/releases/download/v2.0.1/UwDiff-2.0.1-win-x64.zip'
  checksum64     = 'ae5ec932ef8e11ec17af400b1c7b02209260980e37faa6bb8173e717637bfd7e'
  checksumType64 = 'sha256'
}
Install-ChocolateyZipPackage @packageArgs

# uvd.exe -> console shim; UwDiff.exe -> GUI shim (the zip has a top folder UwDiff-<version>)
New-Item -ItemType File -Force -Path (Join-Path $toolsDir "UwDiff-$($env:ChocolateyPackageVersion)\UwDiff.exe.gui") | Out-Null
