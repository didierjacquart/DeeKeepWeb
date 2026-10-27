using DeeKeep.Common.Hosting;
using DeeKeep.Contracts;
using DeeKeep.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.AddDeeKeepService(ServiceNames.Storage);
builder.Services.AddStorageService(builder.Configuration);

var app = builder.Build();
app.MapDeeKeepDefaults();
app.MapStorageService();
app.Run();
