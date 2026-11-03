/* Connect SSMS to localhost (or your named instance) and run this first.
   Use ExactLocalServer as -LocalServer in 02_Export-Local2022.ps1.
   localhost is a connection alias, not necessarily SERVERPROPERTY('ServerName').
   Read-only.
*/
SET NOCOUNT ON;

SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) AS ExactLocalServer,
       CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS ProductVersion,
       CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) AS MajorVersion,
       CONVERT(nvarchar(128), SERVERPROPERTY('Edition')) AS Edition;

IF CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) <> 16
    THROW 51010, 'This package is configured for SQL Server 2022 (major version 16). Check the instance.', 1;
