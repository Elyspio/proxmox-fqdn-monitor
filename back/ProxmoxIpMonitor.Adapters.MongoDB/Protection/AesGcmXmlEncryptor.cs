using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace ProxmoxIpMonitor.Adapters.MongoDB.Protection;

/// <summary>
///     Wraps Data Protection key-ring elements with AES-GCM under the environment-supplied master key.
///     The built-in encryptors bind to DPAPI or a certificate; neither fits a Linux container whose
///     key ring lives in Mongo and is backed up with the database.
/// </summary>
public sealed class AesGcmXmlEncryptor(DataProtectionMasterKey masterKey) : IXmlEncryptor
{
	internal const string ElementName = "encryptedKey";
	private const int NonceSize = 12;
	private const int TagSize = 16;

	public EncryptedXmlInfo Encrypt(XElement plaintextElement)
	{
		ArgumentNullException.ThrowIfNull(plaintextElement);

		var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));

		var nonce = new byte[NonceSize];
		RandomNumberGenerator.Fill(nonce);

		var ciphertext = new byte[plaintext.Length];
		var tag = new byte[TagSize];

		using (var aes = new AesGcm(masterKey.Key, TagSize))
		{
			aes.Encrypt(nonce, plaintext, ciphertext, tag);
		}

		// nonce || tag || ciphertext, so the layout is self-describing at fixed offsets.
		var payload = new byte[NonceSize + TagSize + ciphertext.Length];
		nonce.CopyTo(payload, 0);
		tag.CopyTo(payload, NonceSize);
		ciphertext.CopyTo(payload, NonceSize + TagSize);

		var element = new XElement(ElementName,
			new XComment(" Encrypted with the DataProtection:MasterKey. Losing that key makes this unreadable. "),
			new XElement("value", Convert.ToBase64String(payload)));

		return new EncryptedXmlInfo(element, typeof(AesGcmXmlDecryptor));
	}
}

/// <summary>Counterpart of <see cref="AesGcmXmlEncryptor" />. Instantiated by Data Protection from the key ring.</summary>
public sealed class AesGcmXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
	private const int NonceSize = 12;
	private const int TagSize = 16;

	public XElement Decrypt(XElement encryptedElement)
	{
		ArgumentNullException.ThrowIfNull(encryptedElement);

		var masterKey = services.GetRequiredService<DataProtectionMasterKey>();

		var encoded = encryptedElement.Element("value")?.Value
		              ?? throw new InvalidOperationException("Malformed encrypted key element: missing <value>.");

		var payload = Convert.FromBase64String(encoded);
		if (payload.Length < NonceSize + TagSize)
			throw new InvalidOperationException("Malformed encrypted key element: payload too short.");

		var nonce = payload.AsSpan(0, NonceSize);
		var tag = payload.AsSpan(NonceSize, TagSize);
		var ciphertext = payload.AsSpan(NonceSize + TagSize);
		var plaintext = new byte[ciphertext.Length];

		try
		{
			using var aes = new AesGcm(masterKey.Key, TagSize);
			aes.Decrypt(nonce, ciphertext, tag, plaintext);
		}
		catch (CryptographicException e)
		{
			throw new InvalidOperationException(
				"Could not decrypt the Data Protection key ring. DataProtection:MasterKey does not match the key that wrote it; stored API tokens must be re-entered.", e);
		}

		return XElement.Parse(Encoding.UTF8.GetString(plaintext));
	}
}
