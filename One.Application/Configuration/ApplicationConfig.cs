using Microsoft.Extensions.DependencyInjection;
using One.Application.Base;
using One.Application.Services.Mapping;
using One.Domain.Entities.Sales;
using System.Reflection;

namespace One.Application.Configuration
{
    public static class ApplicationConfig
    {
        /// <summary>
        /// يسجل كل Repository أو Service في Assembly معين أو Namespace معين تلقائيًا في الـ DI
        /// </summary>
        public static IServiceCollection AddOneApplication(this IServiceCollection services, Assembly? assembly = null)
        {
            assembly ??= Assembly.GetExecutingAssembly();

            // يسجل أي كلاس concrete بيرث من BaseRepository<,>
            var repoBaseType = typeof(BaseRepository<,>);
            var repoTypes = assembly.GetTypes()
                .Where(t =>
                    !t.IsAbstract &&
                    !t.IsInterface &&
                    IsSubclassOfRawGeneric(repoBaseType, t)
                ).ToList();

            foreach (var repoType in repoTypes)
            {
                services.AddScoped(repoType);
            }

            services.AddSingleton<IDynamicMappingService, DynamicMappingService>();

            return services;
        }

        // Helper function to detect generic base class
        private static bool IsSubclassOfRawGeneric(Type generic, Type toCheck)
        {
            while (toCheck != null && toCheck != typeof(object))
            {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == cur)
                    return true;
                toCheck = toCheck.BaseType!;
            }
            return false;
        }
    }
}