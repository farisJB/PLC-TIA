---
name: plc-knowledge
description: Reference of hard-won rules and gotchas for driving Siemens TIA Portal V17 via the Openness API from PowerShell 5.1 — environment setup, the assembly resolver, reflection for generic methods, export/import rules, hardware AML quirks, safety-program unlock requirement, address-range limits, GUID regeneration, and XML-authoring dangers. Consult before any TIA Openness write so a known pitfall isn't re-hit. Triggers include questions about why an Openness call fails, how to avoid TIA crashes, or what order to do safety writes.
---

# plc-knowledge — TIA Openness gotchas (read before writing)

A checklist of pitfalls learned the hard way. Re-read the relevant entry BEFORE
re-implementing anything it covers.

## Environment & connection
- **PowerShell 5.1 only** (blue console). Not PowerShell 7, not ISE (ISE crashes with
  Openness). 5.1 matches the .NET Framework the Openness DLL targets.
- Launch: `powershell.exe -STA -ExecutionPolicy Bypass -File "<script>.ps1"`. Approve the
  TIA "external access" dialog. The Windows user must be in the **Siemens TIA Openness** group.
- **Assembly resolver must be compiled C# (`Add-Type`)**, not a PowerShell scriptblock —
  a scriptblock resolver `StackOverflow`s during `Attach()`.
- **Call generic methods by reflection** (`GetService<T>()` etc.); PS 5.1 doesn't grok
  the `.Method[Type]()` shorthand.
- Scripts don't auto-save. The user keeps work with **Ctrl+S** in TIA (or discards to revert).
  Write a step-by-step **log file** beside each script so progress survives a closed console.

## Reading the project
- **Device lookup MUST traverse all containers**: `project.Devices` +
  `project.UngroupedDevicesGroup` + `project.DeviceGroups` recursively (deduped). A
  top-level-only search silently returns "not found". (A top-level read once reported 3
  devices when the truth was 181.)
- **Verify against reality, never trust "green".** A clean run is not proof. Reconcile
  counts/names/addresses against EPLAN, manuals, the device's real modules. If a figure
  looks too small/clean/convenient, flag it and ask — don't narrate a tidy story around it.

## Export / import of blocks
- Openness exports **only consistent (compiled, error-free) blocks**. Otherwise:
  "Inconsistent blocks … cannot be exported" — compile in TIA first.
- **Never hand-author FBD or F-network XML.** A malformed detail isn't rejected — it
  **crashes TIA**. Always reduce/transform a REAL exported network; keep `<Call>` and
  `<Wires>` byte-for-byte; only rewrite `<Access>` operands.
- Validate XML well-formed (`xml.dom.minidom.parseString`) before every import.
- To place blocks in a specific group: export both to XML while refs intact → delete the
  root originals (snapshot the collection first with `@(...)` — deleting mid-enumeration
  throws "Collection modified") → import into the target group, **DB before FB**.
- After relocating, blocks are uncompiled again — recompile **DB before FB**.
- A global tag = ONE `<Component>` (Scope GlobalVariable); a DB member = TWO components.

## Hardware (AML / CaxProvider)
- `Device.Export(...)` does NOT exist in V17. Hardware export/import is **CAx /
  AutomationML** via `GetService<CaxProvider>()` **on the PROJECT** (portal/device return null).
- **Log file extension MUST be `.log`** (`.txt`/`.xml` rejected). Export won't overwrite —
  delete stale files first.
- Direct module plugging is **unsupported** (`DeviceItemComposition.Create` /
  `CreateAndPlug` all rejected). Always round-trip a real device's AML.
- AML does NOT carry failsafe addresses or PlantDesignation — read those live.
- `CaxImportOptions` = `MoveToParkingLot | OverwriteTiaDevice | RetainTiaDevice`. Use
  `MoveToParkingLot` for a new device. "subnet/group already exists" warnings are harmless.
- A CAx import lands the device **UNASSIGNED**. Assign via
  `IoConnector.ConnectToIoSystem(IoSystem)` (`Siemens.Engineering.HW`, not `.Features`);
  IO system reached via `project.Subnets[...].IoSystems[...]`. Connect connector **[0]** only.
- *** Assignment AUTO-REASSIGNS module addresses *** — set IO start addresses on the LIVE,
  ASSIGNED device, AFTER assignment, via `Address.SetAttribute("StartAddress", <int>)`.
  - Address must be **in CPU range** (S7-1500 max ~32767); out-of-range throws
    "address outside the CPU address range".
  - Walk runs in a CHILD SCOPE — use `$script:`-scoped counters or they silently don't update.
  - Set in **two passes** (park in a temp band, then finals) or you hit
    "address already being used".

## Safety (F-CPU) — critical
- **Writing F-blocks / failsafe-band tags via Openness works ONLY when the safety program
  is OPEN / logged in for editing.** Otherwise TIA refuses with "A password has been
  assigned for the safety program. For changes with Openness you have to log in to the
  safety program." **Always remind the user to unlock safety before any safety-write run.**
- **Safety blocks are never converted to SCL** (certified FBD/LAD only — IEC 61508 / TÜV).
  For safety, do structural review from XML, never logic rewrites.
- Command/standard outputs go on the **standard** output module; only safe outputs go on
  the **failsafe** band. A safety module cannot command motion by design.
- F-system data (QBAD, FSourceAddress, FDestinationAddress, etc.) is available only via
  **live read**, never from AML.
- Typed "DB of UDT" exports as `InstanceDB` with `<InstanceOfName>` + `AutoNumber=false` +
  fixed `<Number>`; to clone, rename block only, set `AutoNumber=true`, drop the `<Number>`.

## Versioning
- Targets **V17** (`PublicAPI\V17`). For V20, re-point the DLL path; core logic carries.

## Hard constraints (never break)
- **No customer / OEM / plant / project / real device names, IPs, or GSD strings** in any
  script, skill, or saved plugin file. They live in chat / the user's private project only.
- **Code comments in English** unless the user asks otherwise.
- **Ask before writing code** — discuss the approach, confirm the plan, then execute.
  Small safe tests first, scale after they pass. Keep a backup / test project for experiments.
