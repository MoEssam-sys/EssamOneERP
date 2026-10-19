using Microsoft.Extensions.Logging;
using One.Application.Base;
using One.Domain.Entities.Sales;
using One.Infrastructure.DbContext.MainDbContext;

namespace One.Application.Repos.Sales
{
    public class BrandsRepo : BaseRepository<Brands, Guid>
    {
        public BrandsRepo(IDapperConnectionFactory dbFactory, ILogger<BrandsRepo> logger)
            : base(dbFactory, logger)
        { }
    }
}