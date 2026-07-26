# Commit and push to git
git add .
git commit -m "Update assembly name and title to CoolShift"
git push origin main

# Build self-contained Release single-file executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed"
    exit 1
}

$desktopDir = "C:\Users\james\Desktop\CoolShift"
if (-not (Test-Path $desktopDir)) {
    New-Item -ItemType Directory -Path $desktopDir -Force
}

$publishExe = Join-Path $PSScriptRoot "bin\Release\net8.0-windows\win-x64\publish\CoolShift.exe"
$desktopExe = Join-Path $desktopDir "CoolShift.exe"

Copy-Item -Path $publishExe -Destination $desktopExe -Force
Write-Host "Published self-contained Release binary to: $desktopExe"

# Create a ZIP for the release
$zipPath = "CoolShift-v2.1.3.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path $publishExe -DestinationPath $zipPath

# Create/upload release on GitHub
gh release create v2.1.3 $zipPath -t "CoolShift v2.1.3" -n "CoolShift release with updated assembly metadata and self-contained single-file executable."
Write-Host "GitHub release updated successfully."
