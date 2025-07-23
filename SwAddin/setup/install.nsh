!include LogicLib.nsh

!define REG_PATH "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"

!macro customInstall
  SetOutPath "$TEMP"

  ; Check if the registry key exists
  DetailPrint "Checking SolidWorks registry key..."
  ReadRegStr $0 HKLM "SOFTWARE\SolidWorks\Security" "Serial Number"

  ; If the key exists, $0 will not be empty
  StrCmp $0 "" keyDoesNotExist continue

  keyDoesNotExist:
    MessageBox MB_OK "SolidWorks is not installed on this PC. Skipping install of Leo AI Add-in."
    Goto done

  continue:
    DetailPrint "Installing the MSI package..."
    ; Run msiexec to install the sw-addin MSI silently
    ExecWait '"msiexec" /i "$INSTDIR\resources\sw_addin_installer.msi" /qn' $0
    DetailPrint "MSI installer returned with code $0"

    ; Check if the installation was successful
    StrCmp $0 "0" done leoAddInInstallerFailed

  leoAddInInstallerFailed:
    MessageBox MB_OK "Leo AI Add-in installation failed with error code $0"
    Goto done

  done:
    DetailPrint "Done"

!macroend


!macro customUnInstall
  ; Check if the MSI file exists in the original resources folder
  IfFileExists "$INSTDIR\resources\sw_addin_installer.msi" uninstallFromResources uninstallByGuid

  uninstallFromResources:
    ; Uninstall it from original resources folder
    DetailPrint "Uninstalling from $INSTDIR"
    ExecWait '"msiexec" /x "$INSTDIR\resources\sw_addin_installer.msi" /qn' $0

    ; Check the return code from msiexec
    StrCmp $0 "0" uninstallSuccess uninstallFailed

  uninstallByGuid:
    ; Use the product code of the MSI to uninstall
    DetailPrint "Running msiexec to uninstall by Product Code..."
    ExecWait '"msiexec" /x "{D4A2D6A7-1E2F-4B9D-BB4E-33F636B5EDE4}" /qn' $0

    ; Check the return code from msiexec
    StrCmp $0 "0" uninstallSuccess uninstallFailed

  uninstallSuccess:
    DetailPrint "Uninstallation successful."
    Goto done

  uninstallFailed:
    MessageBox MB_OK "Leo AI Add-in removal failed with error code $0"
    Goto done

  done:
    DetailPrint "Uninstallation process finished."

!macroend
