# V1.5 — C# port of the V1.0 Openness scripts

C# (.NET Framework 4.8) re-implementation of the proven V1.0 PowerShell feasibility scripts,
to prove the Openness fundamentals work in C# before porting the V2 MCP host (V2.5).

Scope = the **core representative set** (one port per capability class), not all 39 scripts.
Source of truth for rules/gotchas: `../Claude - TIA - Prompts and References.md`.
Original PowerShell frozen in `../_PowerShell_backup_pre_Csharp_20260629/`.

## Project

`TiaSharp/` — one console app with a command dispatcher; each step is a subcommand.
Implemented so far (all READ-ONLY):
- **`connect`** — port of V1.0 Step 1 (attach + read project).
- **`listblocks`** — port of Step 2 (PLC block tree + total count).
- **`listhardware`** — port of Step 7 (all devices via full traversal, de-duped by name).
- **`exportblock <name> [--out <dir>]`** — port of Steps 3/4/6: export a block to SimaticML XML
  (always) + SCL source (SCL/STL blocks only; GRAPH/FBD report "not generated", not an error).
  Run it on a normal FB, a safety F-block, and a GRAPH block to prove all three.
- **`exportdevice <name> [--out <dir>]`** — port of Step 20: export a hardware device to AML via
  CaxProvider. Default output dir is `.\export`.
- **`creategroup <name|A/B/C> [confirm]`** — port of Step 8: ensure a (possibly nested) program-block
  group exists. WRITE; dry-run unless `confirm`; never saves.
- **`importblock <xmlPath> --group <name|A/B> [--as <NewName>] [--override] [confirm]`** — port of
  Step 13: import a block XML into a group (path resolved to absolute; no overwrite unless
  `--override`; imported blocks are uncompiled - compile in TIA). `--as <NewName>` rewrites the
  block's name *inside* a temp copy of the XML (source file untouched) so a copied block imports as
  a NEW, distinct block instead of colliding with the original (block names are unique per PLC).
  WRITE; dry-run unless `confirm`; never saves.
- **`clonedevice (<liveTemplateName> | --from <amlPath>) --as <NewName> --ip <ip> --pn <pnName>
  --group <targetGroup> --iobase <int> [--subnet X] [--iosystem Y] [--tempband N] [confirm]`** —
  port of the full clone recipe (Steps 20/22/23/24). Two source modes:
  - **live template** (give the device name): exports it from the open project to AML first.
  - **`--from <amlPath or partial name>`**: clone from an existing AML file (e.g. the device
    library) — no live export; the original device + group names are auto-detected from the file.
    `--from` accepts a full path **or just a fragment** of the filename, which is resolved against
    your `device_library` and `export` folders (e.g. `--from DEV01-DRV01`). If the fragment matches
    more than one file, it lists them so you can narrow it. Use **`findaml <partial>`** to search.

  Then it transforms the AML (rename device + group, set IP + PROFINET name, regenerate all GUIDs),
  imports (MoveToParkingLot), assigns IoConnectors[0] to the IO system, and sets module IO start
  addresses (two-pass from `--iobase`, ≤ ~32767). `--pn` = PROFINET device name (lowercase station
  name, unique). `--iobase` = start byte of the device's I/O image. WRITE; dry-run unless `confirm`;
  never saves. F-params are auto-assigned by TIA; F-module addressing needs the safety program
  unlocked (refusals are listed as address errors).
- **`createtags <tableName> [--group <name|A/B>] [--tag Name:Type:%Addr]... [--file <csv>] [confirm]`**
  — port of Steps 26/28 (Round C). Creates/completes a PLC tag table in the root tag-table
  group, or with `--group` inside a (nested) tag-table user group — missing group levels are
  created on confirm:
  creates the table only if missing, creates only the tags whose name isn't already present
  (a partial table is completed, not duplicated), then reads back every tag to verify.
  `--tag` is repeatable (`Name:Type:%Address`, e.g. `SENSOR_ADVANCE_1:Bool:%I16000.0`); the
  last two `:`-fields are type and address, so names with `+`/`-` are fine. `--file` takes a
  CSV (`name,type,address`; `#` comments and a header line are ignored). If a type name is
  rejected it is retried quoted, then bare (UDT quirk). Failsafe-band addresses are refused
  while safety is locked - each failure is logged individually and the run continues.
  WRITE; dry-run unless `confirm`; never saves.
- **`wirefb <xmlPath> --group <name|A/B> [--map OLD=NEW]... [confirm]`** — port of Step 29 plus
  the proven operand-repoint recipe (Round D). Repoints operands inside a *working copy* of an
  exported FB XML (source untouched), dumps the final distinct operand list for verification,
  then Override-imports the block into the group. Map forms:
  - `--map DbName:Member=GlobalTag` — a DB-member access (two `<Component>`s) becomes ONE
    global-tag component (the DB component is dropped);
  - `--map OldTag=NewTag` — single-component rename.

  Matching is whitespace-tolerant (real exports put components on separate lines); a map that
  matches nothing ABORTS before import (the silent-failure gotcha). The rewritten XML is
  validated for well-formedness and saved next to the source as `<name>.wired.xml` (also on
  dry-run, so you can inspect it). WRITE; dry-run unless `confirm`; never saves.
- **`importchain --group <name|A/B> --files <a.xml,b.xml,...> [--file <x.xml>]... [confirm]`** —
  port of Step 35, the F-WRITE GATE (Round E). Imports an ordered list of block XMLs into one
  group with Override, each file independently try/caught so a refused F-object is pinpointed
  instead of killing the run. Proven order: standard DBs -> safety DBs -> FFB -> FB. The
  SAFETY PROGRAM MUST BE UNLOCKED in TIA first (it re-locks on every TIA restart); re-running
  after unlock is safe because imports are Override. Does NOT compile. WRITE; dry-run unless
  `confirm`; never saves.
- **`compile [<blockName>] [--group <g>] [--all] [--hw [<device>]]`** — ONE compile at the
  highest level (proven live: V17 has no group-level compile service, and per-block loops pay
  the F-consistency cost once per block). Bare `compile`, `--all`, or `--group <g>` all do a
  SINGLE software-level pass over the whole PLC software (`--group` is just noted in the log);
  `compile <blockName>` compiles one block; `--hw` additionally (or alone) does ONE hardware
  compile — of the named device, else the PLC station. A hardware compile (re)generates the
  F-I/O QBAD DBs. F-consistency processing can take minutes — not a hang. Reports the compiler
  message tree + error/warning totals; exit 0 only on 0 errors. Never saves.
- **`listblocks [--group <name|A/B>]`** — the block-tree listing can now be scoped to one group.
- **`order [<file|partial>]`** (shell-only) — run a command "note" written in Notepad/Notepad++:
  save the note as a `.txt` in the `orders\` folder next to `TiaSharp.exe`, then in the shell
  type `order` (newest note), `order myjob` (newest note whose filename contains "myjob"), or
  `order <full path>`. The note's non-`#` lines are previewed; type `y`/`confirm` to run them
  top-to-bottom, with exactly the same rules as typing: a write line without `confirm` only
  dry-runs (append `confirm`, or put a bare `y` on the next line). `exit`/`order` are ignored
  inside a note; files with "example" in the name are never auto-picked. Copy-paste templates
  for every command live in `orders\Openness_order_example_list.txt` (fully commented, so
  running it does nothing).
- **`shell`** — attach to TIA **once** (single access-dialog approval), then run all the above
  commands in one live session with no re-attach and no repeated dialog. Preview of the V2.5
  host loop. Inside the prompt: `status`, `listblocks [--group <g>]`, `listhardware`,
  `exportblock <name> [--out <dir>]`, `exportdevice <name> [--out <dir>]`,
  `creategroup <name|A/B> [confirm]`, `importblock <xmlPath> --group <name|A/B> [--override] [confirm]`,
  `clonedevice ...`, `createtags ...`, `wirefb ...`, `importchain ...`, `compile ...`,
  `help`, `exit`. Writes preview first; type `confirm` (or `y`) to apply the last preview.

## Examples (generic names - substitute your own)

All writes preview first; add `confirm` (or type `confirm`/`y` in the shell) to apply.
Run from the project base folder so relative paths and the log land where you expect.

**createtags — inline tags (Round C):**

```powershell
TiaSharp.exe createtags ++ST900 `
  --tag SENSOR_ADVANCE_1:Bool:%I17000.0 `
  --tag SENSOR_RETRACT_1:Bool:%I17000.3 `
  --tag OUTPUT_ADVANCE_1:Bool:%Q17014.0 `
  --tag OUTPUT_RETRACT_1:Bool:%Q17014.3 confirm
```

**createtags — table inside a tag-table group (nested path supported):**

```powershell
# creates group "Stations" (and "Line 9" under it) if missing, then the table inside it
TiaSharp.exe createtags ++ST900 --group "Stations/Line 9" `
  --tag SENSOR_ADVANCE_1:Bool:%I17000.0 confirm
```

Notes: without `--group` the table goes to the tag-table ROOT (the station convention).
Tag-table names must be unique across the PLC - if a same-named table already exists in a
DIFFERENT group, TIA refuses the create and the error is logged; the command completes the
table only where it finds it in the TARGET group.

**createtags — from CSV (bulk):**

```powershell
TiaSharp.exe createtags ++ST900 --file .\scaffold\st900_tags.csv confirm
```

`st900_tags.csv`:

```
# name,type,address
name,type,address
SENSOR_ADVANCE_2,Bool,%I17000.1
SENSOR_ADVANCE_3,Bool,%I17000.2
++ST900+DEV01-VAL05-BG1,Bool,%I17000.6
```

(Names may contain `+`/`-`; the last two fields are always type and address.)

**createtags — telegram tags typed as a UDT (the proven drive convention):**

The UDT/FUDT is the tag's DATA TYPE, not a tag itself. Procedure:

1. The UDT must already EXIST in the project (PLC data types) — `createtags` never creates
   UDTs; a missing type fails that tag and is logged.
2. ONE tag per direction, the whole UDT as the type, at the telegram start byte `.0`:
   `<station>+<device>_IN/_OUT` (standard), `_FIN/_FOUT` (failsafe). No per-member tags —
   the UDT members map the telegram.
3. In and Out share the same start byte per module; failsafe telegram at the module base,
   standard at base+9 (SEW convention).
4. Pass the UDT name bare — it is retried as-given → `"quoted"` → bare automatically.
5. The failsafe pair LAST, with the safety program UNLOCKED (F-band tags are refused while
   locked; logged individually, the run continues).

```powershell
# standard pair
TiaSharp.exe createtags ++ST900 `
  --tag ++ST900+TD001_IN:MY_TELEG_UDT_IN:%I17109.0 `
  --tag ++ST900+TD001_OUT:MY_TELEG_UDT_OUT:%Q17109.0 confirm

# failsafe pair at the module base - safety UNLOCKED first
TiaSharp.exe createtags ++ST900 `
  --tag ++ST900+TD001_FIN:MY_TELEG_FUDT_IN:%I17100.0 `
  --tag ++ST900+TD001_FOUT:MY_TELEG_FUDT_OUT:%Q17100.0 confirm
```

**wirefb — repoint 4 hardware operands from a fake DB to real tags, then import (Round D):**

```powershell
# 1) dry-run: writes MYFIXTURE_FB.wired.xml + prints the final operand list, imports NOTHING
TiaSharp.exe wirefb .\scaffold\MYFIXTURE_FB.xml --group "90 SG90" `
  --map MYFIXTURE_TAGS_DB:SENSOR_ADVANCE_1=SENSOR_ADVANCE_1 `
  --map MYFIXTURE_TAGS_DB:SENSOR_RETRACT_1=SENSOR_RETRACT_1 `
  --map MYFIXTURE_TAGS_DB:OUTPUT_ADVANCE_1=OUTPUT_ADVANCE_1 `
  --map MYFIXTURE_TAGS_DB:OUTPUT_RETRACT_1=OUTPUT_RETRACT_1

# 2) check the operand dump, then apply + verify:
TiaSharp.exe wirefb .\scaffold\MYFIXTURE_FB.xml --group "90 SG90" --map ... confirm
TiaSharp.exe compile --group "90 SG90"
```

(`OLD` side: `DbName:Member` collapses the two-component DB access to ONE global-tag
component; a plain `OldTag=NewTag` renames a single-component operand. A map that matches
nothing aborts before import.)

**importchain — the safety chain, safety program UNLOCKED first (Round E):**

```powershell
# F-band tags first (failsafe outputs land on the PROFIsafe module):
TiaSharp.exe createtags ++ST900 --tag ++ST900+DEV01-PWR01:Bool:%Q17008.0 confirm

# then the ordered chain: standard DBs -> safety DBs -> FFB -> FB
TiaSharp.exe importchain --group "90 SG90" --files `
  "scaffold\MY_GLOB_DB.xml,scaffold\MY_STD_SDB.xml,scaffold\MY_SAFE_SDB.xml,scaffold\MY_FSDB.xml,scaffold\MY_PNEUMATICS_FFB.xml,scaffold\MYFIXTURE_FB.xml" confirm

TiaSharp.exe compile --group "90 SG90"
```

(Each file is try/caught independently - an "IMPORT FAILED" on an F-block almost always means
the safety program is still LOCKED; unlock and re-run, Override makes re-runs safe.)

**compile — three scopes:**

```powershell
TiaSharp.exe compile MYFIXTURE_FB          # one block
TiaSharp.exe compile --group "90 SG90"     # a group (DBs before code blocks)
TiaSharp.exe compile --all                 # whole PLC software
```

**listblocks — scoped to one group:**

```powershell
TiaSharp.exe listblocks --group "90 SG90"
```

**Same flow inside one attached `shell` session (single access dialog):**

```
tia> createtags ++ST900 --tag SENSOR_ADVANCE_1:Bool:%I17000.0
tia> y
tia> wirefb scaffold\MYFIXTURE_FB.xml --group "90 SG90" --map MYFIXTURE_TAGS_DB:SENSOR_ADVANCE_1=SENSOR_ADVANCE_1
tia> y
tia> compile --group "90 SG90"
tia> importchain --group "90 SG90" --files scaffold\MY_STD_SDB.xml,scaffold\MY_SAFE_SDB.xml,scaffold\MY_PNEUMATICS_FFB.xml
tia> y
tia> compile --group "90 SG90"
tia> exit
```

## Write-command safety

Write commands (`creategroup`, `importblock`, and the clone/tags/safety steps coming next) are
**dry-run by default** - they report what they *would* do and change nothing. Add `confirm` to
apply. Nothing is ever auto-saved; keep changes with Ctrl+S in TIA or just don't save to revert.
Test on a backup/test project. Safety (F) writes additionally require the safety program unlocked.

## Prerequisites (on the TIA machine, in VS Code)

- .NET SDK (any recent) + the **.NET Framework 4.8 Developer Pack** (needed to build `net48`).
- Your Windows login already in the **Siemens TIA Openness** users group.
- TIA V17 open with the test project loaded.
- If your Openness path differs from the default
  `C:\Program Files\Siemens\Automation\Portal V17\PublicAPI\V17`,
  edit the `HintPath` in `TiaSharp/TiaSharp.csproj` (and pass `--api "<path>"` at run time).

## Build

```powershell
cd "V1.5\TiaSharp"
dotnet build -c Release
```

Output exe: `V1.5\TiaSharp\bin\Release\net48\TiaSharp.exe`

## Run — Step 1 (connect)

From a folder where you want the log written (the log lands in the current directory):

```powershell
"V1.5\TiaSharp\bin\Release\net48\TiaSharp.exe" connect
```

(Or during dev: `dotnet run -c Release -- connect` from `V1.5\TiaSharp`.)

Approve the TIA "external access" dialog when it appears. Optional: `... connect --api "C:\...\PublicAPI\V20"`.

## What to check / report back

- Console output + the generated `TiaSharp_connect_log.txt`.
- It should reach **"Attached to TIA Portal OK."**, print the project name/path and top-level
  device count, then **"=== DONE OK ==="** — matching the old `TIA_Step1_log.txt`.
- If it fails, paste the `ERROR ... / LOADER ... / STACK ...` lines. The most likely first-run
  issue is the assembly resolver or the `net48` targeting pack — both are quick fixes.
    