using DeeKeep.Common.Hosting;
using DeeKeep.Contracts;
using DeeKeep.Library;
using DeeKeep.Library.Organize;

var builder = WebApplication.CreateBuilder(args);
builder.AddDeeKeepService(ServiceNames.Library);
builder.Services.AddLibraryService(builder.Configuration);
builder.Services.AddOrganizeService(builder.Configuration);

var app = builder.Build();
app.MapDeeKeepDefaults();
app.MapLibraryService();
app.MapOrganizeService();
app.Run();
