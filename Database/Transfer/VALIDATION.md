# Validation — 2026-10-29

Verified in the cloud workspace:

- `UsedDbExport` restores and builds with .NET SDK 10.0.403, Microsoft SMO 181.37.1, and patched `System.Security.Cryptography.Pkcs` 10.0.14: **0 warnings, 0 errors**.
- Compared `used-objects.json` against the actual `DbTable` attributes under `One.Domain/Entities`, the EF model snapshot, and `Procedures.cs`: exactly **11 business tables, 11 Identity/history tables, and 12 declared procedure names**. Every name is also represented in the inventory SQL.
- CLI help succeeds. Missing required arguments fail. A pre-existing output file is preserved and rejected before any credential prompt or database connection.
- Microsoft `TransactSql.ScriptDom` 180.140.0 parses both `01_Inventory_Online.sql` and the exporter's actual dependency-preflight SQL using SQL Server 2022 grammar, with no syntax errors.

Not verified:

- Online login, actual object existence/schema, database permissions, procedure bodies, data export, and local import for `db37048` / `db36442`. No source credentials were supplied, and no connection to either online server was attempted.
- An attempted disposable local SQL Server test was blocked by a registry image-download HTTP 403. No live SQL export/import test completed. Parser/build checks do not establish end-to-end database correctness.
- Generated per-source SQL requires runtime verification after the user exports from the real databases. Review the inventory, run with SQLCMD error stopping, verify imported row counts, and run `DBCC CHECKDB` before pointing the app at the copies.

No online application data, schema, or existing local database was changed. No environment configuration draft was edited or published.
