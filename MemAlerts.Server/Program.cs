using MemAlerts.Server.Hubs;
using MemAlerts.Server.Services;
using MemAlerts.Shared.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddSignalR();

// Register existing services
builder.Services.AddSingleton<IAuthService>(sp => new FileAuthService(
    sp.GetRequiredService<ILogger<FileAuthService>>(), 
    null));

builder.Services.AddSingleton<IFriendService>(sp => new FileFriendService(
    sp.GetRequiredService<IAuthService>(),
    sp.GetRequiredService<ILogger<FileFriendService>>(),
    null));

// Configuration for Port
builder.Configuration.AddJsonFile("config.json", optional: true, reloadOnChange: true);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Retrieve port from config or use default
var port = app.Configuration.GetValue<int>("ServerPort", 5050);
if (args.Length > 0 && int.TryParse(args[0], out var customPort))
{
    port = customPort;
}

app.Urls.Add($"http://*:{port}");

app.MapHub<AlertHub>("/alerthub");

Log.Information("Starting MemAlerts SignalR Server on port {Port}...", port);

try 
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Server terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
