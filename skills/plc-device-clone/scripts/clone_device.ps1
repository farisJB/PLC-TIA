# clone_device.ps1  —  plc-tia / plc-device-clone (GENERIC TEMPLATE)
# Export a template IO device, transform, import, assign to IO system, set addresses.
# WRITES to the project (import + assign + address). Test on a backup project first.
# Run from Windows PowerShell 5.1:  powershell.exe -STA -ExecutionPolicy Bypass -File "clone_device.ps1"
#
# Prereqs: a working attach (see plc-connect/connect_read_export.ps1 for the resolver,
# Attach, Get-Service-Generic and Find-AllDevices helpers — reuse them verbatim).

# ---- PARAMETERS -------------------------------------------------------------
$TiaVersion    = "<VER>"
$WorkFolder    = "<ABSOLUTE\WORK\FOLDER>"
$TemplateName  = "<TEMPLATE_DEVICE_NAME>"      # the known-good device to clone
$NewName       = "<NEW_DEVICE_NAME>"
$NewIp         = "<NEW_IP>"                     # e.g. 10.x.x.x
$NewPnName     = "<new-profinet-name>"          # lower-case PN name
$IoBase        = <FREE_IN_RANGE_INT>            # e.g. 16000  (must be <= ~32767, free)
$TempBand      = <FREE_TEMP_INT>                # e.g. 24000  (two-pass parking band)
$SubnetName    = "<SUBNET>"                      # e.g. PN/IE_1
$IoSystemName  = "<IO_SYSTEM>"                   # e.g. PROFINET IO-System
$NewGroupName  = "<TARGET_GROUP>"               # parent group for the clone
# -----------------------------------------------------------------------------
# ... [attach + helpers from plc-connect go here] ...

# 1) EXPORT template to AML
# $cax = Get-Service-Generic $project "Siemens.Engineering.Cax.CaxProvider"
# $tmplAml = Join-Path $WorkFolder "$TemplateName.aml"; $tmplLog = Join-Path $WorkFolder "$TemplateName.log"
# if(Test-Path $tmplAml){ Remove-Item $tmplAml }
# $tmpl = (Find-AllDevices $project) | ? { $_.Name -eq $TemplateName } | select -First 1
# $cax.Export($tmpl, [IO.FileInfo]$tmplAml, [IO.FileInfo]$tmplLog)

# 2) TRANSFORM (pure text). Do this in-sandbox with Python or here with -replace.
#    - rename device / device-item / group / ProfinetDeviceName
#    - set NetworkAddress (IP)
#    - REGENERATE EVERY GUID consistently (unique across the file)
#    - validate well-formed before import
#    Save to:  $cloneAml = Join-Path $WorkFolder "$NewName.aml"

# 3) IMPORT
# $cloneLog = Join-Path $WorkFolder "$NewName.import.log"
# $opt = [Siemens.Engineering.Cax.CaxImportOptions]::MoveToParkingLot
# $cax.Import([IO.FileInfo]$cloneAml, [IO.FileInfo]$cloneLog, $opt)   # "already exists" warnings are harmless

# 4) ASSIGN to the IO system (else addresses inert)
# $ios = $project.Subnets[$SubnetName].IoSystems[$IoSystemName]
# $newDev = (Find-AllDevices $project) | ? { $_.Name -eq $NewName } | select -First 1
# foreach($item in $newDev.DeviceItems){
#     $ni = $null; try { $ni = Get-Service-Generic $item "Siemens.Engineering.HW.Features.NetworkInterface" } catch {}
#     if($ni){ $ni.IoConnectors[0].ConnectToIoSystem($ios); break }   # [0] only
# }

# 5) SET ADDRESSES on the live device — TWO PASSES, $script:-scoped
# $script:cur = $TempBand
# function Set-Pass($dev,[switch]$final,$base){
#     $script:cur = $base
#     foreach($item in $dev.DeviceItems){
#         foreach($addr in $item.Addresses){
#             $addr.SetAttribute("StartAddress", [int]$script:cur)
#             $script:cur += [math]::Ceiling($addr.Length / 8.0)
#         }
#     }
# }
# Set-Pass $newDev -base $TempBand      # pass 1: park everything in the temp band
# Set-Pass $newDev -final -base $IoBase # pass 2: set the real bases

# 6) User compiles in TIA, verifies addresses, Ctrl+S. This script does NOT save.
