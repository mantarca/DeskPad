# DeskPad — USB Type-C İkinci Monitör (Extended Display) Çözümü

Android tabletinizi USB Type-C kablosuyla bilgisayarınıza bağlayarak, Windows'un **Genişletilmiş İkinci Ekran (Extended Display)** olarak tanımasını ve ultra düşük gecikmeyle (< 20 ms) görüntü aktarmasını sağlayan açık kaynaklı sistem.

---

## 🌟 Öne Çıkan Özellikler

1. **Gerçek İkinci Ekran (Extended Display):**
   - Windows Görüntü Ayarlarında 2. monitör olarak tanınır.
   - Pencereleri, tarayıcı sekmelerini veya uygulamaları ana ekrandan tablete serbestçe sürükleyip bırakabilirsiniz.
   - Fiziksel kukla fişe (HDMI dummy plug) gerek yoktur; açık kaynak **IddCx (Indirect Display Driver)** sürücüsü kullanır.

2. **Ultra Düşük Gecikme (Low Latency):**
   - **Ekran Yakalama:** DirectX Desktop Duplication API (`ddagrab`) ile GPU VRAM'den sıfır kopyalama ile yakalanır.
   - **Donanım Kodlama:** H.264 (NVENC / QuickSync / AMF / ultrafast x264) `zerolatency` parametresiyle işlenir.
   - **Android Çözücü:** Android yerel `MediaCodec` API'si ve `SurfaceView` ile doğrudan tabletin GPU'suna basılır (< 5 ms decode süresi).

3. **Otomatik Çözünürlük ve Hata Ayıklama (Tak-Çalıştır):**
   - Tablet Type-C ile bağlandığı anda ADB üzerinden otomatik algılanır.
   - Tabletin doğal ekran çözünürlüğü (Örn: 2560x1600, 1920x1200) otomatik okunur ve sanal ekran profiline atanır.
   - Tablet üzerinde hiçbir IP veya ağ ayarı yapılması gerekmez (`adb reverse` port köprüsü kullanılır).

4. **Çoklu Platform Mimarisi (Windows + Linux Uyumlu):**
   - .NET 10 tabanlı modüler çekirdek (`DeskPad.Core`).
   - Windows için IddCx ve DXGI katmanı, Linux için headless Wayland / PipeWire / EVDI soyutlaması hazırdır.

---

## 📁 Proje Dizin Yapısı

```
deskpad/
├── DeskPad.apk           # Hazır derlenmiş Android Tablet APK dosyası
├── install-apk.bat             # APK'yı tablete tek tıkla yükleyen betik
├── install-driver.bat          # Sanal Monitör sürücüsünü kuran yönetici betiği
├── uninstall-driver.bat        # Sürücüyü sistemden temizleyen kaldırma betiği
├── run-host.bat                # PC Sunucu uygulamasını başlatan betik
│
├── host-pc/                    # Bilgisayar Sunucusu (.NET 10)
│   ├── DeskPad.slnx      # .NET Çözüm dosyası
│   └── src/
│       ├── DeskPad.Core/ # Sürücü, ADB, Protokol, Ağ ve Akış Motoru
│       └── DeskPad.Host/ # Konsol Kontrol Paneli ve Servis Koordinatörü
│
├── android-client/             # Android İstemci Uygulaması (Kotlin / Gradle)
│   ├── app/src/main/
│   │   ├── AndroidManifest.xml
│   │   └── java/com/mantarca/deskpad/
│   │       ├── MainActivity.kt # Tam ekran SurfaceView arayüzü
│   │       ├── decoder/        # MediaCodec Donanım Video Çözücüsü
│   │       ├── network/        # ADB Soket İstemcisi
│   │       └── protocol/       # İkili (Binary) Akış Protokolü
│   └── build.gradle.kts
│
├── drivers/                    # Dijital İmzalı Windows IddCx Sürücü Paketleri
│   └── vdd/                    # Virtual Display Driver & devcon.exe
│
└── tools/                      # Taşınabilir Gerekli Araçlar
    ├── adb/                    # Android Debug Bridge (Kablo algılama ve tünelleme)
    └── ffmpeg/                 # D3D11 DXGI ekran yakalama ve H.264 kodlama motoru
```

---

## 🚀 Hızlı Başlangıç ve Kullanım Rehberi

### Adım 1: Sanal Monitör Sürücüsünü Yükleyin (Tek Seferlik)
1. `install-driver.bat` dosyasına sağ tıklayıp **"Yönetici olarak çalıştır"** seçin.
2. İşlem tamamlandığında Windows Masaüstüne sağ tıklayıp *Görüntü Ayarları*na girdiğinizde 2. bir ekranın belirdiğini göreceksiniz.
3. Windows'ta ekranı **"Bu ekranları genişlet"** (Extend these displays) moduna alın.

### Adım 2: Tablette USB Hata Ayıklamayı Açın
1. Tabletinizde **Ayarlar > Tablet Hakkında** bölümüne gidin.
2. **Derleme Numarası (Build Number)** üzerine 7 kez art arda basarak Geliştirici Seçeneklerini aktif edin.
3. **Ayarlar > Geliştirici Seçenekleri**ne girip **USB Hata Ayıklama (USB Debugging)** özelliğini açın.
4. Tableti Type-C kablosuyla bilgisayara bağlayın. Ekranda "Bu bilgisayara her zaman izin ver" kutucuğunu işaretleyip **Tamam** deyin.

### Adım 3: Android Uygulamasını Tablete Yükleyin
- Tableti Type-C ile bağladıktan sonra `install-apk.bat` dosyasına çift tıklayarak `DeskPad.apk` dosyasını otomatik olarak tabletinize yükleyebilirsiniz.
- (Alternatif olarak `DeskPad.apk` dosyasını tabletinize kopyalayıp dosya yöneticisinden de kurabilirsiniz).

### Adım 4: PC Sunucusunu Başlatın
1. `run-host.bat` dosyasını çalıştırın.
2. Sunucu tabletinizi otomatik algılar, port tünelini kurar ve tablet uygulamasını açtığınız anda masaüstünüz tablete yansımaya başlar!

### Kontrol Paneli Kısayolları (Konsol):
- **[E]** : Sanal 2. Monitörü İstediğiniz Zaman Açıp / Kapatma (Enable / Disable)
- **[S]** : Yayını Manuel Başlatma / Durdurma
- **[D]** : Yakalanacak Ekranı Değiştirme (Ekran 1 / Ekran 2)
- **[R]** : Çözünürlük ve FPS Profili Değiştirme (1080p, 2K, 60 FPS, 120 FPS)
- **[Q]** : Çıkış
