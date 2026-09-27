@echo off
chcp 65001 >nul
cd /d "E:\code\milaqi\build\windows"
echo Starting Milaqi [3D unit model sheet]...
start "" "Milaqi.exe" -- --modelsheet
