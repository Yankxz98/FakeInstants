@echo off
echo ========================================
echo     FakeInstants - Publicação Automática
echo ========================================
echo.

echo Publicando aplicação...
dotnet publish -c Release -p:PublishTrimmed=false --self-contained true -r win-x64

if %errorlevel% neq 0 (
    echo.
    echo ❌ Erro durante a publicação!
    pause
    exit /b 1
)

echo.
echo ✅ Publicação concluída com sucesso!
echo.
echo Arquivos publicados em: bin\Release\net8.0\publish\
echo.
echo Para executar a versão portátil:
echo 1. Copie a pasta 'publish' para seu USB
echo 2. Execute o arquivo fakeinstants.exe
echo.
pause

