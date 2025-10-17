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
// Configure HttpClient to use the same origin (works for both dev and production)
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

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
