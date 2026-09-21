@echo off
rem Build the Windows x64 player from a Windows machine.
rem
rem   build.bat                -> Build\Windows\RCRACE.exe
rem   build.bat C:\out\dir     -> C:\out\dir\RCRACE.exe
rem   set UNITY=C:\path\to\Unity.exe  (override editor autodetect)
rem
rem Uses the Unity editor version from ProjectSettings\ProjectVersion.txt, installed via Unity Hub.
setlocal
set ROOT=%~dp0
set ROOT=%ROOT:~0,-1%
set OUT=%~1
if "%OUT%"=="" set OUT=%ROOT%\Build\Windows
set LOG=%ROOT%\Build\build.log
if not exist "%ROOT%\Build" mkdir "%ROOT%\Build"

for /f "tokens=2" %%v in ('findstr /b "m_EditorVersion:" "%ROOT%\ProjectSettings\ProjectVersion.txt"') do set VERSION=%%v

if "%UNITY%"=="" set UNITY=C:\Program Files\Unity\Hub\Editor\%VERSION%\Editor\Unity.exe
if not exist "%UNITY%" (
    echo Unity %VERSION% not found at "%UNITY%".
    echo Install it with Unity Hub, or:  set UNITY=C:\path\to\Unity.exe
    exit /b 1
)

echo Unity:   %UNITY%
echo Project: %ROOT%
echo Output:  %OUT%
echo Log:     %LOG%
echo Building (this takes a few minutes the first time)...

"%UNITY%" -batchmode -nographics -quit -projectPath "%ROOT%" -executeMethod BuildScript.BuildWindows -buildPath "%OUT%" -logFile "%LOG%"
set STATUS=%ERRORLEVEL%

findstr /c:"BUILD OK" /c:"BUILD FAILED" /c:"error CS" "%LOG%"
if not "%STATUS%"=="0" (
    echo Build failed ^(exit %STATUS%^). See %LOG%
    exit /b %STATUS%
)
echo Done: %OUT%\RCRACE.exe  ^(zip the whole folder to share it^)
endlocal
