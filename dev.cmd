@echo off
REM Sobe o ambiente local completo: API (Neon via user-secrets) + frontend.
REM Uso: abrir o cmd na pasta do projeto e digitar `dev`

start "Vivarium API" cmd /k "dotnet run --project src\Vivarium.Api --launch-profile http"
start "Vivarium Front" cmd /k "npm run dev --prefix frontend"

REM Espera os servidores subirem antes de abrir o navegador
timeout /t 4 /nobreak >nul
start "" http://localhost:5173
