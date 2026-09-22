using ProxmoxIpMonitor.Adapters.Proxmox.Pve;
using Shouldly;
using Xunit;

namespace ProxmoxIpMonitor.Adapters.Tests;

public class PveNetConfigTests
{
	[Fact]
	public void ReadsTheTagOfAVmNic()
	{
		var nets = new Dictionary<string, string> { ["net0"] = "virtio=BC:24:11:AA:BB:CC,bridge=vmbr0,tag=30,firewall=1" };

		PveNetConfig.ResolveVlan(nets, null).ShouldBe(30);
	}

	[Fact]
	public void ReadsTheTagOfAContainerNic()
	{
		var nets = new Dictionary<string, string> { ["net0"] = "name=eth0,bridge=vmbr0,hwaddr=BC:24:11:AA:BB:CC,ip=dhcp,tag=40,type=veth" };

		PveNetConfig.ResolveVlan(nets, null).ShouldBe(40);
	}

	[Fact]
	public void AnUntaggedNicHasNoVlan()
	{
		var nets = new Dictionary<string, string> { ["net0"] = "virtio=BC:24:11:AA:BB:CC,bridge=vmbr0" };

		PveNetConfig.ResolveVlan(nets, null).ShouldBeNull();
	}

	[Fact]
	public void PicksTheNicCarryingTheObservedAddressByMac()
	{
		// The address was seen on net1 (VLAN 30); net0 sits on a different VLAN and must not win.
		var nets = new Dictionary<string, string>
		{
			["net0"] = "virtio=AA:AA:AA:AA:AA:AA,bridge=vmbr0,tag=10",
			["net1"] = "virtio=BC:24:11:AA:BB:CC,bridge=vmbr1,tag=30"
		};

		PveNetConfig.ResolveVlan(nets, "BC:24:11:AA:BB:CC").ShouldBe(30);
	}

	[Fact]
	public void MacMatchingIsCaseInsensitive()
	{
		var nets = new Dictionary<string, string> { ["net0"] = "virtio=BC:24:11:AA:BB:CC,bridge=vmbr0,tag=30" };

		PveNetConfig.ResolveVlan(nets, "bc:24:11:aa:bb:cc").ShouldBe(30);
	}

	[Fact]
	public void FallsBackToThePrimaryNicWhenTheMacDoesNotMatch()
	{
		// No interface carries this MAC, so the lowest-indexed NIC decides. net10 must not sort
		// before net2 as a string would.
		var nets = new Dictionary<string, string>
		{
			["net10"] = "virtio=AA:AA:AA:AA:AA:AA,bridge=vmbr0,tag=99",
			["net2"] = "virtio=BB:BB:BB:BB:BB:BB,bridge=vmbr0,tag=20"
		};

		PveNetConfig.ResolveVlan(nets, "CC:CC:CC:CC:CC:CC").ShouldBe(20);
	}

	[Fact]
	public void NoNicsMeansNoVlan()
	{
		PveNetConfig.ResolveVlan(new Dictionary<string, string>(), "BC:24:11:AA:BB:CC").ShouldBeNull();
	}

	[Theory]
	[InlineData("net0", true)]
	[InlineData("net12", true)]
	[InlineData("network", false)]
	[InlineData("ipconfig0", false)]
	[InlineData("cores", false)]
	public void RecognisesNicConfigKeys(string key, bool expected)
	{
		PveNetConfig.IsNetKey(key).ShouldBe(expected);
	}
}
