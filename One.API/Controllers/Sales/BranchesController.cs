using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using One.Application.Repos.Sales;
using One.Application.Services.Mapping;
using One.Domain.Base;
using One.Domain.DTOs;
using One.Domain.DTOs.Sales;
using One.Domain.Entities.Sales;
using One.Domain.Enums;

namespace One.API.Controllers.Sales
{
    [Authorize]
    [Route("api/sales/[controller]")]
    [ApiController]
    public class BranchesController : BaseApiController
    {
        private readonly BranchesRepo _repo;

        private readonly IDynamicMappingService _mapper;

        private readonly ILogger<BranchesController> _logger;

        public BranchesController(BranchesRepo repo, IDynamicMappingService mapper, ILogger<BranchesController> logger)
        {
            _repo = repo;
            _mapper = mapper;
            _logger = logger;
        }

        // GET: api/sales/Branches
        [HttpGet]
        public async Task<ActionResult<Result<List<Branches>>>> GetAll()
        {
            var res = await _repo.GetAllAsync();
            return ToActionResult(res);
        }

        [HttpPost("filter")]
        public async Task<ActionResult<Result<PagedResult<Branches>>>> Filter([FromBody] FilterBranchRequest filter)
        {
            if (!ModelState.IsValid)
                return ToPagedActionResult(Result<PagedResult<Branches>>.Fail(new() { "Validation failed." }, 400));

            var res = await _repo.QueryByFilterAsync(filter);
            return ToPagedActionResult(res);
        }

        // GET: api/sales/Branches/{id}
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<Result<Branches?>>> GetById(Guid id)
        {
            var res = await _repo.GetByIdAsync(id);
            return ToActionResult(res);
        }

        // POST: api/sales/Branches/create
        [HttpPost("create")]
        public async Task<ActionResult<Result<Branches>>> Create([FromBody] CreateBranchRequest dto)
        {
            if (!ModelState.IsValid)
                return ToActionResult(Result<Branches>.Fail(new() { "Validation failed." }, 400));

            var userId = GetAuthenticatedUserId();
            if (userId == null)
                return Unauthorized(Result<Branches>.Fail(new() { "User not authenticated." }, 401));

            // بنستخدم المابر الدايناميك → هيملأ الخصائص اللي عليها [InsertField]
            var entity = _mapper.MapCreate<Branches, CreateBranchRequest>(dto);

            var insertRes = await _repo.InsertAndReturnAsync(entity, userId.Value);
            if (!insertRes.Success)
                return ToActionResult(Result<Branches>.Fail(insertRes.Messages ?? new() { "Insert failed." },
                                                          insertRes.StatusCode ?? 400));

            return ToActionResult(insertRes, statusOverride: 201);
        }

        // PUT: api/sales/Branches/update
        [HttpPut("update")]
        public async Task<ActionResult<Result<bool>>> Update([FromBody] UpdateBranchRequest dto)
        {
            if (!ModelState.IsValid)
                return ToActionResult(Result<bool>.Fail(new() { "Validation failed." }, 400));

            var userId = GetAuthenticatedUserId();
            if (userId == null)
                return Unauthorized(Result<bool>.Fail(new() { "User not authenticated." }, 401));

            // المابر الدايناميك هيملأ [UpdateField] + Id
            var entity = _mapper.MapUpdate<Branches, UpdateBranchRequest>(dto);

            var res = await _repo.UpdateAsync(entity, userId.Value);
            return ToActionResult(res);
        }

        // DELETE: api/sales/Branches/delete
        [HttpDelete("delete")]
        public async Task<ActionResult<Result<bool>>> Delete([FromBody] Guid id)
        {
            if (!ModelState.IsValid)
                return ToActionResult(Result<bool>.Fail(new() { "Validation failed." }, 400));

            var res = await _repo.DeleteAsync(id);
            return ToActionResult(res);
        }

        // PUT: api/sales/Branches/active
        [HttpPut("active")]
        public async Task<ActionResult<Result<bool>>> SetActive([FromBody] ActiveBranchRequest dto)
        {
            if (!ModelState.IsValid)
                return ToActionResult(Result<bool>.Fail(new() { "Validation failed." }, 400));

            var userId = GetAuthenticatedUserId();
            if (userId == null)
                return Unauthorized(Result<bool>.Fail(new() { "User not authenticated." }, 401));

            // المابر ينسخ الـ Id (وأي مفاتيح ActiveField أخرى لو موجودة) + نمرر IsActive من الـ DTO
            var keyEntity = _mapper.MapActive<Branches, ActiveBranchRequest>(dto);

            var res = await _repo.SetActiveAsync(keyEntity, dto.IsActive, userId.Value);
            return ToActionResult(res);
        }

        // GET: api/sales/Branchescrud/firstOrDefault
        [HttpGet("firstOrDefault")]
        public async Task<ActionResult<Result<Branches?>>> FirstOrDefaultAsync(Guid id)
        {
            var res = await _repo.FirstOrDefaultAsync("Id = @id", new { id });
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // GET: api/sales/Branchescrud/exists/code/{code}
        [HttpGet("exists/code/{code}")]
        public async Task<ActionResult<Result<bool>>> IsCodeUnique(string code)
        {
            var res = await _repo.IsUniqueAsync(nameof(Branches.Code), code);
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // POST: api/sales/Branchescrud/search
        [HttpPost("search")]
        public async Task<ActionResult<Result<List<Branches>>>> Search([FromBody] SearchRequest req)
        {
            var res = await _repo.SearchAsync(
                columns: [nameof(Branches.Name), nameof(Branches.Code), nameof(Branches.Phone)],
                query: req.Query,
                maxResults: req.MaxResults ?? 10,
                likeCond: LikeConditions.OR,
                isActive: req.IsActive,
                firstId: req.FirstId,
                firstIdColumn: req.FirstIdColumn,
                secondId: req.SecondId,
                secondIdColumn: req.SecondIdColumn
            );

            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // POST: api/sales/Branchescrud/ids
        [HttpPost("ids")]
        public async Task<ActionResult<Result<List<Branches>>>> GetByIds([FromBody] IEnumerable<Guid> ids)
        {
            var res = await _repo.GetByIdsAsync(ids);
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // GET: api/sales/Branchescrud/last
        [HttpGet("last")]
        public async Task<ActionResult<Result<Branches?>>> GetLast()
        {
            // تقدر تغيّر العمود حسب ترتيبك: CreatedAt/Id/Code...
            var res = await _repo.GetLastAsync(orderByColumn: nameof(Branches.CreatedAt));
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // GET: api/sales/Branchescrud/random
        [HttpGet("random")]
        public async Task<ActionResult<Result<Branches?>>> GetRandom()
        {
            var res = await _repo.GetRandomAsync();
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // PUT: api/sales/Branchescrud/active
        // Body: { "id": "GUID", "isActive": true }
        [HttpPut("activeDapper")]
        public async Task<ActionResult<Result<bool>>> SetActiveByColumn([FromBody] ActiveBranchRequest dto)
        {
            if (!ModelState.IsValid)
                return StatusCode(400, Result<bool>.Fail(new() { "Validation failed." }, 400));

            var res = await _repo.SetActiveByColumnAsync(dto.Id, dto.IsActive, dto.Column);
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // أمثلة سريعة إضافية

        // GET: api/sales/Branchescrud/find?minCreated=2025-01-01
        [HttpGet("find")]
        public async Task<ActionResult<Result<List<Branches>>>> Find([FromQuery] DateTime? minCreated = null)
        {
            if (minCreated == null)
                return StatusCode(400, Result<List<Branches>>.Fail(new() { "minCreated is required." }, 400));

            var res = await _repo.FindAsync("CreatedAt >= @d", new { d = minCreated });
            return StatusCode(res.StatusCode ?? (res.Success ? 200 : 400), res);
        }

        // GET: api/sales/Branchescrud/count?active=true
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