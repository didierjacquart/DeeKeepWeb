using DeeKeep.Common.Hosting;
using DeeKeep.Contracts;
using DeeKeep.Gateway.Api;
using DeeKeep.Gateway.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddDeeKeepService(ServiceNames.Gateway);
builder.Services.AddSingleton<ServiceDirectory>();
builder.Services.AddDeeKeepApi();
builder.Services.AddEventRelay();
builder.Services.AddDeeKeepAuth(builder.Configuration);
builder.Services.AddDeeKeepProxy(builder.Configuration);
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);

var app = builder.Build();
app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseDeeKeepAuthGate();
app.UseDeeKeepPermissions();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapDeeKeepDefaults(publicApi: true);
app.MapAuthEndpoints();
app.MapDeeKeepApi();
app.MapReverseProxy();
// Application monopage : toute autre route renvoie la coquille du client React.
app.MapFallbackToFile("index.html");
app.Run();
