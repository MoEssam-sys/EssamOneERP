using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using Microsoft.SqlServer.Management.Smo;

// Source access is read-only. This program generates SQL; it never imports it.
try
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("Usage: dotnet run --project Database/Transfer/UsedDbExport -- " +
            "--server HOST --database DB --target NEW_LOCAL_DB --local-server EXACT_SERVER_NAME " +
            "--target-major 16 --output FILE.sql [--schema dbo] [--include-examples]");
        Console.WriteLine("target-major: 13=2016, 14=2017, 15=2019, 16=2022, 17=2025.");
        Console.WriteLine("SQL username and password are prompted; do not put credentials in arguments.");
        return 0;
    }

    var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    bool includeExamples = false;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--include-examples") { includeExamples = true; continue; }
        if (!args[i].StartsWith("--") || i + 1 >= args.Length)
            throw new ArgumentException("Each option requires a value; see --help.");
        flags.Add(args[i], args[++i]);
    }
    string Need(string key) => flags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException($"Missing {key}; see --help.");
    string sourceServer = Need("--server"), sourceDatabase = Need("--database");
    string targetDatabase = Need("--target"), localServer = Need("--local-server");
    int major = int.Parse(Need("--target-major"), CultureInfo.InvariantCulture);
    if (major is < 13 or > 17) throw new ArgumentException("Supported local SQL versions: 13 through 17.");
    if (new[] { "master", "model", "msdb", "tempdb" }.Contains(targetDatabase, StringComparer.OrdinalIgnoreCase))
        throw new ArgumentException("The target must be a new application database.");
    if (targetDatabase.Length > 128 || localServer.Length > 128)
        throw new ArgumentException("SQL database/server names cannot exceed 128 characters.");
    string? schema = flags.GetValueOrDefault("--schema");
    string output = Path.GetFullPath(Need("--output"));
    string report = output + ".inventory.json";
    if (File.Exists(output) || File.Exists(report))
        throw new IOException("Output or inventory report exists. Choose new filenames; nothing is overwritten.");
    string directory = Path.GetDirectoryName(output)!;
    if (!Directory.Exists(directory)) throw new IOException("Create the output directory first.");

    var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "used-objects.json")))!;
    var tableNames = manifest.BusinessTables.Concat(manifest.IdentityTables)
        .Concat(includeExamples ? manifest.ExampleTables : []).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var procNames = manifest.DeclaredProcedures
        .Where(n => includeExamples || !n.StartsWith("Test_", StringComparison.OrdinalIgnoreCase))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    Console.WriteLine($"Source: {sourceServer} / {sourceDatabase}. Pause application writes for a consistent export.");
    Console.Write("SQL username: ");
    string username = Console.ReadLine()?.Trim() ?? "";
    if (username.Length == 0) throw new ArgumentException("A SQL username is required.");
    using var password = ReadPassword();
    var connection = new ServerConnection(sourceServer, username, password)
    {
        DatabaseName = sourceDatabase,
        EncryptConnection = true,
        TrustServerCertificate = false,
        StatementTimeout = 0
    };
    try
    {
        var server = new Server(connection);
        var database = server.Databases[sourceDatabase]
            ?? throw new InvalidOperationException("Source database not found or not visible to this login.");
        object? Scalar(string sql) => connection.ExecuteScalar($"USE {Identifier(sourceDatabase)};\n{sql}");
        System.Data.DataTable Query(string sql) => connection.ExecuteWithResults($"USE {Identifier(sourceDatabase)};\n{sql}").Tables[0];
        if (Scalar("SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION')")?.ToString() != "1")
            throw new InvalidOperationException("Database VIEW DEFINITION is required. Hidden objects cannot be safely treated as absent.");

        var tables = database.Tables.Cast<Table>()
            .Where(t => !t.IsSystemObject && tableNames.Contains(t.Name) && (schema is null || t.Schema == schema))
            .OrderBy(t => t.Schema).ThenBy(t => t.Name).ToArray();
        var procedures = database.StoredProcedures.Cast<StoredProcedure>()
            .Where(p => !p.IsSystemObject && procNames.Contains(p.Name) && (schema is null || p.Schema == schema))
            .OrderBy(p => p.Schema).ThenBy(p => p.Name).ToArray();
        if (tables.Length == 0) throw new InvalidOperationException("No code-listed tables were found. Check the database and schema.");
        if (tables.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)
            || procedures.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Duplicate object names across schemas. Select the application's actual schema with --schema.");

        var ids = tables.Select(t => t.ID).Concat(procedures.Select(p => p.ID)).ToHashSet();
        var triggers = tables.SelectMany(t => t.Triggers.Cast<Trigger>()).ToArray();
        var scope = ids.Concat(triggers.Select(t => t.ID)).ToHashSet();
        string tableIds = string.Join(",", tables.Select(t => t.ID));
        var children = Query($"SELECT object_id FROM sys.objects WHERE parent_object_id IN ({tableIds})");
        foreach (System.Data.DataRow row in children.Rows) scope.Add(Convert.ToInt32(row[0], CultureInfo.InvariantCulture));
        string scopeIds = string.Join(",", scope);
        string allowedIds = string.Join(",", ids);
        var dependencies = Query($"""
            SELECT DISTINCT COALESCE(d.referenced_server_name + '.', '')
                + COALESCE(d.referenced_database_name + '.', '')
                + COALESCE(d.referenced_schema_name + '.', '')
                + COALESCE(d.referenced_entity_name, '?') AS MissingDependency
            FROM sys.sql_expression_dependencies d
            WHERE d.referencing_id IN ({scopeIds}) AND
                (d.referenced_server_name IS NOT NULL OR d.referenced_database_name IS NOT NULL
                 OR (d.referenced_id IS NOT NULL AND d.referenced_id NOT IN ({allowedIds})
                     AND OBJECTPROPERTY(d.referenced_id, 'IsMSShipped') = 0)
                 OR (d.referenced_id IS NULL AND d.referenced_entity_name IS NOT NULL
                     AND d.referenced_class = 1))
            UNION
            SELECT QUOTENAME(OBJECT_SCHEMA_NAME(f.referenced_object_id)) + '.' + QUOTENAME(OBJECT_NAME(f.referenced_object_id))
            FROM sys.foreign_keys f WHERE f.parent_object_id IN ({tableIds}) AND f.referenced_object_id NOT IN ({allowedIds})
            UNION
            SELECT QUOTENAME(SCHEMA_NAME(y.schema_id)) + '.' + QUOTENAME(y.name)
            FROM sys.columns c JOIN sys.types y ON y.user_type_id = c.user_type_id
            WHERE c.object_id IN ({tableIds}) AND y.is_user_defined = 1
            """);
        if (dependencies.Rows.Count > 0)
            throw new InvalidOperationException("Selected objects depend on items outside the code allowlist. Nothing exported. Review: "
                + string.Join(", ", dependencies.Rows.Cast<System.Data.DataRow>().Select(r => r[0].ToString())));
        if (tables.Any(t => t.IsMemoryOptimized || t.TemporalType != TableTemporalType.None))
            throw new InvalidOperationException("Temporal/memory-optimized tables need a separately reviewed export.");
        if (procedures.Any(p => p.IsEncrypted) || triggers.Any(t => t.IsEncrypted))
            throw new InvalidOperationException("Encrypted modules cannot be scripted from their definitions. Nothing exported.");

        var options = new ScriptingOptions
        {
            ScriptSchema = true, ScriptData = false, WithDependencies = false,
            SchemaQualify = true, IncludeDatabaseContext = false, IncludeHeaders = false,
            ScriptDrops = false, IncludeIfNotExists = false,
            DriAll = false, DriPrimaryKey = true, DriUniqueKeys = true,
            DriDefaults = true, DriChecks = true, DriForeignKeys = false,
            Indexes = true, Triggers = false, NoCollation = false,
            Permissions = false, ScriptOwner = false,
            TargetServerVersion = Enum.Parse<SqlServerVersion>($"Version{major}0"),
            TargetDatabaseEngineType = DatabaseEngineType.Standalone
        };
        var scripter = new Scripter(server) { Options = options };
        var rowCounts = new Dictionary<string, long>();
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var writer = new StreamWriter(new FileStream(temporary, FileMode.CreateNew), new UTF8Encoding(true)))
            {
                void Batch(string sql) { writer.WriteLine(sql); writer.WriteLine("GO"); }
                writer.WriteLine(":ON ERROR EXIT");
                writer.WriteLine("-- Generated from the real source schema and data. Contains application/user data: keep private.");
                writer.WriteLine("-- Run in SSMS SQLCMD Mode on the specified LOCAL server only. A NEW database is required.");
                Batch("USE [master];");
                Batch($"IF CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) <> {Literal(localServer)} THROW 51300, 'Wrong SQL Server instance. Import stopped.', 1;\n"
                    + $"IF DB_ID({Literal(targetDatabase)}) IS NOT NULL THROW 51301, 'Target database already exists. Choose a new target.', 1;\n"
                    + $"EXEC({Literal($"CREATE DATABASE {Identifier(targetDatabase)} COLLATE {Identifier(database.Collation)};")});");
                Batch($"USE {Identifier(targetDatabase)};");
                Batch("SET XACT_ABORT ON;\nBEGIN TRANSACTION;");
                foreach (string name in tables.Select(t => t.Schema).Concat(procedures.Select(p => p.Schema)).Distinct().Where(s => s != "dbo"))
                    Batch($"IF SCHEMA_ID({Literal(name)}) IS NULL EXEC({Literal($"CREATE SCHEMA {Identifier(name)} AUTHORIZATION [dbo];")});");

                foreach (Table table in tables)
                {
                    Console.WriteLine($"Schema: {Qualified(table.Schema, table.Name)}");
                    foreach (string sql in scripter.EnumScript(new[] { table.Urn })) Batch(sql);
                }
                options.ScriptSchema = false;
                options.ScriptData = true;
                foreach (Table table in tables)
                {
                    string name = Qualified(table.Schema, table.Name);
                    long before = Convert.ToInt64(Scalar($"SELECT COUNT_BIG(*) FROM {name};"), CultureInfo.InvariantCulture);
                    Console.WriteLine($"Data: {name} ({before} rows)");
                    foreach (string sql in scripter.EnumScript(new[] { table.Urn })) Batch(sql);
                    long after = Convert.ToInt64(Scalar($"SELECT COUNT_BIG(*) FROM {name};"), CultureInfo.InvariantCulture);
                    if (before != after) throw new InvalidOperationException("Source row counts changed during export. Pause writes and retry into a new file.");
                    rowCounts.Add(name, after);
                }
                options.ScriptData = false;
                options.ScriptSchema = true;
                options.DriForeignKeys = true;
                foreach (Table table in tables)
                    foreach (ForeignKey key in table.ForeignKeys)
                        foreach (string? sql in key.Script(options)) if (sql is not null) Batch(sql);
                foreach (StoredProcedure procedure in procedures)
                    foreach (string? sql in procedure.Script(options)) if (sql is not null) Batch(sql);
                foreach (Trigger trigger in triggers)
                    foreach (string? sql in trigger.Script(options)) if (sql is not null) Batch(sql);
                foreach (var (name, count) in rowCounts)
                    Batch($"IF (SELECT COUNT_BIG(*) FROM {name}) <> {count.ToString(CultureInfo.InvariantCulture)} THROW 51302, 'Imported row count differs from export.', 1;");
                Batch("COMMIT TRANSACTION;");
                Batch("SELECT DB_NAME() AS ImportedDatabase, N'Import committed; compare the generated inventory before starting the app.' AS Result;");
            }
            var summary = new
            {
                SourceServer = sourceServer, SourceDatabase = sourceDatabase,
                TargetDatabase = targetDatabase, LocalServer = localServer, ExportedAtUtc = DateTime.UtcNow,
                Tables = rowCounts,
                Procedures = procedures.Select(p => Qualified(p.Schema, p.Name)).ToArray(),
                Triggers = triggers.Select(t => t.Name).ToArray(),
                MissingTableNamesInThisDatabase = tableNames.Except(tables.Select(t => t.Name), StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                MissingProcedureNamesInThisDatabase = procNames.Except(procedures.Select(p => p.Name), StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                Note = "No unrelated tables/procedures copied. Source writes must remain paused; equal counts do not detect concurrent updates. Dynamic SQL dependencies need manual review."
            };
            using (var reportStream = new FileStream(report, FileMode.CreateNew))
                JsonSerializer.Serialize(reportStream, summary, new JsonSerializerOptions { WriteIndented = true });
            File.Move(temporary, output, overwrite: false);
            Console.WriteLine($"Created {output} and {report}. No local database has been changed.");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    finally { connection.Disconnect(); }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Export failed ({ex.GetType().Name}): {ex.Message}");
    return 1;
}

static string Identifier(string name) => "[" + name.Replace("]", "]]") + "]";
static string Literal(string value) => "N'" + value.Replace("'", "''") + "'";
static string Qualified(string schema, string name) => Identifier(schema) + "." + Identifier(name);
static SecureString ReadPassword()
{
    if (Console.IsInputRedirected) throw new InvalidOperationException("Run interactively so the password is not placed in command history or input files.");
    Console.Write("SQL password (hidden): ");
    var value = new SecureString();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.RemoveAt(value.Length - 1); }
        else if (!char.IsControl(key.KeyChar)) value.AppendChar(key.KeyChar);
    }
    Console.WriteLine();
    value.MakeReadOnly();
    return value;
}

sealed class Manifest
{
    public string[] BusinessTables { get; set; } = [];
    public string[] IdentityTables { get; set; } = [];
    public string[] ExampleTables { get; set; } = [];
    public string[] DeclaredProcedures { get; set; } = [];
}
