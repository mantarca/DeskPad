@echo off
chcp 65001 > nul
echo =========================================================
echo    DeskPad - Sanal Monitör Sürücüsü Kurulumu
echo =========================================================
echo.

:: Yönetici yetkisi kontrolü
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [HATA] Bu betik YÖNETİCİ OLARAK çalıştırılmalıdır!
    echo Lütfen bu dosyaya SAĞ TIKLAYIP "Yönetici olarak çalıştır" deyin.
    echo.
    pause
    exit /b 1
)

set DEVCON="%~dp0drivers\vdd\Dependencies\devcon.exe"
set INF="%~dp0drivers\vdd\SignedDrivers\x86\VDD\MttVDD.inf"

echo [1/2] Sürücü kontrol ediliyor...
%DEVCON% status Root\MttVDD > nul 2>&1
if %errorLevel% equ 0 (
    echo [BILGI] Sanal monitör sürücüsü zaten kurulu.
) else (
    echo [2/2] IddCx Sanal Monitör Sürücüsü sisteme yükleniyor...
    %DEVCON% install %INF% Root\MttVDD
    if %errorLevel% equ 0 (
        echo [BAŞARILI] Sürücü başarıyla kuruldu!
    ) else (
        echo [UYARI] Sürücü kurulum kodu: %errorLevel%
    )
)

echo.
echo Sanal Monitör etkinleştiriliyor...
%DEVCON% enable Root\MttVDD

echo.
echo [TAMAMLANDI] Windows Görüntü Ayarlarını açarak 2. Monitörü görebilirsiniz.
echo.
pause
