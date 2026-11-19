@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Play-Astella2D.ps1"
if errorlevel 1 pause
