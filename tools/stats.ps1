# Chipmunk numbers in one place: downloads, stars and GitHub traffic.
# Run:  powershell -ExecutionPolicy Bypass -File tools\stats.ps1
# Needs the GitHub CLI (gh) signed in. GitHub only keeps 14 days of traffic, so each run
# also appends a dated row to stats-history.csv (kept out of git) to build a long-term record.
$ErrorActionPreference = 'Stop'
$repo = 'itsbsiddharth/chipmunk'
function Api($path) { gh api $path | ConvertFrom-Json }

$releases = Api "repos/$repo/releases"
$info = Api "repos/$repo"
$views = Api "repos/$repo/traffic/views"
$clones = Api "repos/$repo/traffic/clones"
$refs = Api "repos/$repo/traffic/popular/referrers"

$setup = 0; $zip = 0
"Downloads by release"
foreach ($r in $releases) {
    $s = ($r.assets | Where-Object name -eq 'Chipmunk-Setup.exe' | Measure-Object download_count -Sum).Sum
    $z = ($r.assets | Where-Object name -eq 'Chipmunk-win-x64.zip' | Measure-Object download_count -Sum).Sum
    $setup += [int]$s; $zip += [int]$z
    "  {0,-8} installer {1,5}   zip {2,5}" -f $r.tag_name, [int]$s, [int]$z
}
""
"Total downloads     {0}  (installer {1}, zip {2})" -f ($setup + $zip), $setup, $zip
"Stars               {0}" -f $info.stargazers_count
"Forks               {0}" -f $info.forks_count
"Repo views, 14 days {0}  ({1} unique visitors)" -f $views.count, $views.uniques
"Repo clones, 14 days {0} ({1} unique)" -f $clones.count, $clones.uniques
if ($refs) { ""; "Where repo visitors came from (14 days)"; $refs | ForEach-Object { "  {0,-24} {1,4} views, {2} unique" -f $_.referrer, $_.count, $_.uniques } }
""
"Website visitors: Vercel dashboard > chipmunk > Analytics"

$history = Join-Path (Split-Path $PSScriptRoot) 'stats-history.csv'
if (-not (Test-Path $history)) { 'date,downloads,installer,zip,stars,forks,views14d,uniques14d' | Set-Content $history -Encoding utf8 }
'{0},{1},{2},{3},{4},{5},{6},{7}' -f (Get-Date -Format 'yyyy-MM-dd'), ($setup + $zip), $setup, $zip, $info.stargazers_count, $info.forks_count, $views.count, $views.uniques | Add-Content $history -Encoding utf8
"Saved to $history"
