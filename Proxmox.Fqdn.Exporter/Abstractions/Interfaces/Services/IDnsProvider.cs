using Proxmox.Fqdn.Exporter.Data;

namespace Proxmox.Fqdn.Exporter.Abstractions.Interfaces.Services;

/// <summary>
///     Exports discovered FQDN records to a DNS provider.
/// </summary>
public interface IDnsProvider
{
	/// <summary>
	///     Gets a value indicating whether this provider is enabled by configuration.
	/// </summary>
	bool IsEnabled { get; }

	/// <summary>
	///     Gets the provider name used in logs.
	/// </summary>
	string Name { get; }

	/// <summary>
	///     Exports the supplied FQDN records.
	/// </summary>
	/// <param name="records">The records to export.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	Task ExportAsync(IReadOnlyCollection<IFqdnWithTimestamp> records, CancellationToken cancellationToken = default);
}
