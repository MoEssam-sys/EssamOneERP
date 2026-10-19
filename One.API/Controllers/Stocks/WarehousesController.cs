using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using One.Application.Repos.Stocks;
using One.Application.Services.Mapping;
using One.Domain.Base;
using One.Domain.DTOs;
using One.Domain.DTOs.Stocks;
using One.Domain.Entities.Stocks;
using One.Domain.Enums;

namespace One.API.Controllers.Stockes
{
    [Authorize]
    [ApiController]
    [Route("api/stocks/[controller]")]
    public class WarehousesController : BaseApiController
    {
        private readonly WarehousesRepo _repo;

        private readonly IDynamicMappingService _mapper;

        private readonly ILogger<WarehousesController> _logger;

        public WarehousesController(WarehousesRepo repo, IDynamicMappingService mapper, ILogger<WarehousesController> logger)
        {
            _repo = repo;
            _mapper = mapper;
            _logger = logger;
        }

        // GET: api/sales/Warehouses
        [HttpGet]
        public async Task<ActionResult<Result<List<Warehouses>>>> GetAll()
        {
            var res = await _repo.GetAllAsync();
            return ToActionResult(res);
        }

        [HttpPost("filter")]
        public async Task<ActionResult<Result<PagedResult<Warehouses>>>> Filter([FromBody] FilterWarehouseRequest filter)
        {
            if (!ModelState.IsValid)
                return ToPagedActionResult(Result<PagedResult<Warehouses>>.Fail(new() { "Validation failed." }, 400));

            var res = await _repo.QueryByFilterAsync(filter);
            return ToPagedActionResult(res);
        }

        // GET: api/sales/Warehouses/{id}
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<Result<Warehouses?>>> GetById(Guid id)
        {
            var res = await _repo.GetByIdAsync(id);
            return ToActionResult(res);
        }

        // POST: api/sales/Warehouses/create
        [HttpPost("create")]
        public async Task<ActionResult<Result<Warehouses>>> Create([FromBody] CreateWarehouseRequest dto)
        {
            if (!ModelState.IsValid)
                return ToActionResult(Result<Warehouses>.Fail(new() { "Validation failed." }, 400));

            var userId = GetAuthenticatedUserId();
            if (userId == null)
                return Unauthorized(Result<Warehouses>.Fail(new() { "User not authenticated." }, 401));

            // بنستخدم المابر الدايناميك → هيملأ الخصائص اللي عليها [InsertField]
            var entity = _mapper.MapCreate<Warehouses, CreateWarehouseRequest>(dto);

            var insertRes = await _repo.InsertAndReturnAsync(entity, userId.Value);
            if (!insertRes.Success)
                return ToActionResult(Result<Warehouses>.Fail(insertRes.Messages ?? new() { "Insert failed." },
                                                          insertRes.StatusCode ?? 400));

            return ToActionResult(insertRes, statusOverride: 201);
        }

        // PUT: api/sales/Warehouses/update
        [HttpPut("update")]
        public async Task<ActionResult<Result<bool>>> Update([FromBody] UpdateWarehouseRequest dto)
        {
            if (!ModelState.IsValid)
                return ToActionResult(Result<bool>.Fail(new() { "Validation failed." }, 400));

            var userId = GetAuthenticatedUserId();
            if (userId == null)
                return Unauthorized(Result<bool>.Fail(new() { "User not authenticated." }, 401));

            // المابر الدايناميك هيملأ [UpdateField] + Id
            var entity = _mapper.MapUpdate<Warehouses, UpdateWarehouseRequest>(dto);

            var res = await _repo.UpdateAsync(entity, userId.Value);
            return ToActionResult(res);
        }

        // DELETE: api/sales/Warehouses/delete
        [HttpDelete("delete")]
        public async Task<ActionResult<Result<bool>>> Delete([FromBody] Guid id)
        {
            if (!ModelState.IsValid)
                return ToActionResult(Result<bool>.Fail(new() { "Validation failed." }, 400));

            var res = await _repo.DeleteAsync(id);
            return ToActionResult(res);
        }

        // PUT: api/sales/Warehouses/active
        [HttpPut("active")]
        public async Task<ActionResult<Result<bool>>> SetActive([FromBody] ActiveWarehouseRequest dto)
        {
            if (!ModelState.IsValid)
                return ToActionResult(Result<bool>.Fail(new() { "Validation failed." }, 400));

            var userId = GetAuthenticatedUserId();
            if (userId == null)
                return Unauthorized(Result<bool>.Fail(new() { "User not authenticated." }, 401));

            // المابر ينسخ الـ Id (وأي مفاتيح ActiveField أخرى لو موجودة) + نمرر IsActive من الـ DTO
            var keyEntity = _mapper.MapActive<Warehouses, ActiveWarehouseRequest>(dto);

            var res = await _repo.SetActiveAsync(keyEntity, dto.IsActive, userId.Value);
            return ToActionResult(res);
        }

        // GET: api/sales/Warehousescrud/firstOrDefault
        [HttpGet("firstOrDefault")]
        public async Task<ActionResult<Result<Warehouses?>>> FirstOrDefaultAsync(Guid id)
        {
            var res = await _repo.FirstOrDefaultAsync("Id = @id", new { id });
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // GET: api/sales/Warehousescrud/exists/code/{code}
        [HttpGet("exists/code/{code}")]
        public async Task<ActionResult<Result<bool>>> IsCodeUnique(string code)
        {
            var res = await _repo.IsUniqueAsync(nameof(Warehouses.Code), code);
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // POST: api/sales/Warehousescrud/search
        [HttpPost("search")]
        public async Task<ActionResult<Result<List<Warehouses>>>> Search([FromBody] SearchRequest req)
        {
            var res = await _repo.SearchAsync(
                columns: new[] { nameof(Warehouses.Name), nameof(Warehouses.Code), nameof(Warehouses.BranchId) },
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

        // POST: api/sales/Warehousescrud/ids
        [HttpPost("ids")]
        public async Task<ActionResult<Result<List<Warehouses>>>> GetByIds([FromBody] IEnumerable<Guid> ids)
        {
            var res = await _repo.GetByIdsAsync(ids);
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // GET: api/sales/Warehousescrud/last
        [HttpGet("last")]
        public async Task<ActionResult<Result<Warehouses?>>> GetLast()
        {
            // تقدر تغيّر العمود حسب ترتيبك: CreatedAt/Id/Code...
            var res = await _repo.GetLastAsync(orderByColumn: nameof(Warehouses.CreatedAt));
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // GET: api/sales/Warehousescrud/random
        [HttpGet("random")]
        public async Task<ActionResult<Result<Warehouses?>>> GetRandom()
        {
            var res = await _repo.GetRandomAsync();
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // PUT: api/sales/Warehousescrud/active
        // Body: { "id": "GUID", "isActive": true }
        [HttpPut("activeDapper")]
        public async Task<ActionResult<Result<bool>>> SetActiveByColumn([FromBody] ActiveWarehouseRequest dto)
        {
            if (!ModelState.IsValid)
                return StatusCode(400, Result<bool>.Fail(new() { "Validation failed." }, 400));

            var res = await _repo.SetActiveByColumnAsync(dto.Id, dto.IsActive, dto.Column);
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // أمثلة سريعة إضافية

        // GET: api/sales/Warehousescrud/find?minCreated=2025-01-01
        [HttpGet("find")]
        public async Task<ActionResult<Result<List<Warehouses>>>> Find([FromQuery] DateTime? minCreated = null)
        {
            if (minCreated == null)
                return StatusCode(400, Result<List<Warehouses>>.Fail(new() { "minCreated is required." }, 400));

            var res = await _repo.FindAsync("CreatedAt >= @d", new { d = minCreated });
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // GET: api/sales/Warehousescrud/count?active=true
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
