using ProxmoxIpMonitor.Abstractions;
using Xunit;

namespace ProxmoxIpMonitor.Core.Tests;

public class SubnetFilterTests
{
	[Theory]
	[InlineData("10.0.0.0/8", "10.0.10.5", true)]
	[InlineData("10.0.0.0/8", "10.255.255.254", true)]
	[InlineData("10.0.0.0/8", "192.168.1.1", false)]
	[InlineData("10.0.10.0/24", "10.0.10.5", true)]
	[InlineData("10.0.10.0/24", "10.0.11.5", false)]
	[InlineData("0.0.0.0/0", "8.8.8.8", true)]
	[InlineData("10.0.0.0/32", "10.0.0.0", true)]
	[InlineData("10.0.0.0/32", "10.0.0.1", false)]
	public void MatchesCidrRanges(string cidr, string ip, bool expected)
	{
		Assert.Equal(expected, SubnetFilter.IsIn(cidr, ip));
	}

	[Theory]
	[InlineData("fe80::1")]
	[InlineData("2001:db8::1")]
	public void Ipv6IsRejected(string ip)
	{
		// Only A records are ever written, so an IPv6 address is never a usable answer.
		Assert.False(SubnetFilter.IsIn("10.0.0.0/8", ip));
	}

	[Theory]
	[InlineData("not-a-cidr", "10.0.0.1")]
	[InlineData("10.0.0.0/33", "10.0.0.1")]
	[InlineData("10.0.0.0/8", "not-an-ip")]
	[InlineData("", "10.0.0.1")]
	public void MalformedInputIsRejectedRatherThanThrowing(string cidr, string ip)
	{
		Assert.False(SubnetFilter.IsIn(cidr, ip));
	}

	[Fact]
	public void AnyMatchingRangeIsEnough()
	{
		Assert.True(SubnetFilter.IsInAny(["192.168.0.0/16", "10.0.0.0/8"], "10.0.10.5"));
		Assert.False(SubnetFilter.IsInAny(["192.168.0.0/16", "172.16.0.0/12"], "10.0.10.5"));
	}
}
