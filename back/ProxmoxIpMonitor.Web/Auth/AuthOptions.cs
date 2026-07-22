using System.ComponentModel.DataAnnotations;

namespace ProxmoxIpMonitor.Web.Auth;

/// <summary>OIDC bearer validation settings. Validated at startup, never defaulted silently.</summary>
public sealed class AuthOptions
{
	public const string SectionName = "Auth";

	/// <summary>Realm issuer, e.g. https://auth.elyspio.fr/realms/internal.</summary>
	[Required]
	public string Authority { get; set; } = "";

	/// <summary>Expected audience — the Keycloak client id.</summary>
	[Required]
	public string Audience { get; set; } = "";

	/// <summary>
	///     Realm role required to use the application at all, reads included.
	///     The UI exposes hypervisor API tokens and can rewrite a DNS zone, so there is no
	///     meaningful "read-only" tier to hand to every account in the realm.
	/// </summary>
	[Required]
	public string AdminRole { get; set; } = "proxmox-ip-monitor-admin";
}
