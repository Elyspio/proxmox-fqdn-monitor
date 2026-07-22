using System.ComponentModel.DataAnnotations;
using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Abstractions.Transports;

/// <summary>
///     A node as the API exposes it. The token secret is deliberately absent: it goes in, never out.
///     <see cref="HasToken" /> is what lets the UI render a "leave blank to keep" field.
/// </summary>
public sealed record NodeDto
{
	public required string Id { get; init; }

	public required string DisplayName { get; init; }

	public required string BaseUrl { get; init; }

	public required string NodeName { get; init; }

	public required string TokenId { get; init; }

	public required bool HasToken { get; init; }

	public required bool AllowInvalidCertificate { get; init; }

	public required bool Enabled { get; init; }

	public static NodeDto From(PveNode node)
	{
		return new NodeDto
		{
			Id = node.Id ?? "",
			DisplayName = node.DisplayName,
			BaseUrl = node.BaseUrl,
			NodeName = node.NodeName,
			TokenId = node.TokenId,
			HasToken = !string.IsNullOrEmpty(node.TokenSecretProtected),
			AllowInvalidCertificate = node.AllowInvalidCertificate,
			Enabled = node.Enabled
		};
	}
}

public sealed record NodeWriteDto
{
	[Required] [MinLength(1)] public string DisplayName { get; init; } = "";

	[Required] [Url] public string BaseUrl { get; init; } = "";

	[Required] [MinLength(1)] public string NodeName { get; init; } = "";

	/// <summary>Token identity, e.g. monitor@pve!ip-monitor.</summary>
	[Required]
	[MinLength(1)]
	public string TokenId { get; init; } = "";

	/// <summary>Null or empty on update keeps the stored secret.</summary>
	public string? TokenSecret { get; init; }

	public bool AllowInvalidCertificate { get; init; }

	public bool Enabled { get; init; } = true;
}

public sealed record TechnitiumDto
{
	public required bool Enabled { get; init; }

	public required string BaseUrl { get; init; }

	public required bool HasApiToken { get; init; }

	public required string Zone { get; init; }

	public string? PrimaryNode { get; init; }

	public required int RecordTtlSeconds { get; init; }

	public required bool CreatePtr { get; init; }
}

public sealed record SettingsDto
{
	public required TimeSpan PollInterval { get; init; }

	public required IReadOnlyList<string> SubnetsFilter { get; init; }

	public required int RetentionMinutes { get; init; }

	public required IReadOnlyList<string> ExcludedHostnames { get; init; }

	public required bool ReconciliationEnabled { get; init; }

	public required bool DeleteOrphanRecords { get; init; }

	public required int JournalRetentionDays { get; init; }

	public required TechnitiumDto Technitium { get; init; }

	public static SettingsDto From(AppSettings settings)
	{
		return new SettingsDto
		{
			PollInterval = settings.PollInterval,
			SubnetsFilter = settings.SubnetsFilter,
			RetentionMinutes = settings.RetentionMinutes,
			ExcludedHostnames = settings.ExcludedHostnames,
			ReconciliationEnabled = settings.ReconciliationEnabled,
			DeleteOrphanRecords = settings.DeleteOrphanRecords,
			JournalRetentionDays = settings.JournalRetentionDays,
			Technitium = new TechnitiumDto
			{
				Enabled = settings.Technitium.Enabled,
				BaseUrl = settings.Technitium.BaseUrl,
				HasApiToken = !string.IsNullOrEmpty(settings.Technitium.ApiTokenProtected),
				Zone = settings.Technitium.Zone,
				PrimaryNode = settings.Technitium.PrimaryNode,
				RecordTtlSeconds = settings.Technitium.RecordTtlSeconds,
				CreatePtr = settings.Technitium.CreatePtr
			}
		};
	}
}

public sealed record TechnitiumWriteDto
{
	public bool Enabled { get; init; }

	public string BaseUrl { get; init; } = "";

	/// <summary>Null or empty keeps the stored token.</summary>
	public string? ApiToken { get; init; }

	public string Zone { get; init; } = "";

	public string? PrimaryNode { get; init; }

	[Range(1, 86400)] public int RecordTtlSeconds { get; init; } = 300;

	public bool CreatePtr { get; init; }
}

public sealed record SettingsWriteDto
{
	public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(1);

	public IReadOnlyList<string> SubnetsFilter { get; init; } = ["10.0.0.0/8"];

	[Range(1, 525600)] public int RetentionMinutes { get; init; } = 300;

	public IReadOnlyList<string> ExcludedHostnames { get; init; } = [];

	public bool ReconciliationEnabled { get; init; }

	public bool DeleteOrphanRecords { get; init; }

	[Range(1, 3650)] public int JournalRetentionDays { get; init; } = 90;

	public TechnitiumWriteDto Technitium { get; init; } = new();
}

/// <summary>Outcome of probing a node's reachability, TLS and token.</summary>
public sealed record NodeTestResultDto(bool Success, string Message);

public sealed record HostPatchDto
{
	public bool? Pinned { get; init; }

	public bool? Excluded { get; init; }
}
