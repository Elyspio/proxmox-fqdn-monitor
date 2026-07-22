using System.Security.Cryptography;

namespace ProxmoxIpMonitor.Adapters.MongoDB.Protection;

/// <summary>
///     The 256-bit key that wraps the Data Protection key ring stored in Mongo.
///     Supplied through configuration (a Helm Secret in deployment) and never persisted:
///     losing it makes every stored API token unreadable, which is the intended blast radius.
/// </summary>
public sealed class DataProtectionMasterKey
{
	public const string ConfigurationKey = "DataProtection:MasterKey";

	private DataProtectionMasterKey(byte[] key)
	{
		Key = key;
	}

	public byte[] Key { get; }

	/// <summary>Parses and validates the configured key, failing loudly rather than inventing one.</summary>
	public static DataProtectionMasterKey Parse(string? configured)
	{
		if (string.IsNullOrWhiteSpace(configured))
			throw new InvalidOperationException(
				$"{ConfigurationKey} is not configured. Generate one with: openssl rand -base64 32");

		byte[] key;
		try
		{
			key = Convert.FromBase64String(configured.Trim());
		}
		catch (FormatException e)
		{
			throw new InvalidOperationException($"{ConfigurationKey} must be base64-encoded.", e);
		}

		if (key.Length != 32)
			throw new InvalidOperationException($"{ConfigurationKey} must decode to exactly 32 bytes, got {key.Length}.");

		return new DataProtectionMasterKey(key);
	}

	/// <summary>Generates a key for local development, so `aspire run` works without setup.</summary>
	public static string GenerateBase64()
	{
		var key = new byte[32];
		RandomNumberGenerator.Fill(key);
		return Convert.ToBase64String(key);
	}
}
