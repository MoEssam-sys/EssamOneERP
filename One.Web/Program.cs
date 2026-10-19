using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using One.Shared.Configuration;
using One.Shared.Services;
using One.Shared.Services.Logging;
using One.Shared.Services.Platform;
using One.Web;
using One.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddOneShared();
builder.Services.AddScoped<IClientStorage, BrowserClientStorage>();
builder.Services.AddScoped<IClientLogger, BrowserClientLogger>();
builder.Services.AddScoped(_ =>
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7211/";
    var handler = new ApiAuthorizationHandler
    {
        InnerHandler = new HttpClientHandler()
    };

    return new HttpClient(handler)
    {
        BaseAddress = new Uri(apiBaseUrl),
        Timeout = TimeSpan.FromSeconds(30)
    };
});

await builder.Build().RunAsync();
