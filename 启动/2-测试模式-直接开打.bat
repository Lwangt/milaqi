@echo off
chcp 65001 >nul
cd /d "E:\code\milaqi\build\windows"
echo Starting Milaqi [test mode: straight into a match vs AI]...
start "" "Milaqi.exe" -- --local
