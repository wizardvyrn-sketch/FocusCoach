@echo off
rem FocusCoach (portable): keeps all data in the "data" folder next to this file
set "FOCUSCOACH_DATA=%~dp0data"
start "" "%~dp0app\FocusCoach.exe"
