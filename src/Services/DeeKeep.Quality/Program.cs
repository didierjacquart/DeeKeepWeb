using DeeKeep.Common.Hosting;
using DeeKeep.Contracts;
using DeeKeep.Quality;

var builder = WebApplication.CreateBuilder(args);
builder.AddDeeKeepService(ServiceNames.Quality);
builder.Services.AddQualityService(builder.Configuration);

var app = builder.Build();
app.MapDeeKeepDefaults();
app.MapQualityService();
app.Run();
