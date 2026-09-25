@echo off
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" x86
if errorlevel 1 exit /b 1
cl /nologo /LD /O1 /MT /W3 /DWIN32 /Fo%~dp0 /FeC:\AetheriumPlay\AetheriumLauncher\SlotIsolation.dll %~dp0SlotIsolation.c /link /NOLOGO /MACHINE:X86 kernel32.lib
exit /b %errorlevel%
