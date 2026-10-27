using DeeKeep.Common.Hosting;
using DeeKeep.Contracts;
using DeeKeep.Gateway.Infrastructure;
using HotChocolate.AspNetCore;

[assembly: HotChocolate.Module("GatewayTypes")]

var builder = WebApplication.CreateBuilder(args);
builder.AddDeeKeepService(ServiceNames.Gateway);
builder.Services.AddSingleton<ServiceDirectory>();
builder.Services.AddEventRelay();
builder.Services.AddDeeKeepAuth(builder.Configuration);
builder.Services.AddDeeKeepProxy(builder.Configuration);
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);

builder.Services.AddDeeKeepGraphQL(builder.Environment.IsDevelopment());

var app = builder.Build();
app.UseResponseCompression();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseDeeKeepAuthGate();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapDeeKeepDefaults();
app.MapAuthEndpoints();
app.MapGraphQL();
app.MapReverseProxy();
// Application monopage : toute autre route renvoie la coquille Angular.
app.MapFallbackToFile("index.html");
app.Run();
