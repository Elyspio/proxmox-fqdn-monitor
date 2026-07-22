using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ProxmoxIpMonitor.Web.Hosting;

/// <summary>
///     The non-telemetry half of what used to be the ServiceDefaults project: service discovery,
///     HTTP resilience and health endpoints. Telemetry is owned by Elyspio.Utils.Telemetry,
///     wired in Program.cs.
/// </summary>
public static class HostingModule
{
	private const string HealthEndpointPath = "/health";
	private const string AlivenessEndpointPath = "/alive";

	public static WebApplicationBuilder AddHostingDefaults(this WebApplicationBuilder builder)
	{
		builder.Services.AddHealthChecks()
			.AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

		builder.Services.AddServiceDiscovery();

		builder.Services.ConfigureHttpClientDefaults(http =>
		{
			http.AddStandardResilienceHandler();
			http.AddServiceDiscovery();
		});

		return builder;
	}

	public static WebApplication MapDefaultEndpoints(this WebApplication app)
	{
		// Exposed in every environment and left anonymous: Kubernetes probes them, and they
		// reveal nothing beyond whether the process is up.
		app.MapHealthChecks(HealthEndpointPath).AllowAnonymous();

		app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
		{
			Predicate = r => r.Tags.Contains("live")
		}).AllowAnonymous();

		return app;
	}
}
