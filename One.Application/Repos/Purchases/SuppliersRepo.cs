using Microsoft.Extensions.Logging;
using One.Application.Base;
using One.Domain.Entities.Purchases;
using One.Infrastructure.DbContext.MainDbContext;

namespace One.Application.Repos.Purchases
{
    public class SuppliersRepo : BaseRepository<Suppliers, Guid>
    {
        public SuppliersRepo(IDapperConnectionFactory dbFactory, ILogger<SuppliersRepo> logger)
            : base(dbFactory, logger)
        { }
    }
}