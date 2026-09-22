using ProxmoxIpMonitor.Abstractions;
using Shouldly;
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
		SubnetFilter.IsIn(cidr, ip).ShouldBe(expected);
	}

	[Theory]
	[InlineData("fe80::1")]
	[InlineData("2001:db8::1")]
	public void Ipv6IsRejected(string ip)
	{
		// Only A records are ever written, so an IPv6 address is never a usable answer.
		SubnetFilter.IsIn("10.0.0.0/8", ip).ShouldBeFalse();
	}

	[Theory]
	[InlineData("not-a-cidr", "10.0.0.1")]
	[InlineData("10.0.0.0/33", "10.0.0.1")]
	[InlineData("10.0.0.0/8", "not-an-ip")]
	[InlineData("", "10.0.0.1")]
	public void MalformedInputIsRejectedRatherThanThrowing(string cidr, string ip)
	{
		SubnetFilter.IsIn(cidr, ip).ShouldBeFalse();
	}

	[Fact]
	public void AnyMatchingRangeIsEnough()
	{
		SubnetFilter.IsInAny(["192.168.0.0/16", "10.0.0.0/8"], "10.0.10.5").ShouldBeTrue();
		SubnetFilter.IsInAny(["192.168.0.0/16", "172.16.0.0/12"], "10.0.10.5").ShouldBeFalse();
	}
}
