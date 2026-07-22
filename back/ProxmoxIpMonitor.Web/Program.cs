using System.Text.Json.Serialization;
using Elyspio.Utils.Telemetry.Technical.Extensions;
using Elyspio.Utils.Telemetry.Tracing.Builder;
using ProxmoxIpMonitor.Adapters.Dns.Injections;
using ProxmoxIpMonitor.Adapters.Dns.Technitium;
using ProxmoxIpMonitor.Adapters.MongoDB.Injections;
using ProxmoxIpMonitor.Adapters.MongoDB.Repositories;
using ProxmoxIpMonitor.Adapters.Proxmox.Injections;
using ProxmoxIpMonitor.Adapters.Proxmox.Pve;
using ProxmoxIpMonitor.Core.Injections;
using ProxmoxIpMonitor.Core.Services;
using ProxmoxIpMonitor.Web.Auth;
using ProxmoxIpMonitor.Web.Filters;
using ProxmoxIpMonitor.Web.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Environment-specific configuration: a mounted secret in deployment, a gitignored file locally.
builder.Configuration.AddJsonFile("appsettings.docker.json", true, true);
builder.Configuration.AddJsonFile("appsettings.Local.json", true, true);

builder.Host.UseSerilogWithTelemetry();

// Telemetry is owned by Elyspio.Utils.Telemetry: it registers every TracingX activity source
// found in the product assemblies. Under Aspire, OTEL_EXPORTER_OTLP_ENDPOINT takes precedence
// over OpenTelemetry:CollectorUri, so the same build works locally and in the cluster.
if (builder.Configuration.IsTelemetryEnabled(out var telemetryOptions))
{
	var telemetry = new AppOpenTelemetryBuilder<Program>(telemetryOptions!, builder.Configuration);
	telemetry.AddAssembly<Collector>();
	telemetry.AddAssembly<PveApiClient>();
	telemetry.AddAssembly<TechnitiumDnsProvider>();
	telemetry.AddAssembly<NodeRepository>();
	telemetry.Build(builder.Services);
	builder.Services.AddOpenTelemetryJsonConfiguration(builder.Configuration);
}

builder.AddHostingDefaults();

// One Add* per project, wired here and nowhere else.
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddProxmoxAdapter();
builder.Services.AddDnsAdapters();
builder.Services.AddCore();
builder.Services.AddAppAuth(builder.Configuration);

// Enums as names, not integers: the frontend compares against "Vm", "Changed", "Failed".
builder.Services.AddControllers(options => options.Filters.Add<HttpExceptionFilter>())
	.AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS — the SPA is same-origin in production; only the Vite dev server needs an exception.
const string corsPolicy = "spa";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var origins = allowedOrigins.Append("http://localhost:5173").Distinct().ToArray();
builder.Services.AddCors(options => options.AddPolicy(corsPolicy, policy =>
	policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

app.UseOpenTelemetryJsonConfiguration();

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseCors(corsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapDefaultEndpoints();

// SPA fallback — any non-API route serves the React app from wwwroot.
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
