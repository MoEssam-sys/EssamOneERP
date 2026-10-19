using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using One.Application.Repos.Sales;
using One.Application.Services.AdvNavigation;
using One.Application.Services.Mapping;
using One.Domain.Base;
using One.Domain.DTOs;
using One.Domain.DTOs.Sales;
using One.Domain.Entities.Sales;
using One.Domain.Enums;
using System.Security.Claims;

namespace One.API.Controllers.Sales
{
    [Authorize]
    [ApiController]
    [Route("api/sales/[controller]")]
    public class BrandsController : BaseApiController
    {
        private readonly BrandsRepo _repo;

        private readonly IDynamicMappingService _mapper;

        private readonly ILogger<BrandsController> _logger;

        public BrandsController(BrandsRepo repo, IDynamicMappingService mapper, ILogger<BrandsController> logger)
        {
            _repo = repo;
            _mapper = mapper;
            _logger = logger;
        }

        // GET: api/sales/brands
        [HttpGet]
        public async Task<ActionResult<Result<List<Brands>>>> GetAll()
        {
            var res = await _repo.GetAllAsync();
            return ToActionResult(res);
        }

        [HttpPost("filter")]
        public async Task<ActionResult<Result<PagedResult<Brands>>>> Filter([FromBody] FilterBrandRequest filter)
        {
            if (!ModelState.IsValid)
                return ToPagedActionResult(Result<PagedResult<Brands>>.Fail(new() { "Validation failed." }, 400));

            var res = await _repo.QueryByFilterAsync(filter);
            return ToPagedActionResult(res);
        }

        // GET: api/sales/brands/{id}
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<Result<Brands?>>> GetById(Guid id)
        {
            var res = await _repo.GetByIdAsync(id);
            return ToActionResult(res);
        }

        // POST: api/sales/brands/create
        [HttpPost("create")]
        public async Task<ActionResult<Result<Brands>>> Create([FromBody] CreateBrandRequest dto)
        {
            if (!ModelState.IsValid)
                return ToActionResult(Result<Brands>.Fail(new() { "Validation failed." }, 400));

            var userId = GetAuthenticatedUserId();
            if (userId == null)
                return Unauthorized(Result<Brands>.Fail(new() { "User not authenticated." }, 401));

            // بنستخدم المابر الدايناميك → هيملأ الخصائص اللي عليها [InsertField]
            var entity = _mapper.MapCreate<Brands, CreateBrandRequest>(dto);

            var insertRes = await _repo.InsertAndReturnAsync(entity, userId.Value);
            if (!insertRes.Success)
                return ToActionResult(Result<Brands>.Fail(insertRes.Messages ?? new() { "Insert failed." },
                                                          insertRes.StatusCode ?? 400));

            return ToActionResult(insertRes, statusOverride: 201);
        }

        // PUT: api/sales/brands/update
        [HttpPut("update")]
        public async Task<ActionResult<Result<bool>>> Update([FromBody] UpdateBrandRequest dto)
        {
            if (!ModelState.IsValid)
                return ToActionResult(Result<bool>.Fail(new() { "Validation failed." }, 400));

            var userId = GetAuthenticatedUserId();
            if (userId == null)
                return Unauthorized(Result<bool>.Fail(new() { "User not authenticated." }, 401));

            // المابر الدايناميك هيملأ [UpdateField] + Id
            var entity = _mapper.MapUpdate<Brands, UpdateBrandRequest>(dto);

            var res = await _repo.UpdateAsync(entity, userId.Value);
            return ToActionResult(res);
        }

        // DELETE: api/sales/brands/delete
        [HttpDelete("delete")]
        public async Task<ActionResult<Result<bool>>> Delete([FromBody] Guid id)
        {
            if (!ModelState.IsValid)
                return ToActionResult(Result<bool>.Fail(new() { "Validation failed." }, 400));

            var res = await _repo.DeleteAsync(id);
            return ToActionResult(res);
        }

        // PUT: api/sales/brands/active
        [HttpPut("active")]
        public async Task<ActionResult<Result<bool>>> SetActive([FromBody] ActiveBrandRequest dto)
        {
            if (!ModelState.IsValid)
                return ToActionResult(Result<bool>.Fail(new() { "Validation failed." }, 400));

            var userId = GetAuthenticatedUserId();
            if (userId == null)
                return Unauthorized(Result<bool>.Fail(new() { "User not authenticated." }, 401));

            // المابر ينسخ الـ Id (وأي مفاتيح ActiveField أخرى لو موجودة) + نمرر IsActive من الـ DTO
            var keyEntity = _mapper.MapActive<Brands, ActiveBrandRequest>(dto);

            var res = await _repo.SetActiveAsync(keyEntity, dto.IsActive, userId.Value);
            return ToActionResult(res);
        }

        // GET: api/sales/brandscrud/firstOrDefault
        [HttpGet("firstOrDefault")]
        public async Task<ActionResult<Result<Brands?>>> FirstOrDefaultAsync(Guid id)
        {
            var res = await _repo.FirstOrDefaultAsync("Id = @id", new { id });
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // GET: api/sales/brandscrud/exists/code/{code}
        [HttpGet("exists/code/{code}")]
        public async Task<ActionResult<Result<bool>>> IsCodeUnique(string code)
        {
            var res = await _repo.IsUniqueAsync(nameof(Brands.Code), code);
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // POST: api/sales/brandscrud/search
        [HttpPost("search")]
        public async Task<ActionResult<Result<List<Brands>>>> Search([FromBody] SearchRequest req)
        {
            var res = await _repo.SearchAsync(
                columns: new[] { nameof(Brands.Name), nameof(Brands.Code) },
                query: req.Query,
                maxResults: req.MaxResults ?? 10,
                likeCond: LikeConditions.OR,
                isActive: req.IsActive,                    // null/true/false حسب ما تبعت
                firstId: req.FirstId,
                firstIdColumn: req.FirstIdColumn,          // مثال: "CompanyId"
                secondId: req.SecondId,
                secondIdColumn: req.SecondIdColumn         // مثال: "BranchId"
            );

            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // POST: api/sales/brandscrud/ids
        [HttpPost("ids")]
        public async Task<ActionResult<Result<List<Brands>>>> GetByIds([FromBody] IEnumerable<Guid> ids)
        {
            var res = await _repo.GetByIdsAsync(ids);
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // GET: api/sales/brandscrud/last
        [HttpGet("last")]
        public async Task<ActionResult<Result<Brands?>>> GetLast()
        {
            // تقدر تغيّر العمود حسب ترتيبك: CreatedAt/Id/Code...
            var res = await _repo.GetLastAsync(orderByColumn: nameof(Brands.CreatedAt));
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // GET: api/sales/brandscrud/random
        [HttpGet("random")]
        public async Task<ActionResult<Result<Brands?>>> GetRandom()
        {
            var res = await _repo.GetRandomAsync();
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // PUT: api/sales/brandscrud/active
        // Body: { "id": "GUID", "isActive": true }
        [HttpPut("activeDapper")]
        public async Task<ActionResult<Result<bool>>> SetActiveByColumn([FromBody] ActiveBrandRequest dto)
        {
            if (!ModelState.IsValid)
                return StatusCode(400, Result<bool>.Fail(new() { "Validation failed." }, 400));

            var res = await _repo.SetActiveByColumnAsync(dto.Id, dto.IsActive, dto.Column);
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // أمثلة سريعة إضافية

        // GET: api/sales/brandscrud/find?minCreated=2025-01-01
        [HttpGet("find")]
        public async Task<ActionResult<Result<List<Brands>>>> Find([FromQuery] DateTime? minCreated = null)
        {
            if (minCreated == null)
                return StatusCode(400, Result<List<Brands>>.Fail(new() { "minCreated is required." }, 400));

            var res = await _repo.FindAsync("CreatedAt >= @d", new { d = minCreated });
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // GET: api/sales/brandscrud/count?active=true
        [HttpGet("count")]
        public async Task<ActionResult<Result<int>>> Count([FromQuery] bool? active = null)
        {
            Result<int> res;
            if (active.HasValue)
                res = await _repo.CountAsync("IsActive = @x", new { x = active.Value });
            else
                res = await _repo.CountAsync();

            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }



    }
}