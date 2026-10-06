@echo off
chcp 65001 >nul
cd /d "%~dp0"
if not exist "dist\Autotyper.exe" (
 echo Сначала запустите build-and-run.bat.
 pause
 exit /b 1
)
start "" "%~dp0dist\Autotyper.exe"
