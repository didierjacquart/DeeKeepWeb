using DeeKeep.Common.Hosting;
using DeeKeep.Contracts;
using DeeKeep.Catalog;

var builder = WebApplication.CreateBuilder(args);
builder.AddDeeKeepService(ServiceNames.Catalog);
builder.Services.AddCatalogService(builder.Configuration);

var app = builder.Build();
app.MapDeeKeepDefaults();
app.MapCatalogService();
app.Run();
