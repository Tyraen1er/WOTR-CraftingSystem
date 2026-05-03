param(
    [Parameter(Mandatory = $true)]
    [string]$LogPath
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $LogPath)) {
    throw "Log file not found: $LogPath"
}

$patterns = @(
    "\[CUSTOM_ENCHANTS\]",
    "\[DYNAMIC_ENCHANT\]",
    "\[FORMULA\]",
    "\[ATELIER-DEBUG\]",
    "Error in BlueprintsCache\.Init",
    "Blueprint introuvable",
    "Binder FATAL",
    "EXCEPTION CRITIQUE"
)

Write-Host "== Crafting log signal extraction ==" -ForegroundColor Cyan
foreach ($pattern in $patterns) {
    $count = (Select-String -Path $LogPath -Pattern $pattern | Measure-Object).Count
    "{0,-35} {1,6}" -f $pattern, $count
}

Write-Host ""
Write-Host "Recent high-risk lines:" -ForegroundColor Yellow
Select-String -Path $LogPath -Pattern "FATAL|ERROR|EXCEPTION|CRASH|Blueprint introuvable" | Select-Object -Last 30
