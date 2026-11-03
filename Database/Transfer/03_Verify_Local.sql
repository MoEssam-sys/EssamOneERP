/* LOCAL ONLY, after both imports. Read-only checks.
   Edit the two database names if you changed the export targets.
   The imports already verify exact exported row counts before COMMIT.
*/
SET NOCOUNT ON;

SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) AS ConnectedServer,
       name, collation_name, compatibility_level, state_desc
FROM sys.databases
WHERE name IN (N'db37048_Local', N'db36442_Local');

IF DB_ID(N'db37048_Local') IS NULL OR DB_ID(N'db36442_Local') IS NULL
    THROW 51020, 'One or both local copies are missing. Check target names and import results.', 1;

DBCC CHECKDB ([db37048_Local]) WITH NO_INFOMSGS, ALL_ERRORMSGS;
DBCC CHECKDB ([db36442_Local]) WITH NO_INFOMSGS, ALL_ERRORMSGS;

/* Run 01_Inventory_Online.sql against each LOCAL copy too.
   Compare schemas, tables, procedure definitions and foreign keys with ONLINE.
   Before starting the API, ensure that Identity tables AND __EFMigrationsHistory
   are in the database selected by ConnectionStrings:UserConnection.
*/
