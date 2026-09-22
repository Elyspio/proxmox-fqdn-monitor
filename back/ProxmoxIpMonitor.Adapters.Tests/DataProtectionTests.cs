using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using ProxmoxIpMonitor.Adapters.MongoDB.Protection;
using Shouldly;
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

		parsed.Key.Length.ShouldBe(32);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void AMissingKeyFailsLoudlyInsteadOfBeingInvented(string? configured)
	{
		// Silently generating a key would make every stored token unreadable after a restart,
		// with no signal that anything went wrong.
		var error = Should.Throw<InvalidOperationException>(() => DataProtectionMasterKey.Parse(configured));

		error.Message.ShouldContain("DataProtection:MasterKey");
	}

	[Fact]
	public void ANonBase64KeyIsRejected()
	{
		Should.Throw<InvalidOperationException>(() => DataProtectionMasterKey.Parse("not base64 !!"));
	}

	[Fact]
	public void AKeyOfTheWrongLengthIsRejected()
	{
		var tooShort = Convert.ToBase64String(new byte[16]);

		var error = Should.Throw<InvalidOperationException>(() => DataProtectionMasterKey.Parse(tooShort));

		error.Message.ShouldContain("32 bytes");
	}

	[Fact]
	public void KeyRingElementsSurviveAnEncryptDecryptRoundTrip()
	{
		var masterKey = DataProtectionMasterKey.Parse(Key());
		var services = new ServiceCollection().AddSingleton(masterKey).BuildServiceProvider();

		var plaintext = new XElement("key", new XElement("secret", "s3cr3t-token"));

		var encrypted = new AesGcmXmlEncryptor(masterKey).Encrypt(plaintext);
		var decrypted = new AesGcmXmlDecryptor(services).Decrypt(encrypted.EncryptedElement);

		decrypted.ToString().ShouldBe(plaintext.ToString());
	}

	[Fact]
	public void CiphertextDoesNotLeakThePlaintext()
	{
		var masterKey = DataProtectionMasterKey.Parse(Key());

		var encrypted = new AesGcmXmlEncryptor(masterKey).Encrypt(new XElement("key", "s3cr3t-token"));

		encrypted.EncryptedElement.ToString().ShouldNotContain("s3cr3t-token");
	}

	[Fact]
	public void ADifferentMasterKeyCannotDecryptTheRing()
	{
		var encrypted = new AesGcmXmlEncryptor(DataProtectionMasterKey.Parse(Key())).Encrypt(new XElement("key", "value"));

		var otherKey = DataProtectionMasterKey.Parse(Key());
		var services = new ServiceCollection().AddSingleton(otherKey).BuildServiceProvider();

		var error = Should.Throw<InvalidOperationException>(() => new AesGcmXmlDecryptor(services).Decrypt(encrypted.EncryptedElement));

		error.Message.ShouldContain("does not match");
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

		Should.Throw<InvalidOperationException>(() => new AesGcmXmlDecryptor(services).Decrypt(element));
	}
}
