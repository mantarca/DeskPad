@echo off
chcp 65001 > nul
echo =========================================================
echo    DeskPad - Sanal Monitör Sürücüsünü Kaldırma
echo =========================================================
echo.

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [HATA] Bu betik YÖNETİCİ OLARAK çalıştırılmalıdır!
    pause
    exit /b 1
)

set DEVCON="%~dp0drivers\vdd\Dependencies\devcon.exe"

echo Sanal monitör sürücüsü kaldırılıyor...
%DEVCON% remove Root\MttVDD

echo.
echo [TAMAMLANDI] Sanal monitör ve sürücü sistemden kaldırıldı.
echo.
pause
