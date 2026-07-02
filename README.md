# plc-tia (v1.5 — TiaSharp C# console tool + skill docs)

A reusable toolkit for driving **Siemens TIA Portal V17** through the **Openness API**.
It packages proven recipes — connect/read/export, hardware device cloning, tag creation,
FB operand rewiring, and standard + safety (F) block-chain imports — so a fresh session
can pick up without re-deriving anything.

## v1.5 — what changed vs v1.0

v1.0 proved every recipe as individual **PowerShell 5.1** scripts. v1.5 re-implements the
whole proven set as ONE **C# (.NET Framework 4.8) console app — `tools/TiaSharp/`** — because
Openness is a .NET API and C# is its first-class language: the assembly resolver is a normal
`AssemblyResolve` handler, `GetService<T>()` is native (no reflection dance), and everything
is compile-time checked. All commands were re-proven live against a real V17 project,
including the full safety-gate flow (F-DB + FFB imports with the safety program unlocked).

| v1.0 (PowerShell scripts) | v1.5 (TiaSharp commands) |
|---|---|
| Step 1 connect | `connect` |
| Step 2 / 7 list blocks / hardware | `listblocks [--group]`, `listhardware` |
| Steps 3/4/6 export blocks | `exportblock` |
| Step 20 export device AML | `exportdevice`, `findaml` |
| Step 8 create group | `creategroup` |
| Step 13 import block | `importblock [--as] [--override]` |
| Steps 20/22/23/24 device clone | `clonedevice` (one command, live or from library AML) |
| Steps 26/28 create tags | `createtags` (inline / CSV, UDT types, tag-table groups) |
| Step 29 wire FB | `wirefb` (operand repoint + Override import + verify dump) |
| Step 35 safety chain | `importchain` (ordered, per-file try/catch, the F-gate) |
| — | `compile` (ONE software-level pass; `--hw` for hardware) |
| — | `shell` (attach ONCE, no repeated access dialogs) |
| — | `order` (run command notes written in Notepad++, preview + confirm) |

Build & full command reference: **`tools/TiaSharp/README.md`**.
The v1.0 PowerShell scripts remain the line-by-line reference implementation inside the
skills; the recipes and gotchas are identical — only the engine changed.

## What's inside

| Component | Purpose |
|---|---|
| `tools/TiaSharp` | The v1.5 C# console tool — every proven recipe as a subcommand; dry-run/confirm writes; persistent `shell`; `order` notes. |
| `skills/plc-connect` | Attach to TIA, read the block tree, export blocks / hardware. The foundation. |
| `skills/plc-device-clone` | Clone an IO device (AML export → transform → import → assign → set addresses), parametrised by name / IP / PROFINET-name / IO base. |
| `skills/plc-fb-scaffold` | Build a standard FB + safety FFB + FSEQ bridge + tag chain by reducing real exported blocks. |
| `skills/plc-knowledge` | Reference of all hard-won gotchas (absolute paths for Export AND Import, `.log` extension, safety unlock + re-lock on restart, address ranges, GUID regen, host contention, etc.). |

## Safety & write rules (unchanged, non-negotiable)

- **Writes are dry-run by default** — add `confirm` to apply. **Nothing is ever auto-saved**:
  Ctrl+S in TIA keeps a change, closing without saving reverts it.
- **Test on a backup / test project first**, never production.
- **Safety (F) work requires the safety program OPEN / unlocked** before any F-block or
  failsafe-band write — and safety **re-locks on every TIA restart**.
- **No plant/customer/device identifiers** are stored in this repo by design. All real
  names, IPs, GSD strings and addresses are runtime parameters.
- Safety blocks are never converted to SCL and F-logic is never authored — only certified
  blocks are cloned/reduced, and the user validates every reduced F-block in TIA.

## Intended workflow for a new station

1. User provides the **station list** (devices, IPs, PROFINET names, IO bases).
2. `TiaSharp shell` attaches once; `clonedevice` builds the IO devices from templates.
3. Hardware compile (`compile --hw`) generates the F-I/O (QBAD) system DBs.
4. `createtags` creates the station tag table (telegram tags typed with their UDT/FUDT).
5. Export a proven template chain, reduce/rename it file-side, `importchain` it through
   the safety gate, `wirefb` the FB onto the real tags.
6. `compile` — ONE software pass — verify 0 errors against reality, review reduced
   F-blocks in TIA, then Ctrl+S.

Targets **V17** (`PublicAPI\V17`). For other versions, pass `--api` / edit the csproj
`HintPath`; the core logic carries.
