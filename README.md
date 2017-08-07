# Genesys Workspace Desktop Edition extension

## Screenshot
![Screenshot](https://github.com/gnaudio/jabra-call-control-extension-for-genesys-workspace-desktop-edition/blob/master/docs/genesyscallcontrol.png)

## Overview
Genesys Workspace Desktop Edition extension that integrates the physical buttons on a Jabra headset with the Genesys SIP client

## Build status
![Build status](https://gnaudio.visualstudio.com/_apis/public/build/definitions/45495ae2-8252-4d9e-a321-699be9abf508/99/badge)

## Extension
This extension supports:
-	Genesys Workspace Desktop Edition version 8.5
-	All professional Jabra headsets

## Features
Supported device features:
-	Device ringer
- Accept/reject incoming call
- Microphone mute synchronization
-	End active call
- Hold/resume

## How to test this extension
Before deploying the extension to an organization, you can test this extension from a PC with Genesys Workspace Desktop Edition version 8.5 installed:
- Close the Genesys Workspace Desktop Edition software
- Unzip the [zip file](https://github.com/gnaudio/jabra-call-control-extension-for-genesys-workspace-desktop-edition/releases) and copy the content to _C:\Program Files (x86)\GCTI\Workspace Desktop Edition_
- Start the Genesys Workspace Desktop Edition software

To remove the extension, close the Genesys Workspace Desktop Edition software and remove the files copied from the zip file.

## Deployment
### Option 1
Genesys recommend using the [ClickOnce](https://msdn.microsoft.com/en-us/library/142dbbz4(v=vs.90).aspx) technology for deploying Genesys Workspace Desktop Edition to an organization. Unzip the [zip file](https://github.com/gnaudio/jabra-call-control-extension-for-genesys-workspace-desktop-edition/releases) and copy the content to _C:\Program Files (x86)\GCTI\Workspace Desktop Edition_ to prepare and publish a new ClickOnce revision as documented here:
[https://docs.genesys.com/Documentation/IW/latest/Dep/DeploymentProcedures](https://docs.genesys.com/Documentation/IW/latest/Dep/DeploymentProcedures)
### Option 2
Use the the dedicated Windows installer (.MSI file) from a [release](https://github.com/gnaudio/jabra-call-control-extension-for-genesys-workspace-desktop-edition/releases). This installer supports mass deployment scenarios.

