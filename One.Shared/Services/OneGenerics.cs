using MudBlazor;
using System.Text.Json;

namespace One.Shared.Services
{
    public static class OneGenerics
    {
        //public readonly HttpClient _http;

        //public GenericService(IHttpClientFactory http, ILanguageContainerService localizer, ISnackbar msg)
        //{
        //    _http = http.CreateClient("AuthorizedHttpClient");
        //    _localizer = localizer;
        //    _msg = msg;
        //}

        public static async Task<T> DeepCopy<T>(T obj)
        {
            var json = JsonSerializer.Serialize(obj);
            return await Task.FromResult(JsonSerializer.Deserialize<T>(json)!);
        }

        //var filtered = await res.Items.SearchValueAsync(
        //    value,
        //    _msg,
        //    nameof(ShipmentPort.Code),
        //    nameof(ShipmentPort.EngDesc),
        //    nameof(ShipmentPort.ArbDesc)
        //);
        public static async Task<List<T>> SearchValueAsync<T>(
        this List<T> sourceList,
        string? value,
        params string[] properties)
        {
            if (string.IsNullOrEmpty(value))
                return sourceList;

            try
            {
                var type = typeof(T);
                // جلب الـ PropertyInfo مرة واحدة لكل خاصية مطلوبة
                var props = properties
                    .Select(p => type.GetProperty(p))
                    .Where(p => p != null)
                    .ToArray();

                if (props.Length == 0)
                    return [];

                var result = sourceList.Where(item =>
                    props.Any(prop =>
                    {
                        var propValue = prop!.GetValue(item)?.ToString();
                        return propValue != null && propValue.Contains(value, StringComparison.InvariantCultureIgnoreCase);
                    })).ToList();

                return await Task.FromResult(result);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error during search operation with value '{value}'", ex);
            }
        }

        public static async Task<List<T>> SearchValueAsync<T>(
        this IEnumerable<T> sourceList,
        string? value,
        params string[] properties)
        {
            if (string.IsNullOrEmpty(value))
                return sourceList.ToList();

            try
            {
                var type = typeof(T);
                // جلب الـ PropertyInfo مرة واحدة فقط
                var props = properties
                    .Select(p => type.GetProperty(p))
                    .Where(p => p != null)
                    .ToArray();

                if (props.Length == 0)
                    return [];

                var result = sourceList.Where(item =>
                    props.Any(prop =>
                    {
                        var propValue = prop!.GetValue(item)?.ToString();
                        return propValue != null && propValue.Contains(value, StringComparison.InvariantCultureIgnoreCase);
                    })).ToList();

                return await Task.FromResult(result);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error during search operation with value '{value}'", ex);
            }
        }
    }
}