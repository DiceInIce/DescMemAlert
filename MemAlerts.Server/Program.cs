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
const long maxSignalRMessageSize = 1024L * 1024 * 200; // 200 MB to allow inline video payloads

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = maxSignalRMessageSize;
    options.EnableDetailedErrors = true;
});

// Configuration for Port
builder.Configuration.AddJsonFile("config.json", optional: true, reloadOnChange: true);

// PostgreSQL connection string (config.json ConnectionStrings:PostgreSql or env)
var connectionString =
    builder.Configuration.GetConnectionString("PostgreSql") ??
    builder.Configuration["PostgreSql"] ??
    builder.Configuration["ConnectionStrings:PostgreSql"];

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("PostgreSQL connection string is not configured.");
}

// Register services backed by PostgreSQL + Dapper
builder.Services.AddSingleton<IAuthService>(sp =>
    new PostgresAuthService(connectionString, sp.GetRequiredService<ILogger<PostgresAuthService>>()));

builder.Services.AddSingleton<IFriendService>(sp =>
    new PostgresFriendService(connectionString, sp.GetRequiredService<ILogger<PostgresFriendService>>()));

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

app.MapHub<AlertHub>("/alerthub", options =>
{
    options.ApplicationMaxBufferSize = maxSignalRMessageSize;
    options.TransportMaxBufferSize = maxSignalRMessageSize;
});

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
