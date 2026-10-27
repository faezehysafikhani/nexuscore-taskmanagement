using System.Net;
using System.Net.Sockets;
using NexusCore.Application.Security;

namespace NexusCore.Infrastructure.Messaging;

/// <summary>
/// Opens the connection to an SMS gateway itself: resolves the name, refuses any address that is
/// not public (unless the host is one the server's operator listed) and connects to the address
/// it checked. The name is therefore resolved once, and the answer that was checked is the one
/// that is used.
/// </summary>
public sealed class GatewayConnector(IOutboundAddressPolicy policy)
{
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

        if (!policy.IsTrustedGatewayHost(host) && addresses.Any(address => !OutboundAddressPolicy.IsPublic(address)))
        {
            throw new HttpRequestException("The address is not allowed.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
