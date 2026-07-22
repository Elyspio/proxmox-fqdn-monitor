using ProxmoxIpMonitor.Adapters.Proxmox.Pve;
using Xunit;

namespace ProxmoxIpMonitor.Adapters.Tests;

public class PveNetConfigTests
{
	[Fact]
	public void ReadsTheTagOfAVmNic()
	{
		var nets = new Dictionary<string, string> { ["net0"] = "virtio=BC:24:11:AA:BB:CC,bridge=vmbr0,tag=30,firewall=1" };

		Assert.Equal(30, PveNetConfig.ResolveVlan(nets, null));
	}

	[Fact]
	public void ReadsTheTagOfAContainerNic()
	{
		var nets = new Dictionary<string, string> { ["net0"] = "name=eth0,bridge=vmbr0,hwaddr=BC:24:11:AA:BB:CC,ip=dhcp,tag=40,type=veth" };

		Assert.Equal(40, PveNetConfig.ResolveVlan(nets, null));
	}

	[Fact]
	public void AnUntaggedNicHasNoVlan()
	{
		var nets = new Dictionary<string, string> { ["net0"] = "virtio=BC:24:11:AA:BB:CC,bridge=vmbr0" };

		Assert.Null(PveNetConfig.ResolveVlan(nets, null));
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

		Assert.Equal(30, PveNetConfig.ResolveVlan(nets, "BC:24:11:AA:BB:CC"));
	}

	[Fact]
	public void MacMatchingIsCaseInsensitive()
	{
		var nets = new Dictionary<string, string> { ["net0"] = "virtio=BC:24:11:AA:BB:CC,bridge=vmbr0,tag=30" };

		Assert.Equal(30, PveNetConfig.ResolveVlan(nets, "bc:24:11:aa:bb:cc"));
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

		Assert.Equal(20, PveNetConfig.ResolveVlan(nets, "CC:CC:CC:CC:CC:CC"));
	}

	[Fact]
	public void NoNicsMeansNoVlan()
	{
		Assert.Null(PveNetConfig.ResolveVlan(new Dictionary<string, string>(), "BC:24:11:AA:BB:CC"));
	}

	[Theory]
	[InlineData("net0", true)]
	[InlineData("net12", true)]
	[InlineData("network", false)]
	[InlineData("ipconfig0", false)]
	[InlineData("cores", false)]
	public void RecognisesNicConfigKeys(string key, bool expected)
	{
		Assert.Equal(expected, PveNetConfig.IsNetKey(key));
	}
}
