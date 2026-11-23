using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace Recodec.Core.Devices;

/// <summary>
/// Busca reproductores DLNA (teles, consolas, altavoces) en la red local con SSDP y les pregunta
/// qué formatos aceptan. Es lo mismo que hace Windows para "Reproducir en".
/// </summary>
public static class DlnaScanner
{
    private static readonly IPEndPoint SsdpGroup = new(IPAddress.Parse("239.255.255.250"), 1900);
    private const string RendererType = "urn:schemas-upnp-org:device:MediaRenderer:1";

    /// <param name="onSink">Recibe la lista de formatos tal cual la envía cada aparato (para diagnóstico).</param>
    public static async Task<List<DeviceProfile>> ScanAsync(TimeSpan wait, Action<string>? log = null, CancellationToken ct = default, Action<DeviceProfile, string>? onSink = null)
    {
        var locations = await DiscoverAsync(wait, log, ct);
        log?.Invoke(locations.Count == 0 ? "No ha respondido ningún reproductor." : $"Han respondido {locations.Count} reproductores; leyendo sus formatos…");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var tasks = locations.Select(l => DescribeAsync(http, l, log, ct, onSink));
        var found = (await Task.WhenAll(tasks)).Where(d => d is not null).Select(d => d!).ToList();
        return found.GroupBy(d => d.Id).Select(g => g.First()).OrderBy(d => d.Name).ToList();
    }

    /// <summary>Envía la búsqueda SSDP por cada red del equipo y recoge las direcciones de descripción (LOCATION).</summary>
    public static async Task<List<Uri>> DiscoverAsync(TimeSpan wait, Action<string>? log = null, CancellationToken ct = default)
    {
        var found = new ConcurrentDictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        var locals = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.SupportsMulticast)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .ToList();
        if (locals.Count == 0) locals.Add(IPAddress.Any);

        var msg = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            $"ST: {RendererType}\r\n\r\n");

        async Task SearchOn(IPAddress local)
        {
            try
            {
                using var udp = new UdpClient(new IPEndPoint(local, 0));
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);
                if (!local.Equals(IPAddress.Any))
                    udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                for (var i = 0; i < 2; i++) await udp.SendAsync(msg, msg.Length, SsdpGroup);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(wait);
                while (!timeout.IsCancellationRequested)
                {
                    UdpReceiveResult r;
                    try { r = await udp.ReceiveAsync(timeout.Token); }
                    catch (OperationCanceledException) { break; }
                    var text = Encoding.ASCII.GetString(r.Buffer);
                    var loc = Header(text, "LOCATION");
                    if (loc is not null && Uri.TryCreate(loc, UriKind.Absolute, out var uri)) found.TryAdd(uri.ToString(), uri);
                }
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                log?.Invoke($"No se ha podido buscar por la red {local}: {ex.Message}");
            }
        }

        await Task.WhenAll(locals.Select(SearchOn));
        ct.ThrowIfCancellationRequested();
        return found.Values.ToList();
    }

    private static string? Header(string response, string name)
    {
        foreach (var line in response.Split("\r\n"))
        {
            var i = line.IndexOf(':');
            if (i > 0 && line[..i].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return line[(i + 1)..].Trim();
        }
        return null;
    }

    /// <summary>Lee la descripción del aparato y su lista de formatos.</summary>
    public static async Task<DeviceProfile?> DescribeAsync(HttpClient http, Uri location, Action<string>? log, CancellationToken ct, Action<DeviceProfile, string>? onSink = null)
    {
        try
        {
            var xml = XDocument.Parse(await http.GetStringAsync(location, ct));
            var root = xml.Root!;
            var device = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "device" &&
                             (Child(e, "deviceType")?.Contains("MediaRenderer", StringComparison.OrdinalIgnoreCase) ?? false))
                         ?? root.Descendants().FirstOrDefault(e => e.Name.LocalName == "device");
            if (device is null) return null;

            var name = Child(device, "friendlyName") ?? location.Host;
            var udn = Child(device, "UDN") ?? location.ToString();
            var manufacturer = Child(device, "manufacturer");
            var model = Child(device, "modelName");
            model ??= Child(device, "modelNumber");

            var service = device.Descendants().FirstOrDefault(e => e.Name.LocalName == "service" &&
                              (Child(e, "serviceType")?.Contains("ConnectionManager", StringComparison.OrdinalIgnoreCase) ?? false));
            var control = service is null ? null : Child(service, "controlURL");
            var serviceType = service is null ? null : Child(service, "serviceType");
            if (control is null || serviceType is null)
            {
                log?.Invoke($"{name}: no ofrece la lista de formatos (ConnectionManager).");
                return null;
            }

            var baseText = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value.Trim();
            var baseUri = !string.IsNullOrEmpty(baseText) && Uri.TryCreate(baseText, UriKind.Absolute, out var b) ? b : location;
            var controlUri = new Uri(baseUri, control);

            var body = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                       "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                       $"<s:Body><u:GetProtocolInfo xmlns:u=\"{serviceType}\"/></s:Body></s:Envelope>";
            using var req = new HttpRequestMessage(HttpMethod.Post, controlUri) { Content = new StringContent(body, Encoding.UTF8, "text/xml") };
            req.Headers.TryAddWithoutValidation("SOAPAction", $"\"{serviceType}#GetProtocolInfo\"");
            using var resp = await http.SendAsync(req, ct);
            var answer = XDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var sink = answer.Descendants().FirstOrDefault(e => e.Name.LocalName == "Sink")?.Value ?? "";
            if (sink.Length == 0)
            {
                log?.Invoke($"{name}: la lista de formatos está vacía.");
                return null;
            }
            var profile = DeviceProfile.FromProtocolInfo(sink, udn, name, manufacturer, model, location.Host);
            onSink?.Invoke(profile, sink);
            return profile;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log?.Invoke($"{location.Host}: no se ha podido leer ({ex.Message}).");
            return null;
        }
    }

    private static string? Child(XElement e, string name) =>
        e.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } v ? v : null;
}
