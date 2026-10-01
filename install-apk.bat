@echo off
chcp 65001 > nul
echo =========================================================
echo    DeskPad - Android APK Tablete Yükleme
echo =========================================================
echo.

set ADB="%~dp0tools\adb\adb.exe"
set APK="%~dp0DeskPad.apk"

echo Bağlı Android cihazlar kontrol ediliyor...
%ADB% devices -l
echo.

echo Tabletinize "DeskPad.apk" yükleniyor...
%ADB% install -r %APK%
if %errorLevel% equ 0 (
    echo.
    echo [BAŞARILI] DeskPad uygulaması tabletinize yüklendi!
    echo Tabletinizin ana ekranından veya menüsünden "DeskPad" uygulamasını açabilirsiniz.
) else (
    echo.
    echo [UYARI] Yükleme başarısız oldu. Lütfen şunları kontrol edin:
    echo 1. Tabletin USB Hata Ayıklama (USB Debugging) modunun açık olduğundan emin olun.
    echo 2. Tablet ekranındaki "Bu bilgisayara izin ver" uyarısını onaylayın.
)

echo.
pause
