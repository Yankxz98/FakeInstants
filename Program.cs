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
// Configure HttpClient to point to the server (not the client host)
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("http://localhost:8999") });

// Register our custom services
builder.Services.AddScoped<AudioService>();
builder.Services.AddScoped<SoundManager>();
builder.Services.AddSingleton<JsonStorageService>();
builder.Services.AddScoped<FolderBasedCategoryService>();
builder.Services.AddScoped<FileMoveService>();

var host = builder.Build();

// Initialize AudioService
var audioService = host.Services.GetRequiredService<AudioService>();
await audioService.InitializeAsync();

await host.RunAsync(); 
