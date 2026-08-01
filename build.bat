@echo off
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /win32manifest:app.manifest /out:GolfDeck.exe Program.cs
if %errorlevel%==0 (echo Built GolfDeck.exe) else (echo BUILD FAILED)
