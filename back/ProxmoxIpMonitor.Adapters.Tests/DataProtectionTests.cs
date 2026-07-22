using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using ProxmoxIpMonitor.Adapters.MongoDB.Protection;
using Xunit;

namespace ProxmoxIpMonitor.Adapters.Tests;

public class DataProtectionTests
{
	private static string Key()
	{
		return DataProtectionMasterKey.GenerateBase64();
	}

	[Fact]
	public void AValidBase64KeyIsAccepted()
	{
		var parsed = DataProtectionMasterKey.Parse(Key());

		Assert.Equal(32, parsed.Key.Length);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void AMissingKeyFailsLoudlyInsteadOfBeingInvented(string? configured)
	{
		// Silently generating a key would make every stored token unreadable after a restart,
		// with no signal that anything went wrong.
		var error = Assert.Throws<InvalidOperationException>(() => DataProtectionMasterKey.Parse(configured));

		Assert.Contains("DataProtection:MasterKey", error.Message);
	}

	[Fact]
	public void ANonBase64KeyIsRejected()
	{
		Assert.Throws<InvalidOperationException>(() => DataProtectionMasterKey.Parse("not base64 !!"));
	}

	[Fact]
	public void AKeyOfTheWrongLengthIsRejected()
	{
		var tooShort = Convert.ToBase64String(new byte[16]);

		var error = Assert.Throws<InvalidOperationException>(() => DataProtectionMasterKey.Parse(tooShort));

		Assert.Contains("32 bytes", error.Message);
	}

	[Fact]
	public void KeyRingElementsSurviveAnEncryptDecryptRoundTrip()
	{
		var masterKey = DataProtectionMasterKey.Parse(Key());
		var services = new ServiceCollection().AddSingleton(masterKey).BuildServiceProvider();

		var plaintext = new XElement("key", new XElement("secret", "s3cr3t-token"));

		var encrypted = new AesGcmXmlEncryptor(masterKey).Encrypt(plaintext);
		var decrypted = new AesGcmXmlDecryptor(services).Decrypt(encrypted.EncryptedElement);

		Assert.Equal(plaintext.ToString(), decrypted.ToString());
	}

	[Fact]
	public void CiphertextDoesNotLeakThePlaintext()
	{
		var masterKey = DataProtectionMasterKey.Parse(Key());

		var encrypted = new AesGcmXmlEncryptor(masterKey).Encrypt(new XElement("key", "s3cr3t-token"));

		Assert.DoesNotContain("s3cr3t-token", encrypted.EncryptedElement.ToString());
	}

	[Fact]
	public void ADifferentMasterKeyCannotDecryptTheRing()
	{
		var encrypted = new AesGcmXmlEncryptor(DataProtectionMasterKey.Parse(Key())).Encrypt(new XElement("key", "value"));

		var otherKey = DataProtectionMasterKey.Parse(Key());
		var services = new ServiceCollection().AddSingleton(otherKey).BuildServiceProvider();

		var error = Assert.Throws<InvalidOperationException>(() => new AesGcmXmlDecryptor(services).Decrypt(encrypted.EncryptedElement));

		Assert.Contains("does not match", error.Message);
	}

	[Fact]
	public void TamperedCiphertextIsRejectedRatherThanSilentlyDecoded()
	{
		// AES-GCM authenticates the payload: a flipped byte must fail, not yield garbage.
		var masterKey = DataProtectionMasterKey.Parse(Key());
		var services = new ServiceCollection().AddSingleton(masterKey).BuildServiceProvider();

		var encrypted = new AesGcmXmlEncryptor(masterKey).Encrypt(new XElement("key", "value"));
		var element = XElement.Parse(encrypted.EncryptedElement.ToString());
		var payload = Convert.FromBase64String(element.Element("value")!.Value);
		payload[^1] ^= 0xFF;
		element.Element("value")!.Value = Convert.ToBase64String(payload);

		Assert.Throws<InvalidOperationException>(() => new AesGcmXmlDecryptor(services).Decrypt(element));
	}
}
