---
name: plc-device-clone
description: Clone a PROFINET IO device in Siemens TIA Portal V17 via Openness — export a template device to AML, transform it (new name / IP / PROFINET-name / IO base / fresh GUIDs), import it, assign it to the PLC's IO system, and set IO start addresses. Use when adding one or many IO devices to a project from a proven template. Triggers include cloning a CPX / drive / coupler device, bulk-adding devices from a CSV, or building a station's IO from a device library.
---

# plc-device-clone — export → transform → import → assign → address

Clones a known-good IO device. Requires `plc-connect` for the attach/export plumbing.
Direct module plugging via the API is **unsupported in V17** — always round-trip a
real device's AML instead of authoring hardware from scratch.

## The proven 6-step recipe

1. **Export the template device to AML.** `GetService<CaxProvider>()` on the PROJECT →
   `Export(device, fileInfo.aml, logInfo.log)`. Log file MUST be `.log`. Delete stale
   output first (no overwrite). Use the full-traversal device lookup from `plc-connect`.

2. **Transform the AML (pure text edit).** Rename the device + device-item + parent
   group + `ProfinetDeviceName`; set `NetworkAddress` (IP); and **regenerate every GUID
   consistently** (all of them, kept internally consistent) so the clone is a distinct
   object. Validate well-formed (`xml.dom.minidom.parseString`) before import.
   - You MAY offset the `StartAddress` values here, but it's optional — see step 5,
     they get overwritten on assignment anyway.

3. **Import.** `CaxProvider.Import(aml, log.log, CaxImportOptions.MoveToParkingLot)`.
   The enum is `MoveToParkingLot | OverwriteTiaDevice | RetainTiaDevice` — use
   `MoveToParkingLot` for a new device. "subnet/group already exists" warnings are
   HARMLESS. Import drops the device onto the network **UNASSIGNED**.

4. **Assign to the PLC** (required — addresses are inert until assigned).
   API: `IoConnector.ConnectToIoSystem(IoSystem)`. The types `IoConnector` /
   `IoController` / `IoSystem` live in `Siemens.Engineering.HW` (NOT `.Features`).
   Reach the IO system via the subnet:
   `project.Subnets["<SUBNET>"].IoSystems["<IO_SYSTEM>"]`. The device's PN-IO interface
   (`GetService<NetworkInterface>()`) typically exposes 2 IoConnectors — connect
   **[0] only** ([1] is the shared-device slot, leave it).

5. **Set IO start addresses AFTER assignment.** *** ORDER GOTCHA: assignment
   AUTO-REASSIGNS module addresses to the next free slots, overwriting whatever the AML
   set. *** So set addresses on the LIVE, ASSIGNED device: walk the device items, and on
   each `Address` object call `SetAttribute("StartAddress", <int>)`.
   - *** RANGE GOTCHA: the address must be inside the CPU range (S7-1500 max ~32767).
     A base like 60000 throws "address outside the CPU address range". Pick a free base
     ≤ ~32767. ***
   - *** Two more set-gotchas: (a) the walk runs in a CHILD SCOPE — counters/lists must
     be `$script:`-scoped or they silently don't update; (b) set in TWO PASSES — first
     park all IO in a free temp band, then set the finals — else a target still occupied
     by another module throws "address already being used". ***

6. **User compiles in TIA**, verifies 0 errors + the intended addresses, saves (Ctrl+S).
   Scripts never auto-save.

## Bulk rollout (30+ devices) — design

Discovery is one-time (API + AML layout are known). For volume:
- **Attach once** for the whole batch (attach ≈ 15–20 s; never per device).
- **Build ONE combined AML** with all devices (each replica with its own name / IP /
  PN-name / group AND its own fresh GUIDs — unique across the whole file), import in a
  **single `CaxProvider.Import` call** (per-device import re-parses each time).
- After the single import, **loop the fast API ops per device**: assign IoConnectors[0],
  then set addresses (two-pass, `$script:`-scoped).
- **Compile + save once** at the end.
- Per-device input = a **CSV/Excel the user supplies**: device name, IP, PROFINET name,
  IO base (and group if it varies).
- **MANDATORY GATE: prove the multi-device AML on 2 devices first** (0 errors, correct
  names/IPs/addresses) before trusting it for 30. Multi-device CaxProvider behaviour is
  assumed, not yet verified.

## Template script

`scripts/clone_device.ps1` — generic single-device clone (export → transform → import →
assign → address) driven by `<PLACEHOLDER>` parameters. Extend to a CSV loop only after
the 2-device gate passes.
