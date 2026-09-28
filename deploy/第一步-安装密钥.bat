@echo off
chcp 65001 >nul
cd /d %~dp0

REM ====== Configuration ======
REM Replace YOUR_SERVER_IP with your real server IP.
set SERVER_IP=YOUR_SERVER_IP

if "%SERVER_IP%"=="YOUR_SERVER_IP" (
  echo Please edit this file and replace YOUR_SERVER_IP with your server's IP.
  pause
  exit /b 1
)

echo Running, please wait...
(
  echo ===== INSTALL LOG %date% %time% =====
  type "key\deploy_key.pub" | ssh -v -o StrictHostKeyChecking=accept-new root@%SERVER_IP% "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys && sort -u ~/.ssh/authorized_keys -o ~/.ssh/authorized_keys && echo INSTALL_OK"
  echo EXITCODE=%errorlevel%
) > "key\install.log" 2>&1
notepad "key\install.log"
echo.
echo Log saved to key\install.log and opened in notepad.
pause
