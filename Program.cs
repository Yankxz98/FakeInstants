using fakeinstants;
using fakeinstants.Services;
using fakeinstants.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register services
// Configure HttpClient dynamically based on environment
builder.Services.AddScoped<HttpClient>(sp =>
{
    var httpClient = new HttpClient();

    // Check if we're in development environment by looking at the host
    var hostEnvironment = builder.HostEnvironment;
    if (hostEnvironment.IsDevelopment())
    {
        // In development, use the server URL from Server launchSettings (port 8999)
        httpClient.BaseAddress = new Uri("http://localhost:8999");
    }
    else
    {
        // In production (Render), use relative URLs since frontend and backend are on same origin
        httpClient.BaseAddress = new Uri("/");
    }

    return httpClient;
});

// Register our custom services
builder.Services.AddScoped<AudioService>();
builder.Services.AddScoped<SoundManager>();
builder.Services.AddScoped<JsonStorageService>();
builder.Services.AddScoped<FolderBasedCategoryService>();
builder.Services.AddScoped<FileMoveService>();

var host = builder.Build();

// Initialize AudioService
var audioService = host.Services.GetRequiredService<AudioService>();
await audioService.InitializeAsync();

await host.RunAsync(); 
