# Validation — 2026-11-02

Reviewed against repository commit `fa9202aa16e57e20e247d31bb7b26692a0329edf` and the user's specified local SQL Server 2022 target.

Completed in this session:

- Extracted and reviewed the supplied `OneERP-DbTransfer.zip`; updated its documentation, inventory, CLI argument checks and generated import version guard. Added local-instance detection, a two-source PowerShell export wrapper, local validation SQL, an Arabic source audit and a CSV with source file/line evidence.
- Compared the manifest with the current `DbTable` entity attributes, Identity EF model snapshot, example attributes and `Procedures.cs`: **11 business tables + 11 Identity/history tables, 12 procedure-name constants and 2 example tables**. All matched. The 22 base tables exclude the examples.
- Searched the current C# sources for procedure constant references, procedure names, `CommandType.StoredProcedure`, SQL execution calls and `DapperParamBuilder` callers. The procedure helper has no external callers in this source; current repositories generate direct SQL. This is static evidence, not a runtime trace or proof about server-side triggers.
- Built `UsedDbExport` in Release with .NET SDK 10.0.403 / Microsoft SMO 181.37.1: **0 errors**. Restore/build reported **NU1900** because NuGet's package vulnerability service could not be reached; the cached packages resolved and the compiler build passed. A current online vulnerability audit was not completed.
- CLI checks passed for help, missing required options, unknown options, existing-file preservation, refusing system database targets and unsupported SQL versions. Invalid cases stopped before prompting for credentials or connecting to a source.
- Microsoft `TransactSql.ScriptDom` 180.140.0 with the SQL Server 2022 parser (`TSql160Parser`) parsed `00_Local_Server.sql`, `01_Inventory_Online.sql`, `03_Verify_Local.sql` and the dependency-preflight SQL extracted from the actual exporter source: **0 syntax errors**.

Not executed or established:

- No ONLINE connection, login, actual object/schema existence, row read, procedure definition extraction or export. No configured ONLINE credentials or external SQL TCP access are available in this session.
- No actual SQL Server export/import or `DBCC CHECKDB` run. No PowerShell runtime execution of `02_Export-Local2022.ps1`; its source and arguments were reviewed, and its underlying .NET CLI was built and checked.
- The local instance's exact server name is not known from the user's description “Local”; run `00_Local_Server.sql` on their machine. Source host/database names originate from the supplied transfer archive; `AI_HANDOFF.md` historically identifies `db37048` as UsersDb, but the current ONLINE connection mapping is not confirmed.
- The final per-source schema/data `.sql` files cannot be produced without running the exporter against the real sources. Parser/build checks do not establish data-copy correctness, feature compatibility, complete dynamic-SQL dependencies or cross-source consistency.

Before switching the app: pause source writes across both exports, review both inventories, import with SQLCMD error stopping on the exact local instance, compare actual source/local objects and exported row counts, run the supplied local integrity checks, then set all three connection keys. The API runs EF migrations/seeding at startup, so review the imported Identity history first.

No ONLINE application data, schema or existing local database was changed. No environment configuration was edited or published.
