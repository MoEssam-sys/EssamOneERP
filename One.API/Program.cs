using One.API.Configuration;
using One.API.Hub;
using One.API.Middleware;
using One.Application.Configuration;
using One.Infrastructure.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Register all services via your extension methods
builder.Services.AddOneAPI(builder.Configuration);
builder.Services.AddOneApplication();
builder.Services.AddOneInfrastructure();

var app = builder.Build();

// Pipeline
app.UseMiddleware<RateLimitMiddleware>();

app.UseHttpsRedirection();
app.UseCors("MyCors");

// JWT pipeline (لو أنت مفعّله داخل AddOneAPI لازم دول هنا)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<OneHub>("/OneHubs");

// Seed Roles
await ApiConfig.SeedRolesAsync(app.Services);

app.Run();