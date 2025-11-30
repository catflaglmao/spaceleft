@echo off
set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC_PATH%" (
    echo Error: csc.exe not found at %CSC_PATH%
    echo Please ensure .NET Framework is installed or update the path in build.bat
    pause
    exit /b 1
)

"%CSC_PATH%" /nologo /out:spaceleft.exe spaceleft.cs
if %ERRORLEVEL% EQU 0 (
    echo Build successful!
) else (
    echo Build failed.
)
