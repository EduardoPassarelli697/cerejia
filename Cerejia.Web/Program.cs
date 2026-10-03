using Blazored.LocalStorage;
using Cerejia.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Cerejia.Web.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiUrl = builder.Configuration["ApiUrl"] ?? "http://localhost:5000/";

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(apiUrl),
    Timeout = TimeSpan.FromMinutes(5)
});

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddSingleton<AuthStateService>();
builder.Services.AddScoped<ApiService>();

var app = builder.Build();

var auth = app.Services.GetRequiredService<AuthStateService>();
var storage = app.Services.GetRequiredService<ILocalStorageService>();

try
{
    auth.Token = await storage.GetItemAsync<string>("token");
    auth.EmpresaNome = await storage.GetItemAsync<string>("nome");
    auth.Cnpj = await storage.GetItemAsync<string>("cnpj");
    var empresaIdStr = await storage.GetItemAsync<string>("empresaId");
    if (Guid.TryParse(empresaIdStr, out var eid))
        auth.EmpresaId = eid;
    var usuarioIdStr = await storage.GetItemAsync<string>("usuarioId");
    if (Guid.TryParse(usuarioIdStr, out var uid))
        auth.UsuarioId = uid;
    auth.NomeUsuario = await storage.GetItemAsync<string>("nomeUsuario");
    auth.Supervisor = await storage.GetItemAsync<string>("supervisor") == "True";

}
catch {  }

await app.RunAsync();
