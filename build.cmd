@echo off
rem Build ClipShelf with the csc that ships with Windows (.NET Framework 4.x)
setlocal
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REFS=/r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
set OPTS=/nologo /target:winexe /codepage:65001 /optimize+ /utf8output

taskkill /f /im ClipShelf.exe >nul 2>&1

"%CSC%" %OPTS% %REFS% /out:ClipShelf.exe src\ClipShelf.cs || exit /b 1
start "" /wait ClipShelf.exe --make-icon ClipShelf.ico || exit /b 1
"%CSC%" %OPTS% %REFS% /win32icon:ClipShelf.ico /out:ClipShelf.exe src\ClipShelf.cs || exit /b 1
echo BUILD OK
