@echo off
chcp 65001 >nul
title 计算机信息收集系统
cd /d %~dp0
echo 正在启动服务，请勿关闭本窗口...
"runtime\node-v22.23.3-win-x64\node.exe" server.js
pause
