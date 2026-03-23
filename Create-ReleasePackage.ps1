$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseVersion = "0.0.2"
$publishFolder = Join-Path $projectRoot "Release\WessTools-$releaseVersion-win-x64"
$zipPath = "$publishFolder.zip"

Write-Host "Cleaning previous release output..."
Remove-Item -Recurse -Force $publishFolder -ErrorAction SilentlyContinue
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

Write-Host "Publishing self-contained Windows build..."
dotnet publish "$projectRoot\WessTools.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishFolder

Write-Host "Creating zip package..."
Compress-Archive -Path "$publishFolder\*" -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host ""
Write-Host "Done."
Write-Host "Release folder: $publishFolder"
Write-Host "Release zip:    $zipPath"
Write-Host ""
Write-Host "Upload the zip to a GitHub Release instead of committing it to the repo."
