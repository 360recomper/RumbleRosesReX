@echo off
rem Sets up DLSS 5 for the Rumble Roses XX Recomp (downloads + checks the parts, asks for your nvngx_dlssnr.dll).
rem Nothing outside the dlss5 folder is changed. Details: dlss5\README-DLSS5.txt
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dlss5\setup-dlss5.ps1" %*
