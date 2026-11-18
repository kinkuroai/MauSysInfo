using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MauSysInfo.Services;

public static partial class NetworkCollector
{
    public static PageData Collect()
    {
        var page = new PageData();
        NetworkInterface[] all;
        try { all = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { return page; }

        var relevant = all.Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                                   && !IsPlumbing(n.Name, n.Description)).ToList();
        foreach (var nic in relevant.Where(n => n.OperationalStatus == OperationalStatus.Up)
                                    .OrderByDescending(HasGateway).ThenBy(n => n.Name))
        {
            var card = page.AddBlock(nic.Name, nic.Description);
            card.Add("Adapter", nic.Description);
            card.Add("Type", TypeName(nic.NetworkInterfaceType));
            card.Add("Link speed", Units.LinkSpeed(nic.Speed));
            card.Add("MAC address", FormatMac(nic.GetPhysicalAddress()));
            try
            {
                var ip = nic.GetIPProperties();
                card.Add("IPv4", string.Join(", ", ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                                                                       .Select(a => $"{a.Address}/{a.PrefixLength}")));
                card.Add("IPv6", string.Join(", ", ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal)
                                                                       .Select(a => a.Address.ToString())));
                card.Add("Gateway", string.Join(", ", ip.GatewayAddresses.Select(g => g.Address.ToString()).Where(a => a != "0.0.0.0" && a != "::")));
                card.Add("DNS servers", string.Join(", ", ip.DnsAddresses.Where(a => !a.IsIPv6SiteLocal).Select(a => a.ToString())));
                try { card.Add("DHCP", ip.GetIPv4Properties()?.IsDhcpEnabled == true ? "On" : "Off (static address)"); } catch { }
                card.Add("DNS suffix", ip.DnsSuffix);
            }
            catch { }
        }

        var down = relevant.Where(n => n.OperationalStatus != OperationalStatus.Up).OrderBy(n => n.Name).ToList();
        if (down.Count > 0)
        {
            var card = page.AddBlock("Not connected");
            foreach (var nic in down)
                card.Add(nic.Name, $"{nic.Description} · {StatusName(nic.OperationalStatus)}");
        }

        return page.WithoutEmptyBlocks();
    }

    /// <summary>
    /// Windows' internal pseudo-adapters: driver filter layers ("Ethernet-QoS Packet Scheduler-0000"),
    /// WAN miniports, the kernel debugger and Wi-Fi Direct virtual adapters. None of them is a network you use.
    /// </summary>
    public static bool IsPlumbing(string name, string description) =>
        FilterSuffix().IsMatch(name)
        || description.StartsWith("WAN Miniport", StringComparison.OrdinalIgnoreCase)
        || description.StartsWith("Microsoft Kernel Debug", StringComparison.OrdinalIgnoreCase)
        || description.StartsWith("Microsoft Wi-Fi Direct Virtual", StringComparison.OrdinalIgnoreCase)
        || description.StartsWith("Microsoft ISATAP", StringComparison.OrdinalIgnoreCase)
        || description.StartsWith("Teredo", StringComparison.OrdinalIgnoreCase);

    [System.Text.RegularExpressions.GeneratedRegex(@"-\d{4}$")]
    private static partial System.Text.RegularExpressions.Regex FilterSuffix();

    private static bool HasGateway(NetworkInterface n)
    {
        try { return n.GetIPProperties().GatewayAddresses.Count > 0; }
        catch { return false; }
    }

    public static string? FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? null : string.Join("-", bytes.Select(b => b.ToString("X2")));
    }

    private static string TypeName(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => "Ethernet",
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Wman or NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "Mobile broadband",
        NetworkInterfaceType.Ppp => "PPP / VPN",
        _ => type.ToString(),
    };

    private static string StatusName(OperationalStatus status) => status switch
    {
        OperationalStatus.Down => "disconnected",
        OperationalStatus.NotPresent => "not present",
        OperationalStatus.LowerLayerDown => "cable unplugged / not connected",
        OperationalStatus.Dormant => "dormant",
        _ => status.ToString().ToLowerInvariant(),
    };
}
