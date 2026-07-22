using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using ProxmoxIpMonitor.Abstractions.Interfaces;

namespace ProxmoxIpMonitor.Adapters.MongoDB.Protection;

/// <summary>
///     Encrypts the API tokens the UI writes into Mongo.
///     The purpose is narrow: a database dump or a stray backup must not hand over read access
///     to every hypervisor. It is not a defence against someone who already holds the master key.
/// </summary>
public sealed class SecretProtector(IDataProtectionProvider provider, ILogger<SecretProtector> logger)
	: TracingAdapter(logger), ISecretProtector
{
	private readonly IDataProtector _protector = provider.CreateProtector("ProxmoxIpMonitor.ApiTokens.v1");

	public string Protect(string plaintext)
	{
		// Both the input and the output are secret material: neither ever goes through Log.F.
		using var trace = LogAdapter();

		ArgumentNullException.ThrowIfNull(plaintext);
		return plaintext.Length == 0 ? "" : _protector.Protect(plaintext);
	}

	public string Unprotect(string ciphertext)
	{
		using var trace = LogAdapter();

		if (string.IsNullOrEmpty(ciphertext))
			throw new InvalidOperationException("No API token is stored. Enter it in the settings screen.");

		return _protector.Unprotect(ciphertext);
	}
}
