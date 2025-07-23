# SolidWorks Add-in

This project is a SolidWorks add-in that integrates with LeoAI.

## Dependencies

### System Requirements
- .NET Framework 4.8
- SolidWorks 2023

### NuGet Packages
The `sw-addin` project relies on the following NuGet packages:
- `Newtonsoft.Json`
- `SolidWorks.Interop` (various packages)

These packages will be restored automatically when you build the project.

### Required Reference DLLs
The following files from `SwAddin/References/SW2023/` are required:
- SolidWorks.Interop.sldworks.dll
- SolidWorks.Interop.swcommands.dll
- SolidWorks.Interop.swconst.dll
- SolidWorks.Interop.swpublished.dll
- solidworkstools.dll

## Building the Project

To build the add-in and the installer, you will need:
- Visual Studio 2022 with the ".NET desktop development" workload installed.
- WiX Toolset v6.0.0 installed for Visual Studio 2022. This is required to build the `sw-addin-installer` project.

### Build Steps
1. Open `sw-addin.sln` in Visual Studio 2022.
2. Build the solution. This will:
   - Build the `sw-addin` project.
   - Build the `sw-addin-installer` project, which creates an MSI installer package in `sw-addin-installer/bin/`.

## Installation
You can use the pre-built MSI installer or build it yourself. Prebuilt installers are available in `./sw-addin-installer/sw_addin_installer.msi`.

1. **Close SolidWorks:** Ensure that SolidWorks is not running.
2. **Run the installer:** Navigate to the `sw-addin-installer/bin/` directory and run the `.msi` file.
3. Follow the on-screen instructions to complete the installation.
4. Start SolidWorks. The add-in should be loaded.