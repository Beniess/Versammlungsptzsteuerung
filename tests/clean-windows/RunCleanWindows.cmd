@echo off
powershell.exe -NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\Payload\Run-CleanWindowsGuest.ps1 > C:\Evidence\guest-console.log 2>&1
