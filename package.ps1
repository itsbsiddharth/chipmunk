# Builds the downloadable files into .\release:
#   Chipmunk-Setup.exe     one-file installer (what the website links to)
#   Chipmunk-win-x64.zip   portable copy, for people who'd rather not install
param([string]$Dist, [string]$Release)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$release = if ($Release) { $Release } else { Join-Path $root 'release' }
$dist = if ($Dist) { $Dist } else { Join-Path $root 'dist' }

& (Join-Path $root 'build.ps1') -Out $dist

$version = [regex]::Match((Get-Content "$root\src\Program.cs" -Raw), 'Version = "([0-9.]+)"').Groups[1].Value
New-Item -ItemType Directory -Force $release | Out-Null
$work = Join-Path $env:TEMP "chipmunk-package-$PID"
New-Item -ItemType Directory -Force $work | Out-Null
try {
    # What gets installed: the app, its UI, the optional browser extension, licenses.
    $stage = Join-Path $work 'Chipmunk'
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item "$dist\Chipmunk.exe", "$dist\Microsoft.Web.WebView2.Core.dll", "$dist\Microsoft.Web.WebView2.WinForms.dll", "$dist\WebView2Loader.dll", "$dist\WebView2-LICENSE.txt", "$root\LICENSE" $stage
    Copy-Item "$dist\ui", "$dist\extension" $stage -Recurse
    Remove-Item "$stage\ui\mock-host.js" -ErrorAction SilentlyContinue

    $payload = Join-Path $work 'payload.zip'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)

    # The installer: setup\Setup.cs plus the shared drawing and install code, with the app inside.
    $buildInfo = Join-Path $work 'BuildInfo.cs'
    "namespace Chipmunk.Setup { static class BuildInfo { public const string Version = `"$version`"; } }" | Set-Content $buildInfo -Encoding utf8
    $csc = (Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\Roslyn\csc.exe", "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\Roslyn\csc.exe" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
    $setupExe = Join-Path $release 'Chipmunk-Setup.exe'
    $refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'Microsoft.CSharp.dll',
        'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll' | ForEach-Object { "/r:$_" }
    $src = "$root\setup\Setup.cs", $buildInfo, "$root\src\InstallInfo.cs", "$root\src\Popover.cs", "$root\src\Native.cs", "$root\src\Brand.cs", "$root\src\Mascot.cs"
    & $csc /nologo /noconfig /codepage:65001 /nowarn:0649 /target:winexe /platform:x64 /optimize+ /langversion:latest "/lib:$fw" "/out:$setupExe" `
        "/win32manifest:$root\src\app.manifest" "/win32icon:$root\assets\chipmunk.ico" "/resource:$payload,Chipmunk.Payload.zip" @refs @src
    if ($LASTEXITCODE -ne 0) { throw 'Setup compile failed.' }

    $zip = Join-Path $release 'Chipmunk-win-x64.zip'
    if (Test-Path $zip) { Remove-Item $zip }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
}
finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }

Get-Item "$release\Chipmunk-Setup.exe", "$release\Chipmunk-win-x64.zip" | ForEach-Object { "{0}  {1:N0} KB" -f $_.Name, ($_.Length / 1KB) }
"Version $version"
