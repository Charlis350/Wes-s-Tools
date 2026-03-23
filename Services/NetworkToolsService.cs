using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace WessTools.Services;

public sealed class NetworkToolsService
{
    public string GetNetworkSummary()
    {
        var builder = new StringBuilder();

        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                              adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(adapter => adapter.Speed)
            .Take(2)
            .ToList();

        if (interfaces.Count == 0)
        {
            return "No active network adapters were detected.";
        }

        foreach (var adapter in interfaces)
        {
            var ipAddress = adapter.GetIPProperties().UnicastAddresses
                .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork)?
                .Address
                .ToString() ?? "No IPv4 address";

            builder.AppendLine($"{adapter.Name} - {adapter.NetworkInterfaceType}");
            builder.AppendLine($"IPv4: {ipAddress}");
            builder.AppendLine($"Link speed: {adapter.Speed / 1_000_000} Mbps");
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    public string PingHost(string host)
    {
        using var ping = new Ping();
        var reply = ping.Send(host, 3000);

        return reply.Status == IPStatus.Success
            ? $"Ping to {host} succeeded in {reply.RoundtripTime} ms."
            : $"Ping to {host} failed: {reply.Status}.";
    }
}
