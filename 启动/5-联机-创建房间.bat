@echo off
chcp 65001 >nul
cd /d "E:\code\milaqi\build\windows"
echo Starting Milaqi as HOST on UDP 27015...
start "" "Milaqi.exe" -- --host --port=27015
