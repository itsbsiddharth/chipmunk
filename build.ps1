# Builds Chipmunk into .\dist. Needs Windows 10/11 and the C# compiler from
# "Build Tools for Visual Studio" (free). Nothing else is downloaded.
param([string]$Out)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"

$csc = @(
    "$env:ProgramFiles\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\Roslyn\csc.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\Roslyn\csc.exe",
    "$env:ProgramFiles\Microsoft Visual Studio\2019\*\MSBuild\Current\Bin\Roslyn\csc.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\*\MSBuild\Current\Bin\Roslyn\csc.exe"
) | ForEach-Object { Get-ChildItem $_ -ErrorAction SilentlyContinue } | Select-Object -First 1 -ExpandProperty FullName
if (-not $csc) {
    throw "Couldn't find the C# compiler. Install 'Build Tools for Visual Studio' (C# workload), or use the prebuilt dist folder."
}

$dist = if ($Out) { $Out } else { Join-Path $root 'dist' }
New-Item -ItemType Directory -Force $dist | Out-Null
$icon = Join-Path $root 'assets\chipmunk.ico'
$exe = Join-Path $dist 'Chipmunk.exe'

function Compile([bool]$withIcon) {
    $refs = @(
        'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
        'System.Web.Extensions.dll', 'System.Xaml.dll', 'Microsoft.CSharp.dll',
        'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll',
        "$fw\WPF\PresentationCore.dll", "$fw\WPF\WindowsBase.dll",
        "$root\lib\Microsoft.Web.WebView2.Core.dll", "$root\lib\Microsoft.Web.WebView2.WinForms.dll"
    ) | ForEach-Object { "/r:$_" }
    $cscArgs = @('/nologo', '/noconfig', '/target:winexe', '/platform:x64', '/optimize+', '/langversion:latest',
        "/lib:$fw", "/out:$exe", "/win32manifest:$root\src\app.manifest") + $refs
    if ($withIcon) { $cscArgs += "/win32icon:$icon" }
    $cscArgs += (Get-ChildItem "$root\src\*.cs" | ForEach-Object { $_.FullName })
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0) { throw "Compile failed." }
}

Copy-Item "$root\lib\Microsoft.Web.WebView2.Core.dll", "$root\lib\Microsoft.Web.WebView2.WinForms.dll", "$root\lib\WebView2Loader.dll" $dist -Force

if (-not (Test-Path $icon)) {
    Compile $false
    New-Item -ItemType Directory -Force (Split-Path $icon) | Out-Null
    & $exe --write-icon $icon | Out-Null
}
Compile $true

# Artwork comes from the same drawing code as the app icon and the pet.
$extIcons = "$root\extension\icons"
New-Item -ItemType Directory -Force $extIcons, "$root\ui\assets" | Out-Null
foreach ($s in 16, 32, 48, 128) { if (-not (Test-Path "$extIcons\$s.png")) { & $exe --write-png "$extIcons\$s.png" $s | Out-Null } }
if (-not (Test-Path "$root\ui\assets\icon.png")) { & $exe --write-png "$root\ui\assets\icon.png" 96 | Out-Null }
if (-not (Test-Path "$root\ui\assets\chip.png")) { & $exe --write-mascot "$root\ui\assets\chip.png" 256 holding | Out-Null }

foreach ($folder in 'ui', 'extension') {
    $out = Join-Path $dist $folder
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Copy-Item "$root\$folder" $out -Recurse
}
Remove-Item "$dist\ui\mock-host.js" -ErrorAction SilentlyContinue
Copy-Item "$root\lib\WebView2-LICENSE.txt" $dist -Force
Write-Host "Built $exe"
