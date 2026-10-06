@echo off
chcp 65001 >nul
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
echo [1/5] Проверка компонентов...
where dotnet >nul 2>&1
if errorlevel 1 goto no_dotnet
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto no_vs
set "VSINSTALL="
for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%I"
if not defined VSINSTALL goto no_vs
call "%VSINSTALL%\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul
if errorlevel 1 goto failed
if not defined INTERCEPTION_ROOT set "INTERCEPTION_ROOT=%CD%\vendor\Interception"
if exist "%INTERCEPTION_ROOT%\library\x64\interception.lib" goto sdk_found
echo Укажите папку распакованного SDK Interception, без кавычек:
set /p "INTERCEPTION_ROOT=> "
:sdk_found
if not exist "%INTERCEPTION_ROOT%\library\interception.h" goto no_sdk
if not exist "%INTERCEPTION_ROOT%\library\x64\interception.lib" goto no_sdk
if not exist "%INTERCEPTION_ROOT%\library\x64\interception.dll" goto no_sdk
if not exist build mkdir build
if not exist dist mkdir dist
echo [2/5] Сборка C++ DLL...
cl /nologo /LD /EHsc /std:c++17 /utf-8 /O2 /W4 /MT /I"%INTERCEPTION_ROOT%\library" native\engine.cpp /Fo"build\engine.obj" /link /OUT:"build\typing_engine.dll" /IMPLIB:"build\typing_engine.lib" "%INTERCEPTION_ROOT%\library\x64\interception.lib" user32.lib >build\native-build.log 2>&1
if errorlevel 1 goto native_failed
echo [3/5] Восстановление NuGet и сборка EXE. Первый запуск требует интернета...
dotnet publish app\Autotyper.csproj -c Release -r win-x64 --self-contained true -o dist -p:PublishSingleFile=false >build\desktop-build.log 2>&1
if errorlevel 1 goto desktop_failed
echo [4/5] Копирование нативных библиотек...
copy /y build\typing_engine.dll dist\typing_engine.dll >nul
if errorlevel 1 goto failed
copy /y "%INTERCEPTION_ROOT%\library\x64\interception.dll" dist\interception.dll >nul
if errorlevel 1 goto failed
echo [5/5] Готово: dist\Autotyper.exe
echo Сохраняйте всю папку dist, а не только EXE.
exit /b 0
:no_dotnet
echo ОШИБКА: установите .NET SDK 8 или новее: https://dotnet.microsoft.com/download
exit /b 1
:no_vs
echo ОШИБКА: нужны Visual Studio 2022 Build Tools с C++ x64 и Windows SDK.
echo https://visualstudio.microsoft.com/downloads/
exit /b 1
:no_sdk
echo ОШИБКА: SDK Interception не найден. Нужны library\interception.h и library\x64\interception.lib, interception.dll.
echo https://github.com/oblitum/Interception/releases
exit /b 1
:native_failed
type build\native-build.log
goto failed
:desktop_failed
type build\desktop-build.log
goto failed
:failed
echo Сборка не завершена. Проверьте журнал в папке build.
exit /b 1
