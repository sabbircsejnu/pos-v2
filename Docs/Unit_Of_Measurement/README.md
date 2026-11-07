# IraniMart POS v2 — UOM v1 Documentation

**Baseline:** Approved UOM decisions D01–D12 as of 2026-11-06. Batch/Lot, Expiry, FEFO (D13+) deferred.

Files:

- `01_UOM_SRS.md` — complete functional requirements, workflow outcomes, backward compatibility and acceptance criteria.
- `02_UOM_LLD.md` — proposed .NET/Angular/PostgreSQL implementation architecture, contracts, schema, precision, cost/GL reconciliations and tests.

**Recommended repository folder:** `Docs/Unit_Of_Measurement/`.

**Important:** These are implementation specifications based on approved discussion, **not an audit of the current repository**. Run the read-only compatibility audit described in both documents before coding. In particular, decimal-capable quantity schema and the accounting treatment of repacked stock must be verified; if there is no authorized value-reclassification mechanism, do not improvise GL entries.

**Existing-data compatibility:** Preserve all old stock and transactions as `pcs`; do not rewrite historical quantities or create opening balances.
