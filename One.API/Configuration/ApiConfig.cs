using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using One.Application.Repos.Identity;
using One.Application.Services;
using One.Domain.Entities.Identity;
using One.Infrastructure.DbContext.UsersDbContext;
using One.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text;

namespace One.API.Configuration
{
    public static class ApiConfig
    {
        public static IServiceCollection AddOneAPI(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSignalR();

            // Controllers
            services.AddControllers();

            services.AddMemoryCache();

            // DbContext
            services.AddDbContext<UsersDbContext>(options =>
            {
                options.UseSqlServer(
                    configuration.GetConnectionString("UserConnection"),
                    b => b.MigrationsAssembly(typeof(UsersDbContext).Assembly.FullName));
            });

            // Identity
            services.AddIdentity<UserData, IdentityRole>()
                .AddEntityFrameworkStores<UsersDbContext>()
                .AddDefaultTokenProviders();

            // JWT Auth - RequireHttpsMetadata from config
            var requireHttps = configuration.GetValue<bool>("Security:RequireHttpsMetadata", true);

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.SaveToken = true;
                options.RequireHttpsMetadata = requireHttps;
                options.TokenValidationParameters = new TokenValidationParameters()
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = configuration["JWT:ValidIssuer"],
                    ValidAudience = configuration["JWT:ValidAudience"],
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(configuration["JWT:Secret"]
                            ?? throw new InvalidOperationException("JWT:Secret configuration is missing.")))
                };
            });

            // CORS - Configurable origins from CorsSettings section
            var corsSettings = configuration.GetSection("CorsSettings");
            var allowedOrigins = corsSettings.GetSection("AllowedOrigins").Get<string[]>() ?? [];
            var allowCredentials = corsSettings.GetValue<bool>("AllowCredentials", false);

            services.AddCors(options =>
            {
                options.AddPolicy("MyCors", policy =>
                {
                    if (allowedOrigins.Length > 0)
                        policy.WithOrigins(allowedOrigins);
                    else
                        // Fallback for development if no origins configured
                        policy.AllowAnyOrigin();

                    policy.AllowAnyMethod();
                    policy.AllowAnyHeader();

                    if (allowCredentials && allowedOrigins.Length > 0)
                        policy.AllowCredentials();
                });
            });

            // Auth Repository & Service (UnitOfWork removed)
            services.AddScoped<IAuthRepository, AuthRepository>();
            services.AddScoped<IAuthService, AuthService>();

            // Localization
            services.AddSingleton<One.Domain.Localization.ITranslator, One.API.Services.ServerTranslator>();

            return services;
        }

        public static async Task SeedRolesAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();

            // Auto-Migrate ONLY if database exists
            var context = scope.ServiceProvider.GetRequiredService<UsersDbContext>();

            // Check if we can connect (Database must exist)
            if (!await context.Database.CanConnectAsync())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogCritical("Users Database does not exist! Please create the database manually using the connection string in configuration. The application will not create it automatically.");
                // We stop here to match requirement "Stop if UsersDb doesn't exist"
                // Throwing exception ensures the app doesn't start in a broken state
                throw new Exception("Users Database does not exist. Please create it manually.");
            }

            // Database exists, apply any pending migrations
            try
            {
                await context.Database.MigrateAsync();
            }
            catch (Exception ex)
            {
                // Just in case MigrateAsync tries to do something prohibited, though rare if DB exists
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "Error while applying migrations to existing database.");
                throw;
            }

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var roles = new[] { "Admin", "User", "SuperAdmin", "CompanyAdmin", "Tester" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }
        }
    }
}
