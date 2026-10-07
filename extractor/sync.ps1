Copy-Item -Path "$HOME\Documents\Klei\OxygenNotIncluded\DataDump\*.json" -Destination ".\public\data\" -Force
Write-Host "Game data synced successfully!" -ForegroundColor Green