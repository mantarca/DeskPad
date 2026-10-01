using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DeskPad.Core.Models;
using DeskPad.Core.Protocol;

namespace DeskPad.Core.Network;

public class ClientHandshakeInfo
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshRate { get; set; }
    public string DeviceModel { get; set; } = string.Empty;
}

public class ServerConfigInfo
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Fps { get; set; }
    public string Codec { get; set; } = "h264";
}

public class TcpStreamServer : IDisposable
{
    private readonly int _port;
    private TcpListener? _listener;
    private TcpClient? _activeClient;
    private NetworkStream? _networkStream;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public event Action<ClientHandshakeInfo>? OnClientHandshakeReceived;
    public event Action? OnClientConnected;
    public event Action? OnClientDisconnected;
    public bool IsClientConnected => _activeClient?.Connected == true && _networkStream != null;

    public TcpStreamServer(int port = 2828)
    {
        _port = port;
    }

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Server.NoDelay = true; // Disable Nagle's algorithm for ultra-low latency!
        _listener.Start();

        _ = AcceptClientsAsync(_cts.Token);
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                client.SendBufferSize = 1024 * 1024; // 1MB buffer

                // Disconnect previous client if any
                CloseActiveClient();

                _activeClient = client;
                _networkStream = client.GetStream();
                OnClientConnected?.Invoke();

                _ = HandleClientReceiveAsync(_networkStream, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Error accepting client, retry
            }
        }
    }

    private async Task HandleClientReceiveAsync(NetworkStream stream, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && stream.CanRead)
            {
                var packet = await StreamPacket.ReadFromStreamAsync(stream, ct);
                if (packet == null) break;

                if (packet.Type == PacketType.Handshake)
                {
                    string json = Encoding.UTF8.GetString(packet.Payload);
                    var handshake = JsonSerializer.Deserialize<ClientHandshakeInfo>(json);
                    if (handshake != null)
                    {
                        OnClientHandshakeReceived?.Invoke(handshake);
                    }
                }
                else if (packet.Type == PacketType.Ping)
                {
                    // Respond with Pong
                    var pong = StreamPacket.Serialize(PacketType.Pong, FrameFlags.None, []);
                    await SendRawAsync(pong);
                }
            }
        }
        catch
        {
            // Client error/disconnected
        }
        finally
        {
            CloseActiveClient();
            OnClientDisconnected?.Invoke();
        }
    }

    public async Task SendConfigAckAsync(ServerConfigInfo config)
    {
        string json = JsonSerializer.Serialize(config);
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] packet = StreamPacket.Serialize(PacketType.ConfigAck, FrameFlags.ConfigData, payload);
        await SendRawAsync(packet);
    }

    public async Task SendVideoFrameAsync(byte[] nalData, bool isKeyframe)
    {
        if (!IsClientConnected) return;

        var flags = isKeyframe ? FrameFlags.Keyframe : FrameFlags.None;
        byte[] packet = StreamPacket.Serialize(PacketType.VideoFrame, flags, nalData);
        await SendRawAsync(packet);
    }

    private async Task SendRawAsync(byte[] data)
    {
        if (_networkStream == null || !IsClientConnected) return;

        try
        {
            await _sendLock.WaitAsync();
            try
            {
                await _networkStream.WriteAsync(data);
                await _networkStream.FlushAsync();
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch
        {
            CloseActiveClient();
            OnClientDisconnected?.Invoke();
        }
    }

    private void CloseActiveClient()
    {
        try
        {
            _networkStream?.Dispose();
            _activeClient?.Dispose();
        }
        catch { }
        finally
        {
            _networkStream = null;
            _activeClient = null;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        CloseActiveClient();
        try
        {
            _listener?.Stop();
        }
        catch { }
        _listener = null;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _sendLock.Dispose();
    }
}
