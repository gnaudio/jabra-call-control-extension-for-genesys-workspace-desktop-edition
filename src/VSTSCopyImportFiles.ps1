# ****************************************************************************
# VSTSCopyImportFiles.ps1
# Copy import files from a public URL
# ****************************************************************************

Write-Host Executing VSTSCopyImportFiles.ps1

# Stop on errors
$ErrorActionPreference = "Stop" 

Write-Host "PSVersion = " $PSVersionTable.PSVersion

Write-Host "DownloadUrl = " $env:DownloadUrl

Write-Host "path = " $env:BUILD_REPOSITORY_LOCALPATH 

$output = $env:BUILD_REPOSITORY_LOCALPATH  + "\src\JabraCallControlExtension\Import\Import.zip";
$outputFolder = $env:BUILD_REPOSITORY_LOCALPATH  + "\src\JabraCallControlExtension\Import";

Invoke-WebRequest -Uri $env:DownloadUrl -OutFile $output

Expand-Archive $output -DestinationPath $outputFolder

Write-Host VSTSCopyImportFiles.ps1 execution done