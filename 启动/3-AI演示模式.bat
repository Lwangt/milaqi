@echo off
chcp 65001 >nul
cd /d "E:\code\milaqi\build\windows"
echo Starting Milaqi [AI vs AI demo]...
start "" "Milaqi.exe" -- --local --demo
