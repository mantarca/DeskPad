using DeskPad.Core.Adb;
using DeskPad.Core.Capture;
using DeskPad.Core.Models;
using DeskPad.Core.Network;
using DeskPad.Core.VirtualDisplay;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Title = "DeskPad - Type-C İkinci Ekran Sunucusu";

PrintBanner();

var virtualDisplay = new WindowsVirtualDisplayManager();
var adbManager = new AdbTunnelManager();
using var streamServer = new TcpStreamServer(2828);
using var streamEngine = new ScreenStreamEngine(streamServer);

var currentConfig = new StreamConfiguration
{
    TargetWidth = 1920,
    TargetHeight = 1080,
    TargetFps = 60,
    BitrateKbps = 25000,
    DisplayIndex = 1
};

// Check driver status
bool isDriverInstalled = await virtualDisplay.IsDriverInstalledAsync();
bool isDisplayActive = isDriverInstalled && await virtualDisplay.IsDisplayEnabledAsync();

LogInfo($"Sanal Monitör Sürücüsü: {(isDriverInstalled ? "Kurulu ✓" : "Kurulu Değil ✗")}");
LogInfo($"Sanal 2. Monitör Durumu: {(isDisplayActive ? "Aktif (Windows Ayarlarında 2. Ekran Açık) ✓" : "Pasif / Kapalı")}");

// Start TCP Server
streamServer.Start();
LogInfo("TCP Soket Sunucusu başlatıldı (Port 2828 dinleniyor).");

// Hook events
streamServer.OnClientConnected += () =>
{
    LogSuccess(">>> Tablet bağlandı! El sıkışma bekleniyor...");
};

streamServer.OnClientDisconnected += () =>
{
    LogWarning("<<< Tablet bağlantısı koptu. Akış durduruldu.");
    streamEngine.StopStreaming();
};

streamServer.OnClientHandshakeReceived += async (handshake) =>
{
    LogSuccess($">>> Tablet El Sıkışması: {handshake.DeviceModel} ({handshake.Width}x{handshake.Height} @ {handshake.RefreshRate}Hz)");
    
    // Auto-adjust resolution if tablet provides native metrics
    if (handshake.Width > 0 && handshake.Height > 0)
    {
        currentConfig.TargetWidth = handshake.Width;
        currentConfig.TargetHeight = handshake.Height;
        currentConfig.TargetFps = handshake.RefreshRate > 0 ? handshake.RefreshRate : 60;
    }

    await streamServer.SendConfigAckAsync(new ServerConfigInfo
    {
        Width = currentConfig.TargetWidth,
        Height = currentConfig.TargetHeight,
        Fps = currentConfig.TargetFps,
        Codec = "h264"
    });

    LogInfo($"Akış parametreleri: {currentConfig.TargetWidth}x{currentConfig.TargetHeight} @ {currentConfig.TargetFps} FPS (Ekran #{currentConfig.DisplayIndex})");
    await streamEngine.StartStreamingAsync(currentConfig);
};

streamEngine.OnStreamStarted += () =>
{
    LogSuccess("Ekran yakalama ve akış BAŞLATILDI! Görüntü tablete aktarılıyor.");
};

// Auto-detector loop in background
_ = Task.Run(async () =>
{
    string? lastSerial = null;
    while (true)
    {
        try
        {
            var devices = await adbManager.GetConnectedDevicesAsync();
            if (devices.Count > 0)
            {
                var dev = devices[0];
                if (dev.SerialNumber != lastSerial)
                {
                    lastSerial = dev.SerialNumber;
                    LogSuccess($"[ADB] Tablet algılandı: {dev.ModelName} ({dev.SerialNumber})");

                    // Setup reverse port forward
                    bool tunnelOk = await adbManager.SetupReverseTunnelAsync(2828, dev.SerialNumber);
                    if (tunnelOk)
                    {
                        LogSuccess($"[ADB] Port tüneli kuruldu: Tablet tcp:2828 -> PC tcp:2828");
                    }

                    // Query native resolution
                    var res = await adbManager.GetDeviceDisplayResolutionAsync(dev.SerialNumber);
                    if (res != null)
                    {
                        LogInfo($"[ADB] Tablet ekran çözünürlüğü okundu: {res.Width}x{res.Height}");
                        await virtualDisplay.ConfigureResolutionAsync(res);
                    }
                }
            }
            else
            {
                lastSerial = null;
            }
        }
        catch { }
        await Task.Delay(3000);
    }
});

// Interactive menu loop
PrintMenu();

while (true)
{
    var key = Console.ReadKey(true).Key;
    switch (key)
    {
        case ConsoleKey.I:
            LogInfo("Sürücü kuruluyor (Yönetici yetkisi gerektirebilir)...");
            bool installed = await virtualDisplay.InstallDriverAsync();
            LogInfo(installed ? "Sürücü başarıyla kuruldu!" : "Sürücü kurulamadı.");
            break;

        case ConsoleKey.E:
            bool enabled = await virtualDisplay.IsDisplayEnabledAsync();
            if (enabled)
            {
                LogInfo("Sanal monitör devre dışı bırakılıyor...");
                await virtualDisplay.DisableDisplayAsync();
                LogInfo("Sanal monitör kapatıldı.");
            }
            else
            {
                LogInfo("Sanal monitör etkinleştiriliyor...");
                await virtualDisplay.EnableDisplayAsync();
                LogSuccess("Sanal 2. monitör açıldı! Windows Görüntü Ayarlarından kontrol edebilirsiniz.");
            }
            break;

        case ConsoleKey.S:
            if (streamEngine.IsStreaming)
            {
                LogInfo("Akış manuel olarak durduruluyor...");
                streamEngine.StopStreaming();
            }
            else
            {
                LogInfo($"Akış manuel başlatılıyor ({currentConfig.TargetWidth}x{currentConfig.TargetHeight} @ {currentConfig.TargetFps} FPS)...");
                await streamEngine.StartStreamingAsync(currentConfig);
            }
            break;

        case ConsoleKey.D:
            currentConfig.DisplayIndex = currentConfig.DisplayIndex == 1 ? 0 : 1;
            LogInfo($"Yayınlanacak ekran indeksi değiştirildi: Ekran #{currentConfig.DisplayIndex}");
            if (streamEngine.IsStreaming)
            {
                await streamEngine.StartStreamingAsync(currentConfig);
            }
            break;

        case ConsoleKey.R:
            Console.WriteLine();
            Console.WriteLine("Çözünürlük ve FPS Profili Seçin:");
            Console.WriteLine("1: 1920x1080 @ 60 FPS (FHD Standart)");
            Console.WriteLine("2: 1920x1200 @ 60 FPS (16:10 Tabletler)");
            Console.WriteLine("3: 2560x1600 @ 60 FPS (2K Tabletler)");
            Console.WriteLine("4: 2560x1600 @ 120 FPS (Ultra Akıcı 2K)");
            var opt = Console.ReadKey(true).KeyChar;
            if (opt == '1') { currentConfig.TargetWidth = 1920; currentConfig.TargetHeight = 1080; currentConfig.TargetFps = 60; }
            else if (opt == '2') { currentConfig.TargetWidth = 1920; currentConfig.TargetHeight = 1200; currentConfig.TargetFps = 60; }
            else if (opt == '3') { currentConfig.TargetWidth = 2560; currentConfig.TargetHeight = 1600; currentConfig.TargetFps = 60; }
            else if (opt == '4') { currentConfig.TargetWidth = 2560; currentConfig.TargetHeight = 1600; currentConfig.TargetFps = 120; }
            LogSuccess($"Yeni Profil: {currentConfig.TargetWidth}x{currentConfig.TargetHeight} @ {currentConfig.TargetFps} FPS");
            break;

        case ConsoleKey.Q:
            LogInfo("Çıkılıyor...");
            streamEngine.StopStreaming();
            streamServer.Stop();
            return;
    }
}

static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("==================================================================");
    Console.WriteLine("   DESKPAD - TYPE-C İKİNCİ EKRAN SUNUCUSU (.NET 10)             ");
    Console.WriteLine("   Ultra Düşük Gecikmeli Genişletilmiş Masaüstü Çözümü           ");
    Console.WriteLine("==================================================================");
    Console.ResetColor();
    Console.WriteLine();
}

static void PrintMenu()
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("--- Kısayol Tuşları ---");
    Console.WriteLine("[E] Sanal Monitörü Aç / Kapat (Enable / Disable 2nd Display)");
    Console.WriteLine("[I] Sanal Monitör Sürücüsünü Yükle (Install Driver)");
    Console.WriteLine("[S] Canlı Yayını Başlat / Durdur");
    Console.WriteLine("[D] Yakalanacak Ekranı Değiştir (Ekran 1 / Ekran 2)");
    Console.WriteLine("[R] Çözünürlük ve FPS Profili Değiştir");
    Console.WriteLine("[Q] Çıkış");
    Console.WriteLine("-----------------------");
    Console.ResetColor();
    Console.WriteLine();
}

static void LogInfo(string msg)
{
    Console.ForegroundColor = ConsoleColor.Gray;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
    Console.ResetColor();
}

static void LogSuccess(string msg)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
    Console.ResetColor();
}

static void LogWarning(string msg)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
    Console.ResetColor();
}
