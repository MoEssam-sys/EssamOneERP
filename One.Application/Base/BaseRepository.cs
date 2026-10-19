using Dapper;
using Microsoft.Extensions.Logging;
using One.Domain.Base;
using One.Domain.Enums;
using One.Domain.Services.Attributes;
using One.Infrastructure.DbContext.MainDbContext;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Z.Dapper.Plus;

namespace One.Application.Base
{
    public abstract class BaseRepository<T, TKey> where T : BaseEntity<TKey>, new()
    {
        protected readonly IDapperConnectionFactory dbFactory;
        private readonly ILogger logger;
        protected readonly string tableName;           // الاسم المنطقي (قد يتضمن schema)
        protected readonly string tableNameSql;        // الاسم مؤهَّل بالأقواس [] لاستخدامه في SQL

        // Fix #11: Compiled whitelist regex — only safe SQL identifiers pass
        private static readonly Regex _safeIdentifierRegex =
            new(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);

        // Fix #10: Cache PropertyInfo lookups per filter type
        private static readonly ConcurrentDictionary<Type, (PropertyInfo? pageNumber, PropertyInfo? pageSize)>
            _filterPropCache = new();

        // ===== Static mapping (مرّة لكل نوع) =====
        static BaseRepository()
        {
            var t = typeof(T);
            var tbl = t.GetCustomAttribute<DbTableAttribute>()?.Name;
            var resolved = string.IsNullOrWhiteSpace(tbl) ? t.Name : tbl!;
            // خريطة افتراضية (قد نعيد ضبطها بالـ ctor حسب TableNameOverride):
            DapperPlusManager.Entity<T>().Table(resolved).Key(x => x.Id);
        }

        // ===== Ctor =====
        protected BaseRepository(IDapperConnectionFactory dbFactory, ILogger _logger)
        {
            this.dbFactory = dbFactory;
            logger = _logger;

            var t = typeof(T);
            var tbl = t.GetCustomAttribute<DbTableAttribute>()?.Name;
            var resolved = string.IsNullOrWhiteSpace(tbl) ? t.Name : tbl!;

            tableName = !string.IsNullOrWhiteSpace(TableNameOverride) ? TableNameOverride! : resolved;
            tableNameSql = Qualify(tableName); // "[dbo].[Table]" أو "[Table]"

            // تأكيد اتساق دَبلَر-بلَس مع اسم الجدول النهائي (بما فيها الـ override)
            try { DapperPlusManager.Entity<T>().Table(tableName); } catch { /* ignore */ }
        }

        /// <summary>اختياري: لو حابب تغيّر اسم الجدول يدويًا بدل [DbTable] على الكلاس.</summary>
        protected virtual string? TableNameOverride => null;

        // ----------------- READ -----------------
        public virtual async Task<Result<T?>> GetByIdAsync(TKey id)
        {
            try
            {
                using var db = dbFactory.CreateConnection();
                var (sql, dp) = AttributeCrudEngine.BuildGetByIdSql<T, TKey>(id);
                LogSql(sql, dp);
                var row = await db.QueryFirstOrDefaultAsync<T>(sql, dp);
                return Result<T?>.Ok(row);
            }
            catch (Exception ex)
            {
                return Result<T?>.Fail(new() { "An error occurred while retrieving the record." });
            }
        }

        public virtual async Task<Result<List<T>>> GetAllAsync()
        {
            try
            {
                using var db = dbFactory.CreateConnection();
                // Fix #6: Safety limit — prevents unbounded full-table scans
                var sql = "SELECT TOP (100) " + AttributeCrudEngine.BuildGetAllSql<T>().TrimStart().Substring("SELECT ".Length);
                LogSql(sql, null);
                var list = (await db.QueryAsync<T>(sql)).AsList();
                return Result<List<T>>.Ok(list);
            }
            catch (Exception ex)
            {
                return Result<List<T>>.Fail(new() { "An error occurred while retrieving records." });
            }
        }

        public virtual async Task<Result<TKey>> InsertAsync(
            T entity,
            Guid currentUserId,
            Func<IDbConnection, IDbTransaction, TKey, Task<Result<bool>>>? extraActions = null)
        {
            using var db = dbFactory.CreateConnection();
            db.Open();
            using var tran = db.BeginTransaction();

            try
            {
                if (entity is BaseEntity<TKey> baseEntity)
                {
                    baseEntity.CreatedAt = DateTime.UtcNow;
                    baseEntity.CreatedBy = currentUserId;
                }

                var errors = AttributeCrudEngine.ValidateRequired(entity!, isInsert: true);
                if (errors.Count > 0)
                {
                    tran.Rollback();
                    return Result<TKey>.Fail(errors.Cast<string?>().ToList(), 400);
                }

                var (sql, dp, _) = AttributeCrudEngine.BuildInsertSql<TKey>(entity!);
                LogSql(sql, dp);
                var id = await db.ExecuteScalarAsync<TKey>(sql, dp, tran);

                if (id == null)
                {
                    tran.Rollback();
                    return Result<TKey>.Fail(new() { "Insert did not return a new id." }, 500);
                }

                if (extraActions != null)
                {
                    var more = await extraActions(db, tran, id);
                    if (!more.Success)
                    {
                        tran.Rollback();
                        return Result<TKey>.Fail(more.Messages ?? new() { "Post-insert actions failed." });
                    }
                }

                tran.Commit();
                return Result<TKey>.Ok(id);
            }
            catch (Exception ex)
            {
                tran.Rollback();
                return Result<TKey>.Fail(new() { "An error occurred while inserting the record." });
            }
        }

        public virtual async Task<Result<T>> InsertAndReturnAsync(
            T entity,
            Guid currentUserId,
            Func<IDbConnection, IDbTransaction, TKey, Task<Result<bool>>>? extraActions = null)
        {
            using var db = dbFactory.CreateConnection();
            db.Open();
            using var tran = db.BeginTransaction();

            try
            {
                if (entity is BaseEntity<TKey> baseEntity)
                {
                    baseEntity.CreatedAt = DateTime.UtcNow;
                    baseEntity.CreatedBy = currentUserId;
                }

                var errors = AttributeCrudEngine.ValidateRequired(entity!, isInsert: true);
                if (errors.Count > 0)
                {
                    tran.Rollback();
                    return Result<T>.Fail(errors.Cast<string?>().ToList(), 400);
                }

                var (insSql, dp, _) = AttributeCrudEngine.BuildInsertSql<TKey>(entity!);
                LogSql(insSql, dp);
                var id = await db.ExecuteScalarAsync<TKey>(insSql, dp, tran);

                if (id == null)
                {
                    tran.Rollback();
                    return Result<T>.Fail(new() { "Insert did not return a new id." }, 500);
                }

                if (extraActions != null)
                {
                    var more = await extraActions(db, tran, id);
                    if (!more.Success)
                    {
                        tran.Rollback();
                        return Result<T>.Fail(more.Messages ?? new() { "Post-insert actions failed." });
                    }
                }

                var (selSql, selDp) = AttributeCrudEngine.BuildGetByIdSql<T, TKey>(id);
                LogSql(selSql, selDp);
                var created = await db.QueryFirstOrDefaultAsync<T>(selSql, selDp, tran);

                tran.Commit();

                if (created == null)
                    return Result<T>.Fail(new() { "Inserted but no row returned by GetById." }, 500);

                return Result<T>.Ok(created);
            }
            catch (Exception ex)
            {
                tran.Rollback();
                return Result<T>.Fail(new() { "An error occurred while inserting the record." });
            }
        }

        // ----------------- UPDATE -----------------
        public virtual async Task<Result<bool>> UpdateAsync(
            T entity,
            Guid currentUserId,
            Func<IDbConnection, IDbTransaction, T, Task<Result<bool>>>? extraActions = null)
        {
            using var db = dbFactory.CreateConnection();
            db.Open();
            using var tran = db.BeginTransaction();

            try
            {
                if (entity is BaseEntity<TKey> baseEntity)
                {
                    baseEntity.UpdatedAt = DateTime.UtcNow;
                    baseEntity.UpdatedBy = currentUserId;
                }

                var errors = AttributeCrudEngine.ValidateRequired(entity!, isInsert: false);
                if (errors.Count > 0)
                {
                    tran.Rollback();
                    return Result<bool>.Fail(errors.Cast<string?>().ToList(), 400);
                }

                var (sql, dp) = AttributeCrudEngine.BuildUpdateSql(entity!);
                LogSql(sql, dp);
                var rows = await db.ExecuteAsync(sql, dp, tran);

                if (rows <= 0)
                {
                    tran.Rollback();
                    return Result<bool>.Fail(new() { "No rows updated." }, 404);
                }

                if (extraActions != null)
                {
                    var more = await extraActions(db, tran, entity);
                    if (!more.Success)
                    {
                        tran.Rollback();
                        return Result<bool>.Fail(more.Messages ?? new() { "Post-update actions failed." });
                    }
                }

                tran.Commit();
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                tran.Rollback();
                return Result<bool>.Fail(new() { "An error occurred while updating the record." });
            }
        }

        // ----------------- DELETE -----------------
        /// <summary>حذف باستخدام Id فقط (لو موجود).</summary>
        public virtual async Task<Result<bool>> DeleteAsync(TKey id)
        {
            try
            {
                var tmp = Activator.CreateInstance<T>();
                var idProp = typeof(T).GetProperty("Id");
                if (idProp == null)
                    return Result<bool>.Fail(new() { "No 'Id' property on T for DeleteAsync(id)." });

                idProp.SetValue(tmp, id);

                using var db = dbFactory.CreateConnection();
                var (sql, dp) = AttributeCrudEngine.BuildDeleteSql(tmp!);
                LogSql(sql, dp);
                var rows = await db.ExecuteAsync(sql, dp);

                return rows > 0 ? Result<bool>.Ok(true)
                                : Result<bool>.Fail(new() { "No rows deleted." }, 404);
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail(new() { "An error occurred while deleting the record." });
            }
        }

        // ----------------- ACTIVATE / DEACTIVATE -----------------
        /// <summary>تفعيل/تعطيل باستخدام [ActiveField]/[DeleteField] أو Id.</summary>
        public virtual async Task<Result<bool>> SetActiveAsync(
            T entityKeyOnly, bool isActive, Guid updatedBy, string? stateColumnName = null)
        {
            try
            {
                if (entityKeyOnly is BaseEntity<TKey> be)
                {
                    be.UpdatedAt = DateTime.UtcNow;
                    be.UpdatedBy = updatedBy;
                }

                using var db = dbFactory.CreateConnection();
                var (sql, dp) = AttributeCrudEngine.BuildSetActiveSql(entityKeyOnly!, isActive, stateColumnName);
                LogSql(sql, dp);
                var rows = await db.ExecuteAsync(sql, dp);

                return rows > 0 ? Result<bool>.Ok(true)
                                : Result<bool>.Fail(new() { "No rows changed." }, 404);
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail(new() { "An error occurred while updating the record status." });
            }
        }

        // ----------------- SELECT with Filter -----------------
        /// <summary>أي كلاس فلتر عليه [DbTable] + [MatchOn]/[MatchDictionary]</summary>
        public virtual async Task<Result<PagedResult<T>>> QueryByFilterAsync<TFilter>(TFilter filter)
        {
            try
            {
                using var db = dbFactory.CreateConnection();

                // 1) SQL الأساسي
                var (baseSqlRaw, dp) = AttributeCrudEngine.BuildSelectByFilterSql(filter!);

                // 2) شيل ; وأي ORDER BY نهائي لتجنّب "Incorrect syntax near 'ORDER'"
                var baseSql = baseSqlRaw.Trim();
                if (baseSql.EndsWith(";")) baseSql = baseSql[..^1].Trim();
                baseSql = Regex.Replace(baseSql, @"\border\s+by\b[\s\S]*$", string.Empty, RegexOptions.IgnoreCase).Trim();

                // 3) إجمالي العدّ
                var countSql = $"SELECT COUNT_BIG(1) FROM ({baseSql}) AS _c";
                var totalCount = await db.ExecuteScalarAsync<long>(countSql, dp);

                // 4) صفحة/حجم
                int pageNumber = 1, pageSize = 10;
                // Fix #10: Use cached PropertyInfo to avoid reflection on every request
                var (pnProp, psProp) = _filterPropCache.GetOrAdd(filter!.GetType(), t =>
                    (t.GetProperty("PageNumber"), t.GetProperty("PageSize")));

                if (pnProp?.PropertyType == typeof(int))
                    pageNumber = Math.Max(1, (int)(pnProp.GetValue(filter) ?? 1));

                if (psProp?.PropertyType == typeof(int))
                    pageSize = Math.Max(1, (int)(psProp.GetValue(filter) ?? 10));

                // 5) ترتيب افتراضي قبل OFFSET/FETCH
                string orderBy = HasProp(typeof(T), "CreatedAt")
                                 ? "[CreatedAt] DESC, [Id] DESC"
                                 : "[Id] DESC";

                var pagedSql = $"{baseSql} ORDER BY {orderBy} OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                var dpPaged = new DynamicParameters(dp);
                dpPaged.Add("@Offset", (pageNumber - 1) * pageSize);
                dpPaged.Add("@PageSize", pageSize);

                LogSql(pagedSql, dpPaged);

                var items = (await db.QueryAsync<T>(pagedSql, dpPaged)).AsList();

                var paged = new PagedResult<T>(
                    items: items,
                    totalCount: (int)Math.Min(totalCount, int.MaxValue),
                    pageNumber: pageNumber,
                    pageSize: pageSize,
                    filters: null, sorts: null, metadata: null
                );

                return Result<PagedResult<T>>.Ok(paged);
            }
            catch (Exception ex)
            {
                return Result<PagedResult<T>>.Fail(new() { "An error occurred while querying records." });
            }

            static bool HasProp(Type t, string name)
                => t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) != null;
        }

        #region ----- Paging / Query helpers -----

        public async Task<Result<List<T>>> FindAsync(string whereSql, object? parameters = null)
        {
            try
            {
                using var conn = dbFactory.CreateConnection();
                var sql = $"SELECT * FROM {tableNameSql} WHERE {whereSql}";
                var list = (await conn.QueryAsync<T>(sql, parameters)).ToList();
                return Result<List<T>>.Ok(list);
            }
            catch (Exception)
            {
                return Result<List<T>>.Fail(["حدث خطأ أثناء البحث."]);
            }
        }

        public async Task<Result<bool>> IsExistAsync(string whereSql, object? parameters = null)
        {
            try
            {
                using var conn = dbFactory.CreateConnection();
                var sql = $"SELECT 1 FROM {tableNameSql} WHERE {whereSql}";
                var exists = await conn.ExecuteScalarAsync<int?>(sql, parameters);
                return Result<bool>.Ok(exists.HasValue);
            }
            catch (Exception)
            {
                return Result<bool>.Fail(["تعذر التحقق من وجود العنصر."]);
            }
        }

        public async Task<Result<int>> CountAsync(string whereSql = "1=1", object? parameters = null)
        {
            try
            {
                using var conn = dbFactory.CreateConnection();
                var sql = $"SELECT COUNT(*) FROM {tableNameSql} WHERE {whereSql}";
                var count = await conn.ExecuteScalarAsync<int>(sql, parameters);
                return Result<int>.Ok(count);
            }
            catch (Exception)
            {
                return Result<int>.Fail(["تعذر حساب العدد."]);
            }
        }

        public async Task<Result<T?>> FirstOrDefaultAsync(string whereSql, object? parameters = null)
        {
            try
            {
                using var conn = dbFactory.CreateConnection();
                var sql = $"SELECT TOP 1 * FROM {tableNameSql} WHERE {whereSql}";
                var entity = await conn.QueryFirstOrDefaultAsync<T>(sql, parameters);
                return Result<T?>.Ok(entity);
            }
            catch (Exception)
            {
                return Result<T?>.Fail(["تعذر استرجاع العنصر."]);
            }
        }

        public async Task<Result<List<T>>> GetByIdsAsync(IEnumerable<TKey> ids, string? idColumn = "Id")
        {
            try
            {
                if (ids is null)
                    return Result<List<T>>.Fail(["قائمة المعرفات فارغة."]);

                var idsList = ids.ToList();
                if (idsList.Count == 0)
                    return Result<List<T>>.Ok(new List<T>());

                if (!IsValidSqlIdentifier(idColumn))
                    return Result<List<T>>.Fail(["اسم العمود غير صالح."]);

                using var conn = dbFactory.CreateConnection();
                var sql = $"SELECT * FROM {tableNameSql} WHERE [{idColumn}] IN @ids";
                var result = (await conn.QueryAsync<T>(sql, new { ids = idsList })).ToList();
                return Result<List<T>>.Ok(result);
            }
            catch (Exception)
            {
                return Result<List<T>>.Fail(["تعذر جلب العناصر المطلوبة."]);
            }
        }

        #endregion ----- Paging / Query helpers -----

        /// <summary>Soft-Delete: تعيين IsActive = 0 بالمعرّف</summary>
        public async Task<Result<bool>> DeleteByColumnAsync(object id, string? idColumn = "Id")
        {
            try
            {
                if (!IsValidSqlIdentifier(idColumn))
                    return Result<bool>.Fail(["اسم العمود غير صالح."]);

                using var conn = dbFactory.CreateConnection();
                var sql = $"UPDATE {tableNameSql} SET [IsActive] = 0 WHERE [{idColumn}] = @id";
                var affected = await conn.ExecuteAsync(sql, new { id });
                return Result<bool>.Ok(affected > 0);
            }
            catch (Exception)
            {
                return Result<bool>.Fail(["تعذر حذف السجل."]);
            }
        }

        public async Task<Result<bool>> SetActiveByColumnAsync(object id, bool isActive, string? idColumn = "Id")
        {
            try
            {
                if (!IsValidSqlIdentifier(idColumn))
                    return Result<bool>.Fail(["اسم العمود غير صالح."]);

                using var conn = dbFactory.CreateConnection();
                var sql = $"UPDATE {tableNameSql} SET [IsActive] = @isActive WHERE [{idColumn}] = @id";
                var affected = await conn.ExecuteAsync(sql, new { isActive, id });
                return Result<bool>.Ok(affected > 0);
            }
            catch (Exception)
            {
                return Result<bool>.Fail(["تعذر تغيير حالة العنصر."]);
            }
        }

        public async Task<Result<List<T>>> GetByIdsByColumnAsync(IEnumerable<object> ids, string? idColumn = "Id")
        {
            try
            {
                if (!IsValidSqlIdentifier(idColumn))
                    return Result<List<T>>.Fail(["اسم العمود غير صالح."]);

                using var conn = dbFactory.CreateConnection();
                var sql = $"SELECT * FROM {tableNameSql} WHERE [{idColumn}] IN @ids";
                var result = (await conn.QueryAsync<T>(sql, new { ids })).ToList();
                return Result<List<T>>.Ok(result);
            }
            catch (Exception)
            {
                return Result<List<T>>.Fail(["تعذر جلب العناصر المطلوبة."]);
            }
        }

        public async Task<Result<T?>> GetLastAsync(string orderByColumn = "Id")
        {
            try
            {
                if (!IsValidSqlIdentifier(orderByColumn))
                    return Result<T?>.Fail(["اسم عمود الترتيب غير صالح."]);

                using var conn = dbFactory.CreateConnection();
                var sql = $"SELECT TOP 1 * FROM {tableNameSql} ORDER BY [{orderByColumn}] DESC";
                var entity = await conn.QueryFirstOrDefaultAsync<T>(sql);
                return Result<T?>.Ok(entity);
            }
            catch (Exception)
            {
                return Result<T?>.Fail(["تعذر جلب العنصر الأخير."]);
            }
        }

        public async Task<Result<T?>> GetRandomAsync()
        {
            try
            {
                using var conn = dbFactory.CreateConnection();
                var sql = $"SELECT TOP 1 * FROM {tableNameSql} ORDER BY NEWID()";
                var entity = await conn.QueryFirstOrDefaultAsync<T>(sql);
                return Result<T?>.Ok(entity);
            }
            catch (Exception)
            {
                return Result<T?>.Fail(["تعذر جلب عنصر عشوائي."]);
            }
        }

        public async Task<Result<List<T>>> SearchAsync(
             string[] columns,
             string? query,
             int? maxResults = 20,
             LikeConditions likeCond = LikeConditions.OR,
             bool? isActive = true,                // null => هات الكل، true/false => فلترة
             Guid? firstId = null,
             string? firstIdColumn = null,
             Guid? secondId = null,
             string? secondIdColumn = null
         )
        {
            try
            {
                // --- أعمدة البحث النصي ---
                if (columns == null || columns.Length == 0)
                    return Result<List<T>>.Fail(["لم يتم تحديد أعمدة البحث."]);

                var safeCols = columns
                    .Select(ResolvePropertyToColumn)             // <-- ترجمة property -> column
                    .Where(IsValidSqlIdentifier)                 // تحقّق أمان الاسم
                    .Select(c => $"LOWER([{c}])")                // بحث بدون حساسية حالة الحروف
                    .ToArray();

                if (safeCols.Length == 0)
                    return Result<List<T>>.Fail(["أسماء الأعمدة غير صالحة."]);

                // --- فاليديشن الأعمدة/المعرفات التابعة (اختياريين) ---
                // FirstId
                if (!string.IsNullOrWhiteSpace(firstIdColumn))
                {
                    if (!IsValidSqlIdentifier(firstIdColumn))
                        return Result<List<T>>.Fail(["اسم العمود الأول (FirstIdColumn) غير صالح."]);

                    if (!firstId.HasValue || firstId.Value == Guid.Empty)
                        return Result<List<T>>.Fail(["تم تمرير FirstIdColumn بدون قيمة FirstId صحيحة."]);
                }
                else if (firstId.HasValue && firstId.Value != Guid.Empty)
                {
                    return Result<List<T>>.Fail(["تم تمرير FirstId بدون تحديد FirstIdColumn."]);
                }

                // SecondId
                if (!string.IsNullOrWhiteSpace(secondIdColumn))
                {
                    if (!IsValidSqlIdentifier(secondIdColumn))
                        return Result<List<T>>.Fail(["اسم العمود الثاني (SecondIdColumn) غير صالح."]);

                    if (!secondId.HasValue || secondId.Value == Guid.Empty)
                        return Result<List<T>>.Fail(["تم تمرير SecondIdColumn بدون قيمة SecondId صحيحة."]);
                }
                else if (secondId.HasValue && secondId.Value != Guid.Empty)
                {
                    return Result<List<T>>.Fail(["تم تمرير SecondId بدون تحديد SecondIdColumn."]);
                }

                // --- WHERE parts ---
                var conds = new List<string>();
                var dp = new DynamicParameters();

                // بحث نصّي (لو فيه query)
                if (!string.IsNullOrWhiteSpace(query))
                {
                    var q = query.Trim().ToLowerInvariant();
                    var likeParts = safeCols.Select((col, idx) => $"{col} LIKE @q{idx}").ToList();
                    for (int i = 0; i < likeParts.Count; i++)
                        dp.Add($"@q{i}", $"%{q}%");

                    var likeJoined = string.Join(likeCond == LikeConditions.OR ? " OR " : " AND ", likeParts);
                    conds.Add($"({likeJoined})");
                }

                // IsActive فلترة
                if (isActive.HasValue)
                {
                    conds.Add("[IsActive] = @isActive");
                    dp.Add("@isActive", isActive.Value);
                }

                // FirstId/SecondId
                if (!string.IsNullOrWhiteSpace(firstIdColumn))
                {
                    conds.Add($"[{firstIdColumn}] = @firstId");
                    dp.Add("@firstId", firstId!.Value);
                }

                if (!string.IsNullOrWhiteSpace(secondIdColumn))
                {
                    conds.Add($"[{secondIdColumn}] = @secondId");
                    dp.Add("@secondId", secondId!.Value);
                }

                // بناء SQL
                var whereSql = conds.Count > 0 ? " WHERE " + string.Join(" AND ", conds) : "";

                using var conn = dbFactory.CreateConnection();
                string sql = (maxResults.HasValue && maxResults.Value > 0)
                    ? $"SELECT TOP ({maxResults.Value}) * FROM {tableNameSql}{whereSql}"
                    : $"SELECT * FROM {tableNameSql}{whereSql}";

                var result = (await conn.QueryAsync<T>(sql, dp)).ToList();
                return Result<List<T>>.Ok(result);
            }
            catch
            {
                return Result<List<T>>.Fail(["تعذر تنفيذ البحث."]);
            }
        }

        public async Task<Result<bool>> IsUniqueAsync(string column, object value)
        {
            try
            {
                if (!IsValidSqlIdentifier(column))
                    return Result<bool>.Fail(["اسم العمود غير صالح."]);

                var col = $"[{column}]";
                var sql = $"SELECT 1 FROM {tableNameSql} WHERE {col} = @value";

                var v = value is string s ? s.Trim() : value;

                using var conn = dbFactory.CreateConnection();
                var exists = await conn.ExecuteScalarAsync<int?>(sql, new { value = v });
                return Result<bool>.Ok(!exists.HasValue);
            }
            catch (Exception)
            {
                return Result<bool>.Fail(["تعذر التحقق من التفرد."]);
            }
        }

        public async Task<Result<bool>> UpdateByColumnAsync(string column, object value, object id, string? idColumn = "Id")
        {
            try
            {
                if (!IsValidSqlIdentifier(column) || !IsValidSqlIdentifier(idColumn))
                    return Result<bool>.Fail(["اسم عمود غير صالح."]);

                var col = $"[{column}]";
                var idCol = $"[{idColumn}]";

                var sql = $"UPDATE {tableNameSql} SET {col} = @value WHERE {idCol} = @id";

                var v = value is string s ? s.Trim() : value;

                using var conn = dbFactory.CreateConnection();
                var affected = await conn.ExecuteAsync(sql, new { value = v, id });
                return Result<bool>.Ok(affected > 0);
            }
            catch (Exception)
            {
                return Result<bool>.Fail(["تعذر تعديل البيانات."]);
            }
        }

        // ===== Bulk ops (Z.Dapper.Plus) =====
        public async Task<Result<int>> BulkInsertAsync(IEnumerable<T> entities)
        {
            try
            {
                if (entities == null) return Result<int>.Fail(["القائمة فارغة."]);
                using var conn = dbFactory.CreateConnection();
                // Fix #9: Removed Task.Run — BulkInsert is sync; wrapping it in Task.Run wastes a ThreadPool thread
                conn.BulkInsert(entities);
                return Result<int>.Ok(entities.Count());
            }
            catch (Exception)
            {
                return Result<int>.Fail(["خطأ في الإدخال الجماعي."]);
            }
        }

        public async Task<Result<int>> BulkUpdateAsync(IEnumerable<T> entities)
        {
            try
            {
                if (entities == null) return Result<int>.Fail(["القائمة فارغة."]);
                using var conn = dbFactory.CreateConnection();
                conn.BulkUpdate(entities);
                return Result<int>.Ok(entities.Count());
            }
            catch (Exception)
            {
                return Result<int>.Fail(["خطأ في التحديث الجماعي."]);
            }
        }

        public async Task<Result<int>> BulkDeleteAsync(IEnumerable<T> entities)
        {
            try
            {
                if (entities == null) return Result<int>.Fail(["القائمة فارغة."]);
                using var conn = dbFactory.CreateConnection();
                conn.BulkDelete(entities);
                return Result<int>.Ok(entities.Count());
            }
            catch (Exception)
            {
                return Result<int>.Fail(["خطأ في الحذف الجماعي."]);
            }
        }

        public async Task<Result<int>> BulkMergeAsync(IEnumerable<T> entities)
        {
            try
            {
                if (entities == null) return Result<int>.Fail(["القائمة فارغة."]);
                using var conn = dbFactory.CreateConnection();
                conn.BulkMerge(entities);
                return Result<int>.Ok(entities.Count());
            }
            catch (Exception)
            {
                return Result<int>.Fail(["خطأ في دمج البيانات الجماعي."]);
            }
        }

        // ========== Helpers ==========
        // Fix #11: Whitelist-based validation — only safe identifier characters allowed
        public static bool IsValidSqlIdentifier(string? columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName))
                return false;

            return _safeIdentifierRegex.IsMatch(columnName);
        }

        private static string Qualify(string name)
        {
            // يحوّل "dbo.Table" -> "[dbo].[Table]" ، و"Table" -> "[Table]"
            var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Join(".", parts.Select(p => p.StartsWith("[") && p.EndsWith("]") ? p : $"[{p}]"));
        }

        private static string ResolvePropertyToColumn(string propOrColumn)
        {
            // لو ده اسم property على T، ترجم لاسم العمود (يحترم [DbColumn])
            var p = typeof(T).GetProperty(propOrColumn,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (p != null)
            {
                var colAttr = p.GetCustomAttribute<DbColumnAttribute>();
                var col = string.IsNullOrWhiteSpace(colAttr?.Name) ? p.Name : colAttr!.Name!;
                return col;
            }

            // وإلا اعتبره بالفعل اسم عمود قادم جاهز
            return propOrColumn;
        }

        // ----------------- Logging Helper -----------------
        private void LogSql(string sql, DynamicParameters? dp)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                logger.LogDebug("SQL: {Sql}", sql);
                if (dp != null)
                {
                    var names = dp.ParameterNames;
                    logger.LogDebug("Params: {Names}", string.Join(", ", names));
                }
                sw.Stop();
            }
            catch
            {
                // لا شيء
            }
        }
    }
}