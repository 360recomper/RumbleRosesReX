@echo off
rem Rumble Roses XX Recomp with DLSS 5 (neural rendering), 4K at 60 fps. Same saves and mods as the normal launch.
rem Set up first with "Setup DLSS 5.bat". F9 = DLSS 5 on/off, F10 = DLSS 5 settings panel, Home = ReShade menu.
rem ReShade is loaded for this game only (no system-wide install).
setlocal
set "ROOT=%~dp0"
set "D=%ROOT%dlss5\"
if not exist "%D%host64\nvngx_dlssnr.dll" (
  echo DLSS 5 is not set up yet. Run "Setup DLSS 5.bat" first.
  pause
  exit /b 1
)
rem Use the current game build: copy the exe and its DLLs from game\ (only newer files), then the DLSS 5 runtime
rem DLL if this add-on brings its own.
xcopy /d /y /q "%ROOT%game\rumble_roses_xx.exe" "%D%" >nul
xcopy /d /y /q "%ROOT%game\*.dll" "%D%" >nul
if exist "%D%runtime\rexruntime.dll" copy /y "%D%runtime\rexruntime.dll" "%D%rexruntime.dll" >nul
set "VK_ADD_IMPLICIT_LAYER_PATH=%D%ReShade64.json"
copy /y "%D%ReShade.template.ini" "%D%ReShade.ini" >nul
copy /y "%D%dlss5-feed.template.cfg" "%D%dlss5-feed.cfg" >nul
start "" /min powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%D%align-dlss5-panel.ps1"
cd /d "%D%"
start "" rumble_roses_xx.exe "--game_data_root=%ROOT%game\assets" %*
