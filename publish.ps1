git init
git add .
git commit -m "Initial commit with new UI and Startup fixes"
git branch -M main
gh repo create imsystemvsc/Parktoggle --public --source . --remote origin --push
if ($LASTEXITCODE -ne 0) {
    # If repo already exists, just push
    git remote add origin https://github.com/imsystemvsc/Parktoggle.git
    git push -u origin main --force
}

# Build the release
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed"
    exit 1
}

# Create a ZIP for the release
$binPath = "bin\Release\net8.0-windows"
$zipPath = "ParkToggle-v1.0.0.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path "$binPath\*" -DestinationPath $zipPath

# Create the release
gh release create v1.0.0 $zipPath -t "ParkToggle v1.0.0" -n "Initial release with updated UI, Core Parking features, and System Vital Statistics"
