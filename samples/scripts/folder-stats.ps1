# Island example (PowerShell): summarise the folder you're looking at in Explorer.
#
# Button setup:  Action = Run script, Script file = this file,
#                Arguments = "{currentFolder}", Output handling = JsonProtocol
#
# Anything printed before the last line is just log output (visible in the run log).
# The LAST non-empty line is parsed as JSON by Island.

param([string]$Folder = (Get-Location).Path)

# Make non-ASCII file names survive the trip through stdout.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if (-not (Test-Path -LiteralPath $Folder -PathType Container)) {
    @{ error = "Not a folder: $Folder" } | ConvertTo-Json -Compress
    exit 0
}

$files = Get-ChildItem -LiteralPath $Folder -File -Recurse -ErrorAction SilentlyContinue
$sizeMb = [math]::Round((($files | Measure-Object Length -Sum).Sum) / 1MB, 1)
$largest = $files | Sort-Object Length -Descending | Select-Object -First 10

Write-Output "Scanned $($files.Count) files in $Folder"

$report = ($largest | ForEach-Object { "{0,10:N0} KB  {1}" -f ($_.Length / 1KB), $_.FullName.Substring($Folder.Length).TrimStart('\') }) -join "`n"

@{
    notify    = "$($files.Count) files, $sizeMb MB in $(Split-Path $Folder -Leaf)"
    clipboard = $Folder
    popup     = "Largest files in $Folder`n`n$report"
} | ConvertTo-Json -Compress
