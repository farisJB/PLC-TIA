# scaffold_notes.md — plc-fb-scaffold build checklist (GENERIC)

The block XML for the FB / FFB / bridge / DBs is **not stored in this plugin** — it is
generated at build time by reducing the user's own real exported blocks (library- and
device-specific, and may contain real identifiers). This file is the procedure only.

## Inputs needed from the user / project
- The real standard FB to reduce (e.g. a device control FB) — exported XML.
- The real safety FFB container to reduce — exported XML.
- The FSEQ exchange UDTs (STD->FS / FS->STD) and the bridge UDT name.
- The standard + safety global UDTs (GLOB / SAFE_GLOB) and their instance DBs.
- The device's tag list: channel -> %I/%Q address, standard vs failsafe band.

## Order of operations
1. Confirm the **safety program is OPEN / unlocked** in TIA (required for any F-write).
2. Create / update the **tag table** (idempotent). Command outputs -> standard band.
3. Reduce the standard FB to one instance, repoint operands to the new tags/DBs.
4. Reduce the safety FFB to one instance, repoint releases / QBAD / safe outputs.
5. Build the **FSEQ bridge** (standard DB, member typed as the whole FSEQ struct).
6. Build / rename the support DBs (GLOB, SAFE_GLOB, diag, counters, SEQ).
7. Validate every XML well-formed; dump operand lists to verify the repoints.
8. Import order: standard DBs -> safety DBs -> FFB -> FB (Override). Recompile DB->FB.
9. User compiles in TIA, verifies 0 errors + correct tag<->address<->FB mapping, Ctrl+S.

## Verify against reality
Green compile is necessary but not sufficient. Re-read the imported operands and confirm
the tag -> address -> module -> FFB mapping matches the device's actual modules.
