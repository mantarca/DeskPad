using System.Diagnostics;
using DeskPad.Core.Models;
using DeskPad.Core.Network;

namespace DeskPad.Core.Capture;

public class ScreenStreamEngine : IDisposable
{
    private readonly string _ffmpegPath;
    private readonly TcpStreamServer _server;
    private Process? _ffmpegProcess;
    private CancellationTokenSource? _streamCts;
    private bool _isStreaming;

    // gdigrab yuksek (>=75) framerate degerlerinde kilitlenip hic frame uretmiyor.
    // Bu yuzden yakalama hizini guvenli bir ust sinirla sinirliyoruz.
    private const int MaxCaptureFps = 60;

    public bool IsStreaming => _isStreaming;
    public event Action<string>? OnLog;
    public event Action? OnStreamStarted;
    public event Action? OnStreamStopped;

    public ScreenStreamEngine(TcpStreamServer server, string? customFfmpegPath = null)
    {
        _server = server;
        _ffmpegPath = customFfmpegPath ?? FindFfmpegPath();
    }

    private static string FindFfmpegPath()
    {
        string[] candidates = [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg", "bin", "ffmpeg.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "tools", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages", "Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe", "ffmpeg-9.0.2-full_build", "bin", "ffmpeg.exe"),
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            "ffmpeg"
        ];

        foreach (var path in candidates)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (File.Exists(full)) return full;
            }
            catch { }
        }

        // Kurulumda indirilen arsiv ic klasorle acildigi icin ozyinelemeli ara
        // (orn. tools\ffmpeg\ffmpeg-<surum>-essentials_build\bin\ffmpeg.exe)
        var found = Paths.FindFile(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg"), "ffmpeg.exe");
        if (found != null) return found;

        return "ffmpeg";
    }

    public async Task StartStreamingAsync(StreamConfiguration config)
    {
        if (_isStreaming) StopStreaming();

        _streamCts = new CancellationTokenSource();
        var ct = _streamCts.Token;

        // gdigrab'in destekledigi guvenli yakalama hizina sinirla (ornek: tablet 90Hz bildirse bile 60)
        int captureFps = Math.Clamp(config.TargetFps, 1, MaxCaptureFps);

        // Auto-detect best encoder (NVENC -> QSV -> AMF -> libx264)
        string encoderArgs = await GetEncoderArgumentsAsync(config);

        // Belirli bir monitör (sanal/ikincil ekran) secildiyse sadece o bölgeyi yakala; aksi halde tüm masaüstü.
        string captureInput = config.CaptureWidth > 0 && config.CaptureHeight > 0
            ? $"-f gdigrab -framerate {captureFps} -draw_mouse 1 -offset_x {config.CaptureX} -offset_y {config.CaptureY} -video_size {config.CaptureWidth}x{config.CaptureHeight} -i desktop "
            : $"-f gdigrab -framerate {captureFps} -draw_mouse 1 -i desktop ";

        // FFmpeg capture command using gdigrab for maximum compatibility
        string args = $"-hide_banner -loglevel warning " +
                      captureInput +
                      $"-vf \"scale={config.TargetWidth}:{config.TargetHeight}:force_original_aspect_ratio=decrease,pad={config.TargetWidth}:{config.TargetHeight}:(ow-iw)/2:(oh-ih)/2\" " +
                      $"{encoderArgs} " +
                      $"-f h264 -bsf:v dump_extra pipe:1";

        OnLog?.Invoke($"FFmpeg başlatılıyor: {_ffmpegPath} {args}");

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            _ffmpegProcess = Process.Start(psi);
            if (_ffmpegProcess == null)
            {
                OnLog?.Invoke("FFmpeg süreci başlatılamadı.");
                return;
            }

            _isStreaming = true;
            OnStreamStarted?.Invoke();

            // Log stderr asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_ffmpegProcess.HasExited && !ct.IsCancellationRequested)
                    {
                        string? line = await _ffmpegProcess.StandardError.ReadLineAsync(ct);
                        if (line != null) OnLog?.Invoke($"[FFmpeg] {line}");
                    }
                }
                catch { }
            }, ct);

            // Read raw H.264 stream from stdout and package NAL units
            _ = Task.Run(() => ReadAndBroadcastStreamAsync(_ffmpegProcess.StandardOutput.BaseStream, ct), ct);
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Yayın başlatma hatası: {ex.Message}");
            StopStreaming();
        }
    }

    private async Task<string> GetEncoderArgumentsAsync(StreamConfiguration config)
    {
        int bitrate = config.BitrateKbps;
        int fps = Math.Clamp(config.TargetFps, 1, MaxCaptureFps);
        
        string encoder = await DetectBestEncoderAsync();
        
        if (encoder == "h264_nvenc")
        {
            return $"-c:v {encoder} -preset p1 -tune ull -rc cbr -b:v {bitrate}k -maxrate {bitrate}k -bufsize {bitrate/2}k -g {fps * 2} -keyint_min {fps} -zerolatency 1 -pix_fmt yuv420p";
        }
        else if (encoder == "h264_qsv")
        {
            return $"-c:v {encoder} -preset veryfast -b:v {bitrate}k -maxrate {bitrate}k -bufsize {bitrate/2}k -g {fps * 2} -keyint_min {fps} -pix_fmt nv12";
        }
        else if (encoder == "h264_amf")
        {
            return $"-c:v {encoder} -quality speed -b:v {bitrate}k -maxrate {bitrate}k -bufsize {bitrate/2}k -g {fps * 2} -keyint_min {fps} -pix_fmt yuv420p";
        }
        else
        {
            return $"-c:v libx264 -preset ultrafast -tune zerolatency " +
                   $"-g {fps * 2} -keyint_min {fps} -sc_threshold 0 " +
                   $"-b:v {bitrate}k -maxrate {bitrate}k -bufsize {bitrate / 2}k " +
                   $"-pix_fmt yuv420p";
        }
    }

    private async Task<string> DetectBestEncoderAsync()
    {
        string[] encoders = ["h264_nvenc", "h264_qsv", "h264_amf"];
        foreach (var enc in encoders)
        {
            if (await ProbeEncoderAsync(enc))
            {
                OnLog?.Invoke($"GPU Encoder bulundu: {enc}");
                return enc;
            }
        }
        OnLog?.Invoke("Uygun GPU Encoder bulunamadı, CPU (libx264) kullanılacak.");
        return "libx264";
    }

    private async Task<bool> ProbeEncoderAsync(string encoderName)
    {
        try
        {
            string args = $"-hide_banner -loglevel error -f lavfi -i testsrc=duration=1:size=1280x720:rate=30 -c:v {encoderName} -f null -";
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReadAndBroadcastStreamAsync(Stream stdout, CancellationToken ct)
    {
        byte[] readBuffer = new byte[2 * 1024 * 1024]; // 2MB buffer for large keyframes
        int bufferLength = 0;

        try
        {
            while (!ct.IsCancellationRequested && _isStreaming)
            {
                int bytesRead = await stdout.ReadAsync(readBuffer.AsMemory(bufferLength, readBuffer.Length - bufferLength), ct);
                if (bytesRead == 0) break;

                bufferLength += bytesRead;

                int offset = 0;
                while (offset < bufferLength - 3)
                {
                    // Search for the NEXT start code (00 00 01)
                    // We start searching from offset + 3 to avoid finding the start code that might be AT offset.
                    int nextNalIndex = -1;
                    for (int i = offset + 3; i <= bufferLength - 3; i++)
                    {
                        if (readBuffer[i] == 0 && readBuffer[i + 1] == 0 && readBuffer[i + 2] == 1)
                        {
                            // Check if it's a 4-byte start code (00 00 00 01)
                            if (i > 0 && readBuffer[i - 1] == 0)
                            {
                                nextNalIndex = i - 1;
                            }
                            else
                            {
                                nextNalIndex = i;
                            }
                            break;
                        }
                    }

                    if (nextNalIndex != -1)
                    {
                        if (nextNalIndex > offset)
                        {
                            int nalLength = nextNalIndex - offset;
                            byte[] nalData = new byte[nalLength];
                            Buffer.BlockCopy(readBuffer, offset, nalData, 0, nalLength);

                            // Verify it actually has a start code (skip garbage at the very beginning of stream)
                            if ((nalLength >= 3 && nalData[0] == 0 && nalData[1] == 0 && nalData[2] == 1) ||
                                (nalLength >= 4 && nalData[0] == 0 && nalData[1] == 0 && nalData[2] == 0 && nalData[3] == 1))
                            {
                                bool isKeyframe = DetectKeyframeInChunk(nalData);
                                await _server.SendVideoFrameAsync(nalData, isKeyframe);
                            }
                        }
                        offset = nextNalIndex; // Move to the found start code
                    }
                    else
                    {
                        break; // No more complete NAL units in buffer
                    }
                }

                // Shift remaining bytes to start of buffer
                if (offset > 0)
                {
                    int remaining = bufferLength - offset;
                    if (remaining > 0)
                    {
                        Buffer.BlockCopy(readBuffer, offset, readBuffer, 0, remaining);
                    }
                    bufferLength = remaining;
                }
                else if (bufferLength == readBuffer.Length)
                {
                    // Buffer is full but no start code found, drop buffer to avoid overflow
                    bufferLength = 0;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Akış okuma sonlandı: {ex.Message}");
        }
        finally
        {
            StopStreaming();
        }
    }

    private static bool DetectKeyframeInChunk(byte[] chunk)
    {
        // Scan for 00 00 00 01 or 00 00 01 followed by NAL type 5 (IDR) or 7 (SPS)
        for (int i = 0; i < chunk.Length - 4; i++)
        {
            if (chunk[i] == 0 && chunk[i + 1] == 0)
            {
                int nalIndex = -1;
                if (chunk[i + 2] == 1) nalIndex = i + 3;
                else if (chunk[i + 2] == 0 && chunk[i + 3] == 1) nalIndex = i + 4;

                if (nalIndex != -1 && nalIndex < chunk.Length)
                {
                    int nalType = chunk[nalIndex] & 0x1F;
                    if (nalType == 5 || nalType == 7) // IDR or SPS
                        return true;
                }
            }
        }
        return false;
    }

    public void StopStreaming()
    {
        if (!_isStreaming) return;
        _isStreaming = false;

        _streamCts?.Cancel();
        try
        {
            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                _ffmpegProcess.Kill(entireProcessTree: true);
            }
        }
        catch { }
        finally
        {
            _ffmpegProcess?.Dispose();
            _ffmpegProcess = null;
        }

        OnStreamStopped?.Invoke();
    }

    public void Dispose()
    {
        StopStreaming();
        _streamCts?.Dispose();
    }
}
