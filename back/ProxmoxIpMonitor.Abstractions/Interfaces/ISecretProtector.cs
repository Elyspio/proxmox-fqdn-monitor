namespace ProxmoxIpMonitor.Abstractions.Interfaces;

/// <summary>
///     Encrypts the API tokens that the UI writes into Mongo. Backed by ASP.NET Data Protection,
///     whose key ring lives in Mongo and is itself wrapped with a master key from the environment.
/// </summary>
public interface ISecretProtector
{
	string Protect(string plaintext);

	/// <summary>Throws when the ciphertext cannot be read — a lost or rotated master key must fail loudly.</summary>
	string Unprotect(string ciphertext);
}
