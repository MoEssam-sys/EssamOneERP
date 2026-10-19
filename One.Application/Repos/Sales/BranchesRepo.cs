using Microsoft.Extensions.Logging;
using One.Application.Base;
using One.Domain.Entities.Sales;
using One.Infrastructure.DbContext.MainDbContext;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace One.Application.Repos.Sales
{
    public class BranchesRepo : BaseRepository<Branches, Guid>
    {
        public BranchesRepo(IDapperConnectionFactory dbFactory, ILogger<BranchesRepo> logger)
            : base(dbFactory, logger)
        { }
    }
}
