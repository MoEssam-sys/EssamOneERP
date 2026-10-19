using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace One.Infrastructure.DbContext.MainDbContext
{
    public class DbConnectionFactory : IDapperConnectionFactory
    {
        private readonly IConfiguration _config;
        private readonly string _connStrName;

        public DbConnectionFactory(IConfiguration config)
        {
            _config = config;
#if DEBUG
            _connStrName = "MainConnection";
#else
            _connStrName = "LiveConnection";
#endif
        }

        public IDbConnection CreateConnection()
        {
            var connString = _config.GetConnectionString(_connStrName);
            return new SqlConnection(connString);
        }
    }
}