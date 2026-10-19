using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using One.Domain.DTOs;
using System.Net;
using System.Text.Json;

namespace One.API.Middleware
{
    public class RateLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IDistributedCache? _distributedCache;
        private readonly IMemoryCache _memoryCache;
        private readonly IHostEnvironment _environment;
        private readonly IConfiguration _configuration;

        public RateLimitMiddleware(
            RequestDelegate next,
            IHostEnvironment environment,
            IMemoryCache memoryCache,
            IConfiguration configuration,
            IDistributedCache? distributedCache = null)
        {
            _next = next;
            _environment = environment;
            _memoryCache = memoryCache;
            _distributedCache = distributedCache;
            _configuration = configuration;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                if (!await IsRequestAllowedAsync(context))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    context.Response.ContentType = "application/json";
                    var response = new { success = false, message = "Too many requests, please try again later", data = "" };
                    await context.Response.WriteAsJsonAsync(response);
                    return;
                }
                await _next(context);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";

                var response = _environment.IsDevelopment() ?
                    new Response() { StatusCode = (int)HttpStatusCode.InternalServerError, Message = ex.StackTrace + ex.Message }
                    : new Response() { StatusCode = (int)HttpStatusCode.InternalServerError, Message = ex.Message };

                var json = JsonSerializer.Serialize(response);
                await context.Response.WriteAsync(json);
            }
        }

        private async Task<bool> IsRequestAllowedAsync(HttpContext context)
        {
            var ip = context.Connection.RemoteIpAddress?.ToString();
            var path = context.Request.Path.ToString();

            if (ip == null || string.IsNullOrWhiteSpace(path))
                return true;

            var cacheKey = $"Rate:{ip}:{path}";
            var rateLimitSettings = _configuration.GetSection("RateLimiting");
            var windowSeconds = rateLimitSettings.GetValue<int>("WindowSeconds", 20);
            var maxRequests = rateLimitSettings.GetValue<int>("MaxRequests", 10);
            var useRedis = rateLimitSettings.GetValue<bool>("UseRedis", false);
            var rateLimitWindow = TimeSpan.FromSeconds(windowSeconds);
            var now = DateTime.UtcNow;

            // Use distributed cache (Redis) if configured, otherwise fallback to memory cache
            if (useRedis && _distributedCache != null)
            {
                return await IsAllowedWithDistributedCacheAsync(_distributedCache, cacheKey, now, rateLimitWindow, maxRequests);
            }
            else
            {
                return IsAllowedWithMemoryCache(cacheKey, now, rateLimitWindow, maxRequests);
            }
        }

        private async Task<bool> IsAllowedWithDistributedCacheAsync(
            IDistributedCache cache, string cacheKey, DateTime now, TimeSpan window, int maxRequests)
        {
            try
            {
                var cachedValue = await cache.GetStringAsync(cacheKey);

                if (cachedValue != null)
                {
                    var entry = JsonSerializer.Deserialize<RateLimitEntry>(cachedValue);
                    if (entry != null && now - entry.FirstRequestTime < window)
                    {
                        if (entry.Count >= maxRequests)
                            return false;

                        entry.Count++;
                        await cache.SetStringAsync(cacheKey,
                            JsonSerializer.Serialize(entry),
                            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = window });
                    }
                    else
                    {
                        var newEntry = new RateLimitEntry { FirstRequestTime = now, Count = 1 };
                        await cache.SetStringAsync(cacheKey,
                            JsonSerializer.Serialize(newEntry),
                            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = window });
                    }
                }
                else
                {
                    var newEntry = new RateLimitEntry { FirstRequestTime = now, Count = 1 };
                    await cache.SetStringAsync(cacheKey,
                        JsonSerializer.Serialize(newEntry),
                        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = window });
                }

                return true;
            }
            catch
            {
                // Fallback to memory cache if Redis fails
                return IsAllowedWithMemoryCache(cacheKey, now, window, maxRequests);
            }
        }

        private bool IsAllowedWithMemoryCache(string cacheKey, DateTime now, TimeSpan window, int maxRequests)
        {
            if (_memoryCache.TryGetValue<(DateTime firstRequestTime, int count)>(cacheKey, out var entry))
            {
                if (now - entry.firstRequestTime < window)
                {
                    if (entry.count >= maxRequests)
                        return false;

                    _memoryCache.Set(cacheKey, (entry.firstRequestTime, entry.count + 1), window);
                }
                else
                {
                    _memoryCache.Set(cacheKey, (now, 1), window);
                }
            }
            else
            {
                _memoryCache.Set(cacheKey, (now, 1), window);
            }

            return true;
        }

        private class RateLimitEntry
        {
            public DateTime FirstRequestTime { get; set; }
            public int Count { get; set; }
        }
    }
}
