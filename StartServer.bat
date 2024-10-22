@echo off
:: Verifica se o script está sendo executado como administrador
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Solicitar permissões de administrador...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

:: Definir as variáveis de caminho
set AccountServer=%~dp0\src\Source\Distribution\DigitalWorldOnline.Account.Host\bin\Debug\net7.0\DigitalWorldOnline.Account.exe
set CharacterServer=%~dp0\src\Source\Distribution\DigitalWorldOnline.Character.Host\bin\Debug\net7.0\DigitalWorldOnline.Character.exe
set GameServer=%~dp0\src\Source\Distribution\DigitalWorldOnline.Game.Host\bin\Debug\net7.0\DigitalWorldOnline.Game.exe
set RoutineServer=%~dp0\src\Source\Distribution\DigitalWorldOnline.Routine.Host\DigitalWorldOnline.Routine\bin\Debug\net7.0\DigitalWorldOnline.Routine.exe

:: Espera 1 segundo
timeout /t 1 /nobreak > nul

:: Executa o Account.Host
start "" "%AccountServer%"

:: Espera 2 segundo
timeout /t 2 /nobreak > nul

:: Executa o Character.Host
start "" "%CharacterServer%"

:: Espera 2 segundo
timeout /t 2 /nobreak > nul

:: Executa o Game.Host
start "" "%GameServer%"

:: Espera 2 segundo
timeout /t 2 /nobreak > nul

:: Executa o Routine.Host
start "" "%RoutineServer%"
