using DeeKeep.Common.Hosting;
using DeeKeep.Contracts;
using DeeKeep.Downloader;

var builder = WebApplication.CreateBuilder(args);
builder.AddDeeKeepService(ServiceNames.Downloader);
builder.Services.AddDownloaderService(builder.Configuration);

var app = builder.Build();
app.MapDeeKeepDefaults();
app.MapDownloaderService();
app.Run();
