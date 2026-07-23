using Microsoft.Extensions.Logging.Abstractions;
using ProxmoxIpMonitor.Abstractions.Exceptions;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Abstractions.Transports;
using ProxmoxIpMonitor.Core.Services;
using Xunit;

namespace ProxmoxIpMonitor.Core.Tests;

public class SettingsServiceTests
{
	private static SettingsWriteDto Write(TechnitiumWriteDto? technitium = null, IReadOnlyList<SubnetWriteDto>? subnets = null)
	{
		return new SettingsWriteDto
		{
			SubnetsFilter = subnets ?? [new SubnetWriteDto { Cidr = "10.0.0.0/8" }],
			Technitium = technitium ?? new TechnitiumWriteDto()
		};
	}

	private static (SettingsService Service, FakeSettingsRepository Repository) Build(AppSettings? stored = null)
	{
		var repository = new FakeSettingsRepository(stored ?? new AppSettings());
		var service = new SettingsService(repository, new PassthroughProtector(), NullLogger<SettingsService>.Instance);
		return (service, repository);
	}

	[Fact]
	public async Task Update_rejects_invalid_cidr()
	{
		var (service, _) = Build();

		var exception = await Assert.ThrowsAsync<HttpException>(
			() => service.UpdateAsync(Write(subnets: [new SubnetWriteDto { Cidr = "10.0.0.0" }]), TestContext.Current.CancellationToken));
		Assert.Equal(System.Net.HttpStatusCode.BadRequest, exception.StatusCode);
	}

	[Fact]
	public async Task Update_persists_label_and_normalises_blank_label()
	{
		var (service, repository) = Build();

		var dto = await service.UpdateAsync(
			Write(subnets:
			[
				new SubnetWriteDto { Cidr = " 10.0.0.0/24 ", Label = " Services " },
				new SubnetWriteDto { Cidr = "10.0.1.0/24", Label = "  " }
			]),
			TestContext.Current.CancellationToken);

		var stored = repository.Stored.SubnetsFilter;
		Assert.Equal("10.0.0.0/24", stored[0].Cidr);
		Assert.Equal("Services", stored[0].Label);
		// A CIDR-only subnet stays usable: a blank label collapses to null.
		Assert.Null(stored[1].Label);

		Assert.Equal("Services", dto.SubnetsFilter[0].Label);
	}

	[Fact]
	public async Task Update_with_empty_token_keeps_the_stored_one()
	{
		var stored = new AppSettings
		{
			Technitium = new TechnitiumSettings
			{
				Enabled = true,
				BaseUrl = "http://dns.example",
				ApiTokenProtected = "protected:original",
				Zone = "lan.example",
				RecordTtlSeconds = 300
			}
		};
		var (service, repository) = Build(stored);

		var dto = await service.UpdateAsync(Write(new TechnitiumWriteDto
		{
			Enabled = true,
			BaseUrl = "http://dns.example",
			Zone = "lan.example",
			RecordTtlSeconds = 300
		}), TestContext.Current.CancellationToken);

		Assert.True(dto.Technitium.HasApiToken);
		Assert.Equal("protected:original", repository.Stored.Technitium.ApiTokenProtected);
	}

	[Fact]
	public async Task Update_protects_a_new_token()
	{
		var (service, repository) = Build();

		await service.UpdateAsync(Write(new TechnitiumWriteDto
		{
			Enabled = true,
			BaseUrl = "http://dns.example",
			ApiToken = "t0ken",
			Zone = "lan.example",
			RecordTtlSeconds = 300
		}), TestContext.Current.CancellationToken);

		Assert.Equal("protected:t0ken", repository.Stored.Technitium.ApiTokenProtected);
	}

	[Fact]
	public async Task Enabling_technitium_without_any_token_is_rejected()
	{
		var (service, _) = Build();

		var exception = await Assert.ThrowsAsync<HttpException>(
			() => service.UpdateAsync(Write(new TechnitiumWriteDto
			{
				Enabled = true,
				BaseUrl = "http://dns.example",
				Zone = "lan.example",
				RecordTtlSeconds = 300
			}), TestContext.Current.CancellationToken));
		Assert.Equal(System.Net.HttpStatusCode.BadRequest, exception.StatusCode);
	}

	private sealed class FakeSettingsRepository(AppSettings stored) : ISettingsRepository
	{
		public AppSettings Stored { get; private set; } = stored;

		public Task<AppSettings> GetAsync(CancellationToken ct = default)
		{
			return Task.FromResult(Stored);
		}

		public Task<AppSettings> SaveAsync(AppSettings settings, CancellationToken ct = default)
		{
			Stored = settings;
			return Task.FromResult(settings);
		}
	}
}
