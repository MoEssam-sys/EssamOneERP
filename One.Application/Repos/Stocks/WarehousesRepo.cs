using Microsoft.Extensions.Logging;
using One.Application.Base;
using One.Domain.Entities.Stocks;
using One.Infrastructure.DbContext.MainDbContext;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace One.Application.Repos.Stocks
{

    public class WarehousesRepo : BaseRepository<Warehouses, Guid>
    {
        public WarehousesRepo(IDapperConnectionFactory dbFactory, ILogger<WarehousesRepo> logger)
            : base(dbFactory, logger)
        { }
    }
}
