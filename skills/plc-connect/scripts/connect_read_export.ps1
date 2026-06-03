# connect_read_export.ps1  —  plc-tia / plc-connect (GENERIC TEMPLATE)
# Attach to a running TIA Portal V<VER>, walk the block tree, optionally export.
# READ-ONLY. Fill <PLACEHOLDERS>. Run from Windows PowerShell 5.1:
#   powershell.exe -STA -ExecutionPolicy Bypass -File "connect_read_export.ps1"

# ---- PARAMETERS (edit these) ------------------------------------------------
$TiaVersion   = "<VER>"                                  # e.g. 17
$ExportFolder = "<ABSOLUTE\EXPORT\FOLDER>"               # where XML/AML go
$LogFile      = Join-Path $ExportFolder "connect_log.txt"
$DllPath      = "C:\Program Files\Siemens\Automation\Portal V$TiaVersion\PublicAPI\V$TiaVersion\Siemens.Engineering.dll"
# -----------------------------------------------------------------------------

function Log($m){ $line="{0}  {1}" -f (Get-Date -Format "HH:mm:ss"), $m; $line; Add-Content -Path $LogFile -Value $line }
if(-not (Test-Path $ExportFolder)){ New-Item -ItemType Directory -Path $ExportFolder | Out-Null }
"" | Set-Content $LogFile
Log "START connect_read_export"

# 1) Compiled C# assembly resolver (a scriptblock resolver StackOverflows on Attach)
$resolverSrc = @"
using System;
using System.Reflection;
public static class TiaResolver {
    public static string DllPath;
    public static Assembly Resolve(object s, ResolveEventArgs a){
        if(a.Name.StartsWith("Siemens.Engineering")) return Assembly.LoadFrom(DllPath);
        return null;
    }
    public static void Hook(string p){ DllPath=p; AppDomain.CurrentDomain.AssemblyResolve += Resolve; }
}
"@
Add-Type -TypeDefinition $resolverSrc -Language CSharp
[TiaResolver]::Hook($DllPath)
[Reflection.Assembly]::LoadFrom($DllPath) | Out-Null
Log "DLL loaded: $DllPath"

# 2) Attach to a RUNNING TIA instance (approve the external-access dialog)
$procs = [Siemens.Engineering.TiaPortal]::GetProcesses()
if($procs.Count -eq 0){ Log "No running TIA Portal found. Open the project first."; return }
$portal  = $procs[0].Attach()
$project = $portal.Projects[0]
Log "Attached. Project: $($project.Name)"

# 3) Reach the PLC software (generic GetService<T> via reflection)
function Get-Service-Generic($obj,$typeName){
    $svcType = [Siemens.Engineering.HW.Features.SoftwareContainer].Assembly.GetType($typeName)
    $m = $obj.GetType().GetMethod("GetService").MakeGenericMethod($svcType)
    return $m.Invoke($obj,$null)
}

function Find-AllDevices($project){
    $list=@(); $seen=@{}
    function Add($d){ if($d -and -not $seen.ContainsKey($d.Name)){ $seen[$d.Name]=$true; $script:list+=$d } }
    foreach($d in $project.Devices){ Add $d }
    foreach($d in $project.UngroupedDevicesGroup.Devices){ Add $d }
    function Walk($grp){ foreach($d in $grp.Devices){ Add $d }; foreach($g in $grp.Groups){ Walk $g } }
    foreach($g in $project.DeviceGroups){ Walk $g }
    return $list
}
$script:list=@()
$devices = Find-AllDevices $project
Log "Devices found (full traversal): $($devices.Count)"

# 4) Walk the block tree of the first PLC software found
function Walk-Blocks($group,$indent){
    foreach($b in $group.Blocks){ Log ("{0}{1}  [{2}/{3}]" -f $indent,$b.Name,$b.GetType().Name,$b.ProgrammingLanguage) }
    foreach($g in $group.Groups){ Log ("{0}<group {1}>" -f $indent,$g.Name); Walk-Blocks $g ($indent+"  ") }
}
foreach($dev in $devices){
    foreach($item in $dev.DeviceItems){
        $sc = $null
        try { $sc = Get-Service-Generic $item "Siemens.Engineering.HW.Features.SoftwareContainer" } catch {}
        if($sc -and $sc.Software -is [Siemens.Engineering.SW.PlcSoftware]){
            $plc=$sc.Software; Log "PLC software: $($plc.Name)"
            Walk-Blocks $plc.BlockGroup "  "
            # --- OPTIONAL EXPORT (uncomment + set names) -----------------
            # $blk = $plc.BlockGroup.Blocks["<BLOCK_NAME>"]
            # $blk.Export([IO.FileInfo](Join-Path $ExportFolder "<BLOCK_NAME>.xml"), [Siemens.Engineering.ExportOptions]::WithDefaults)
            break
        }
    }
}

# 5) OPTIONAL hardware (AML) export via CaxProvider on the PROJECT
# $cax = (Get-Service-Generic $project "Siemens.Engineering.Cax.CaxProvider")
# $aml = Join-Path $ExportFolder "<DEVICE>.aml"; $clog = Join-Path $ExportFolder "<DEVICE>.log"
# if(Test-Path $aml){ Remove-Item $aml }            # Export refuses to overwrite
# $dev = $devices | Where-Object { $_.Name -eq "<DEVICE_NAME>" } | Select-Object -First 1
# $cax.Export($dev, [IO.FileInfo]$aml, [IO.FileInfo]$clog)   # log MUST be .log

Log "DONE. This script made NO changes to the project."
