using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DeskPad.Core.Network;

public class AnnounceMessage
{
    public string type { get; set; } = "TABL_ANNOUNCE";
    public string pcName { get; set; } = Environment.MachineName;
    public int port { get; set; } = 2828;
    public string version { get; set; } = "2.0";
    public bool hasClient { get; set; }
}

public class UdpDiscoveryServer : IDisposable
{
    private readonly int _port;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private readonly Func<bool> _hasClientFunc;

    public event Action<string>? OnLog;

    public UdpDiscoveryServer(Func<bool> hasClientFunc, int port = 2829)
    {
        _hasClientFunc = hasClientFunc;
        _port = port;
    }

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _port));

            _ = ListenAsync(_cts.Token);
            _ = AnnounceLoopAsync(_cts.Token);
            OnLog?.Invoke($"UDP Discovery Server başlatıldı (Port {_port}).");
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"UDP Discovery Server başlatılamadı: {ex.Message}");
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        if (_udpClient == null) return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);
                string message = Encoding.UTF8.GetString(result.Buffer);

                if (message.Contains("TABL_DISCOVER"))
                {
                    OnLog?.Invoke($"[UDP] {result.RemoteEndPoint.Address} adresinden keşif isteği alındı.");
                    await SendAnnounceAsync(result.RemoteEndPoint, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[UDP] Dinleme hatası: {ex.Message}");
            }
        }
    }

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, _port);
                await SendAnnounceAsync(broadcastEndpoint, ct);
            }
            catch { }

            try
            {
                await Task.Delay(5000, ct); // Announce every 5 seconds
            }
            catch (OperationCanceledException) { }
        }
    }

    private async Task SendAnnounceAsync(IPEndPoint endpoint, CancellationToken ct)
    {
        if (_udpClient == null) return;

        var announce = new AnnounceMessage
        {
            hasClient = _hasClientFunc()
        };

        string json = JsonSerializer.Serialize(announce);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await _udpClient.SendAsync(bytes, bytes.Length, endpoint);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
