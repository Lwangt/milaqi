@echo off
chcp 65001 >nul
cd /d "E:\code\milaqi\build\windows"
echo Joining 127.0.0.1:27015  (edit this .bat to change the IP)...
start "" "Milaqi.exe" -- --join=127.0.0.1 --port=27015
