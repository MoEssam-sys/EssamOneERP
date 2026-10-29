/* Run in SSMS, connected to EACH ONLINE application database.
   Read-only: no application tables/procedures are created or changed.
   Inventory from repository HEAD ecd5c581019d68c186cb1399ef0129940b6d0c97.
   A name in the C# model does not prove the table exists in this database.
   Unqualified C# table names do not prove the schema is dbo.
*/
SET NOCOUNT ON;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 51000, 'Select an application database in SSMS first.', 1;

IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'VIEW DEFINITION'), 0) <> 1
    THROW 51001, 'Database VIEW DEFINITION permission is required for a complete inventory. Do not treat hidden objects as missing.', 1;

SELECT DB_NAME() AS DatabaseName,
       CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) AS ServerName,
       CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS SqlVersion,
       DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS DatabaseCollation;

DECLARE @Tables TABLE
(
    TableName sysname PRIMARY KEY,
    ConnectionGroup nvarchar(40) NOT NULL,
    CodeUsage nvarchar(100) NOT NULL
);
INSERT @Tables VALUES
(N'Branches', N'MainConnection / LiveConnection', N'Active repository + API'),
(N'Brands', N'MainConnection / LiveConnection', N'Active repository + API'),
(N'Warehouses', N'MainConnection / LiveConnection', N'Active repository + API'),
(N'Suppliers', N'MainConnection / LiveConnection', N'Active repository + API'),
(N'Customers', N'MainConnection / LiveConnection', N'Entity / DTO only'),
(N'Products', N'MainConnection / LiveConnection', N'Entity / DTO only'),
(N'ProductUnits', N'MainConnection / LiveConnection', N'Entity / DTO only'),
(N'Categories', N'MainConnection / LiveConnection', N'Entity / DTO only'),
(N'Units', N'MainConnection / LiveConnection', N'Entity / DTO only'),
(N'SalesInvoices', N'MainConnection / LiveConnection', N'Entity / DTO; UI currently uses sample data'),
(N'SalesInvoiceDetails', N'MainConnection / LiveConnection', N'Entity / DTO; no invoice API'),
(N'AspNetUsers', N'UserConnection', N'EF Core / Identity'),
(N'AspNetRoles', N'UserConnection', N'EF Core / Identity'),
(N'AspNetUserRoles', N'UserConnection', N'EF Core / Identity'),
(N'AspNetUserClaims', N'UserConnection', N'EF Core / Identity'),
(N'AspNetUserLogins', N'UserConnection', N'EF Core / Identity'),
(N'AspNetUserTokens', N'UserConnection', N'EF Core / Identity'),
(N'AspNetRoleClaims', N'UserConnection', N'EF Core / Identity'),
(N'Companies', N'UserConnection', N'EF Core DbSet'),
(N'OtpChallenges', N'UserConnection', N'EF Core DbSet'),
(N'AppLogs', N'UserConnection', N'EF Core DbSet'),
(N'__EFMigrationsHistory', N'UserConnection', N'EF migration bookkeeping; preserve on copy'),
(N'TestCustomers', N'Examples only', N'Attribute examples; no runtime repository'),
(N'TestOrders', N'Examples only', N'Attribute examples; no runtime repository');

-- Result 1: expected names and actual schemas. Multiple schemas produce multiple rows.
SELECT e.ConnectionGroup, e.TableName, e.CodeUsage,
       s.name AS ActualSchema, t.object_id,
       CASE WHEN t.object_id IS NULL THEN N'Not found in selected database'
            ELSE N'Present' END AS ObjectStatus
FROM @Tables AS e
LEFT JOIN sys.tables AS t ON t.name = e.TableName AND t.is_ms_shipped = 0
LEFT JOIN sys.schemas AS s ON s.schema_id = t.schema_id
ORDER BY e.ConnectionGroup, e.TableName, s.name;

-- Result 2: actual tables matching the code list only.
SELECT s.name AS SchemaName, t.name AS TableName,
       SUM(CASE WHEN p.index_id IN (0, 1) THEN p.rows ELSE 0 END) AS ApproximateRows,
       t.temporal_type_desc, t.is_memory_optimized
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
LEFT JOIN sys.partitions AS p ON p.object_id = t.object_id
WHERE t.is_ms_shipped = 0 AND EXISTS (SELECT 1 FROM @Tables e WHERE e.TableName = t.name)
GROUP BY s.name, t.name, t.temporal_type_desc, t.is_memory_optimized
ORDER BY s.name, t.name;

DECLARE @DeclaredProcedures TABLE (ProcedureName sysname PRIMARY KEY, CodeUsage nvarchar(100));
INSERT @DeclaredProcedures VALUES
(N'Brand_Insert', N'Constant only; no runtime call found'),
(N'Brand_Update', N'Constant only; no runtime call found'),
(N'Brand_Delete', N'Constant only; no runtime call found'),
(N'Brand_Active', N'Constant only; no runtime call found'),
(N'Brand_GetById', N'Constant only; no runtime call found'),
(N'Brand_GetAll', N'Constant only; no runtime call found'),
(N'Test_Insert', N'Test constant only; no runtime call found'),
(N'Test_Update', N'Test constant only; no runtime call found'),
(N'Test_Delete', N'Test constant only; no runtime call found'),
(N'Test_Active', N'Test constant only; no runtime call found'),
(N'Test_GetById', N'Test constant only; no runtime call found'),
(N'Test_GetAll', N'Test constant only; no runtime call found');

-- Result 3: declared names versus actual stored procedures.
SELECT e.ProcedureName, e.CodeUsage, s.name AS ActualSchema,
       CASE WHEN p.object_id IS NULL THEN N'Not found in selected database'
            ELSE N'Present; this does not establish application usage' END AS ObjectStatus
FROM @DeclaredProcedures AS e
LEFT JOIN sys.procedures AS p ON p.name = e.ProcedureName AND p.is_ms_shipped = 0
LEFT JOIN sys.schemas AS s ON s.schema_id = p.schema_id
ORDER BY e.ProcedureName, s.name;

-- Result 4: code-named procedures only and their available definitions.
-- Encrypted/CLR procedures may have no T-SQL definition and cannot be copied as T-SQL.
SELECT s.name AS SchemaName, p.name AS ProcedureName, p.type_desc,
       m.uses_ansi_nulls, m.uses_quoted_identifier,
       OBJECTPROPERTYEX(p.object_id, 'IsEncrypted') AS IsEncrypted,
       m.definition AS ProcedureDefinition
FROM sys.procedures AS p
JOIN sys.schemas AS s ON s.schema_id = p.schema_id
LEFT JOIN sys.sql_modules AS m ON m.object_id = p.object_id
WHERE p.is_ms_shipped = 0 AND EXISTS (SELECT 1 FROM @DeclaredProcedures e WHERE e.ProcedureName = p.name)
ORDER BY s.name, p.name;

-- Result 5: foreign keys, including references to extra tables.
SELECT fk.name AS ForeignKeyName,
       QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id)) AS ChildTable,
       COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ChildColumn,
       QUOTENAME(OBJECT_SCHEMA_NAME(fk.referenced_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.referenced_object_id)) AS ParentTable,
       COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ParentColumn
FROM sys.foreign_keys AS fk
JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
WHERE EXISTS (SELECT 1 FROM @Tables e WHERE e.TableName = OBJECT_NAME(fk.parent_object_id))
ORDER BY ChildTable, fk.name, fkc.constraint_column_id;

-- Result 6: recorded dependencies. Dynamic SQL can have dependencies absent here.
-- References to another database/server require separate review/copy.
SELECT OBJECT_SCHEMA_NAME(d.referencing_id) AS ReferencingSchema,
       OBJECT_NAME(d.referencing_id) AS ReferencingObject,
       d.referenced_server_name, d.referenced_database_name,
       d.referenced_schema_name, d.referenced_entity_name,
       d.is_caller_dependent, d.is_ambiguous
FROM sys.sql_expression_dependencies AS d
WHERE EXISTS (SELECT 1 FROM @Tables e WHERE e.TableName = OBJECT_NAME(d.referencing_id))
   OR EXISTS (SELECT 1 FROM @DeclaredProcedures e WHERE e.ProcedureName = OBJECT_NAME(d.referencing_id))
   OR EXISTS
      (SELECT 1 FROM sys.objects child JOIN @Tables e ON e.TableName = OBJECT_NAME(child.parent_object_id)
       WHERE child.object_id = d.referencing_id)
ORDER BY ReferencingSchema, ReferencingObject;
