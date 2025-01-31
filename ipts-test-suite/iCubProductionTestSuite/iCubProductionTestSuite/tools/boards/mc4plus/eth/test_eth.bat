:start
@echo off

Rem ping an IP address and check the result - davide.tome@iit.it
@echo off

if %OS%==32BIT set PATH=%ProgramFiles%
if %OS%==64BIT set PATH=%ProgramFiles(x86)%

set ip=10.0.1.99
%SystemRoot%\system32\ping.exe -n 1 %ip% >nul
if errorlevel 1 goto error

:end
echo Test Passed! %ip% is available 
Rem pause
EXIT 0

:error
echo Test Failed! %ip% is NOT availabl
EXIT 1