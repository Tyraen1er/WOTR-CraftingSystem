param(
    [string]$ProjectRoot = "."
)

$ErrorActionPreference = "Stop"
Set-Location $ProjectRoot

Write-Host "== Crafting System preflight checks ==" -ForegroundColor Cyan

Write-Host "[1/3] Build (Release)..." -ForegroundColor Yellow
dotnet build "CraftingSystem.csproj" -c Release | Out-Host

Write-Host "[2/3] Validate ModConfig/CustomEnchants.json..." -ForegroundColor Yellow
Get-Content "ModConfig/CustomEnchants.json" -Raw | ConvertFrom-Json | Out-Null

Write-Host "[3/3] Validate ModConfig/Localization.json..." -ForegroundColor Yellow
Get-Content "ModConfig/Localization.json" -Raw | ConvertFrom-Json | Out-Null

Write-Host "Preflight checks: PASS" -ForegroundColor Green
