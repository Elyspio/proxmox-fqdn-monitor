using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Proxmox.Fqdn.Exporter.Abstractions.Interfaces.Services;
using Proxmox.Fqdn.Exporter.Adapters;
using Proxmox.Fqdn.Exporter.Adapters.Proxmox;
using Proxmox.Fqdn.Exporter.Data;
using Proxmox.Fqdn.Exporter.Options;
using Proxmox.Fqdn.Exporter.Repositories;
using Proxmox.Fqdn.Exporter.Services;
using Serilog;

var now = Stopwatch.GetTimestamp();

var builder = Host.CreateApplicationBuilder();


Log.Logger = new LoggerConfiguration()
	.MinimumLevel.Debug()
	.Enrich.FromLogContext()
	.WriteTo.Console()
	.WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
	.CreateLogger();
builder.Services.AddSerilog();


builder.Logging.AddSystemdConsole().SetMinimumLevel(LogLevel.Debug);

builder.Configuration
	.AddJsonFile("config.json", false, true)
	.AddJsonFile("config.secrets.json", true, true)
	.AddEnvironmentVariables();


builder.Services.Configure<AppConfig>(builder.Configuration);

builder.Services.AddSingleton<JsonAdapter>();
builder.Services.AddSingleton<NetworkAdapter>();
builder.Services.AddSingleton<ProcessAdapter>();

builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<FqdnService>();
builder.Services.AddSingleton<HttpClient>();
builder.Services.AddSingleton<IDnsProvider, PiholeService>();
builder.Services.AddSingleton<IDnsProvider, TechnitiumDnsService>();
builder.Services.AddSingleton<WorkflowService>();

builder.Services.AddSingleton<ContainerProxmoxAdapter>();
builder.Services.AddSingleton<VmProxmoxAdapter>();

builder.Services.AddSingleton(sp => new FqdnRepository("fqdn.db", sp.GetRequiredService<ILogger<FqdnRepository>>()));

var host = builder.Build();


var configService = host.Services.GetRequiredService<ConfigService>();
configService.Verify();

await host.Services.GetRequiredService<WorkflowService>().Run();
