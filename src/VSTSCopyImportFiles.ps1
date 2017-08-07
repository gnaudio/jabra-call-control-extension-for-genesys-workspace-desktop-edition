# ****************************************************************************
# VSTSCopyImportFiles.ps1
# Copy import files from a public URL
# ****************************************************************************

Write-Host Executing VSTSCopyImportFiles.ps1

# Stop on errors
$ErrorActionPreference = "Stop" 

Write-Host "PSVersion = " + $PSVersionTable.PSVersion

# Expand-Archive c:\a.zip -DestinationPath c:\a

Write-Host VSTSCopyImportFiles.ps1 execution done