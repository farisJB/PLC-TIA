---
name: plc-connect
description: Connect to Siemens TIA Portal V17 via the Openness API from Windows PowerShell 5.1, then read the block tree or export blocks / hardware to XML / AML. Use this first for any TIA Openness task — it is the foundation the clone and scaffold skills build on. Triggers include attaching to a running TIA instance, listing program blocks, exporting an FB/FC/DB/GRAPH/F_FBD to XML, or exporting a hardware device to AML.
---

# plc-connect — attach, read, export

The safe foundation for all TIA Openness work. Everything here is **read/export only**;
it never modifies the project.

## Environment (must hold)

- TIA Portal **V17** (tested on Update 8). Openness DLL at
  `C:\Program Files\Siemens\Automation\Portal V<VER>\PublicAPI\V<VER>\Siemens.Engineering.dll`.
- The Windows user must be in the **Siemens TIA Openness** group.
- Run from the **blue Windows PowerShell 5.1 console** — NOT PowerShell 7, NOT ISE
  (ISE crashes with Openness). 5.1 matches the .NET Framework the DLL targets.
- Launch with: `powershell.exe -STA -ExecutionPolicy Bypass -File "<script>.ps1"`.
  Approve the TIA "external access" dialog when it appears.

## Two non-negotiable fixes (baked into every script)

1. **Assembly resolver must be compiled C# via `Add-Type`**, not a PowerShell
   scriptblock. A scriptblock resolver causes a `StackOverflowException` during
   `Attach()`.
2. **Call generic methods by reflection** (e.g. `GetService<SoftwareContainer>()`).
   PowerShell 5.1 does not understand the `.Method[Type]()` shorthand.

Every script also writes a step-by-step **log file** beside itself so progress
survives a closed console and can be read back later.

## Core procedure

1. Resolve + load `Siemens.Engineering.dll` (C# resolver).
2. `Attach()` to the running TIA process; get the open `Project`.
3. Reach the PLC software: `GetService<SoftwareContainer>()` on the PLC device item,
   then `.Software` (a `PlcSoftware`). Blocks hang off `PlcSoftware.BlockGroup`.
4. **Walk the full block tree recursively** — `.Blocks` at each group plus `.Groups`.
   Log each block with type + programming language.
5. **Export a block:** `block.Export(FileInfo(<path>.xml), ExportOptions.WithDefaults)`.
   Only **consistent (compiled, error-free)** blocks export — otherwise Openness
   throws "Inconsistent blocks … cannot be exported". Compile in TIA first.
6. **Export hardware (a device):** use the CAx / AutomationML provider, NOT
   `Device.Export` (which does not exist). Get
   `project.GetService<Siemens.Engineering.Cax.CaxProvider>()` **on the PROJECT object**
   (portal and device both return null), then
   `Export(device, FileInfo(<path>.aml), FileInfo(<path>.log))`.
   - The **log file extension MUST be `.log`** (`.txt`/`.xml` are rejected).
   - Export refuses to overwrite — delete stale output first.
   - AML carries the device + subnet + parent group, but NOT failsafe addresses or
     PlantDesignation (read those live if needed).

## Device lookup MUST traverse all containers

A device is usually NOT in `project.Devices` top level — it lives in a group.
Always search `project.Devices` + `project.UngroupedDevicesGroup` +
`project.DeviceGroups` **recursively**, deduped by name. A top-level-only search
silently returns "not found".

## Template script

See `scripts/connect_read_export.ps1` — a generic, parametrised connect + walk +
export skeleton. Fill the `<...>` placeholders (DLL version, export folder, target
block/device names) and run on a test project first.

## What "done" means

Verify against reality, not just a green run. A script finishing without error is
NOT proof the result is right — reconcile counts/names against what you already know
(e.g. EPLAN, manuals). If a number looks too small or too clean, flag it and ask.
