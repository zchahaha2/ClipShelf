@echo off
rem Build ClipShelf.exe, then the installer dist\ClipShelf-Setup-<version>.exe (needs Inno Setup 6).
rem Set ISCC=path\to\ISCC.exe if Inno Setup is not in a standard location.
setlocal
cd /d "%~dp0"
call "%~dp0build.cmd" || exit /b 1

if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC (
  echo Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php or set ISCC=path\to\ISCC.exe
  exit /b 1
)
"%ISCC%" /Q installer\ClipShelf.iss || exit /b 1
echo INSTALLER OK
