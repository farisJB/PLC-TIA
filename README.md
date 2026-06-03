# plc-tia plugin (v0.1.0 — documented skeleton)

A reusable toolkit for driving **Siemens TIA Portal V17** through the **Openness API**
from **Windows PowerShell 5.1**. It packages proven recipes — connect/read/export,
hardware device cloning, and standard + safety (F) block scaffolding — so a fresh
session can pick up without re-deriving anything.

## What's inside

| Skill | Purpose |
|---|---|
| `plc-connect` | Attach to TIA, read the block tree, export blocks / hardware. The foundation every other skill depends on. |
| `plc-device-clone` | Clone an IO device (AML export → transform → import → assign → set addresses), parametrised by name / IP / PROFINET-name / IO base. |
| `plc-fb-scaffold` | Build a standard FB + safety FFB + FSEQ bridge + tag chain by reducing real exported blocks. |
| `plc-knowledge` | Reference of all hard-won gotchas (PowerShell 5.1, `.log` extension, safety unlock, address ranges, GUID regen, etc.). |

## Status & limitations

- **Skeleton, not turnkey.** Scripts are generic templates with `<PLACEHOLDER>`
  parameters. They must be filled in and **tested against a real project** before
  any write/import run. Tune on a test project with a backup, never production first.
- **No plant/customer/device identifiers** are stored here by design. You supply real
  names, IPs, GSD strings, and addresses at run time (ideally from a CSV/Excel).
- **Safety (F) work requires the safety program to be OPEN / logged in for editing**
  before any F-block or failsafe-band write. See `plc-knowledge`.
- Targets **V17** (`PublicAPI\V17`). For V20, re-point the DLL path; core logic carries.

## Intended workflow for a new project

1. User provides the **CPU/PLC type** and a **station list** (devices, IPs, IO bases).
2. `plc-connect` attaches and confirms the environment.
3. `plc-device-clone` builds the CPU station + IO devices from templates.
4. `plc-fb-scaffold` adds tags + standard/safety FB chains per device.
5. User compiles in TIA, verifies 0 errors against reality, and saves (Ctrl+S).
