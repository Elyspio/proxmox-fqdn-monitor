namespace ProxmoxIpMonitor.Abstractions.Models;

/// <summary>
///     A Proxmox hypervisor to poll. Declared through the UI and stored in Mongo, so the
///     API token secret is held encrypted (see ISecretProtector) and never leaves the backend.
/// </summary>
public sealed record PveNode
{
	public string? Id { get; init; }

	/// <summary>Label shown in the UI.</summary>
	public required string DisplayName { get; init; }

	/// <summary>Base URL of the Proxmox API, e.g. https://10.0.0.10:8006.</summary>
	public required string BaseUrl { get; init; }

	/// <summary>Node name as Proxmox knows it, used to build /nodes/{node}/... paths.</summary>
	public required string NodeName { get; init; }

	/// <summary>Token identity in the form user@realm!tokenid.</summary>
	public required string TokenId { get; init; }

	/// <summary>Token secret, protected at rest. Never serialised to the API.</summary>
	public required string TokenSecretProtected { get; init; }

	/// <summary>
	///     Skips certificate validation for this node. Proxmox ships a self-signed certificate on 8006;
	///     turning this on means the token is sent to whatever answers on that address.
	/// </summary>
	public bool AllowInvalidCertificate { get; init; }

	public bool Enabled { get; init; } = true;
}
