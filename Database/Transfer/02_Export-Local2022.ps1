param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $LocalServer,

    [string] $Source1Server = 'db37048.public.databaseasp.net',
    [string] $Source1Database = 'db37048',
    [string] $Source1Target = 'db37048_Local',
    [string] $Source2Server = 'db36442.public.databaseasp.net',
    [string] $Source2Database = 'db36442',
    [string] $Source2Target = 'db36442_Local',
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'exports'),
    [string] $Schema,
    [switch] $IncludeExamples
)

$ErrorActionPreference = 'Stop'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install .NET 10 SDK before running this export.'
}
if ($Source1Target -eq $Source2Target) {
    throw 'Use a different local target database name for each source.'
}

# These hostnames come from the supplied transfer archive. Verify them in SSMS.
# This script only exports files. Import them separately on the LOCAL instance.
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$exportProject = Join-Path $PSScriptRoot 'UsedDbExport/UsedDbExport.csproj'
$jobs = @(
    @{ Server = $Source1Server; Database = $Source1Database; Target = $Source1Target; File = 'source1.used.sql' },
    @{ Server = $Source2Server; Database = $Source2Database; Target = $Source2Target; File = 'source2.used.sql' }
)

# Check BOTH destinations before starting either source connection.
foreach ($job in $jobs) {
    $sqlPath = Join-Path $OutputDirectory $job.File
    if ((Test-Path -LiteralPath $sqlPath) -or (Test-Path -LiteralPath "$sqlPath.inventory.json")) {
        throw "Export files already exist: $sqlPath. Choose another OutputDirectory."
    }
}

foreach ($job in $jobs) {
    $exportArgs = @(
        'run', '--configuration', 'Release', '--project', $exportProject, '--',
        '--server', $job.Server, '--database', $job.Database,
        '--target', $job.Target, '--local-server', $LocalServer,
        '--target-major', '16', '--output', (Join-Path $OutputDirectory $job.File)
    )
    if ($Schema) { $exportArgs += @('--schema', $Schema) }
    if ($IncludeExamples) { $exportArgs += '--include-examples' }
    & dotnet @exportArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Export failed for $($job.Database). Review the error before importing any file."
    }
}

Write-Host "Export finished: $OutputDirectory"
Write-Host 'In SSMS on LOCAL: enable Query > SQLCMD Mode, then run each source*.used.sql file.'
Write-Host 'Compare inventories across BOTH sources before configuring the API connections.'
