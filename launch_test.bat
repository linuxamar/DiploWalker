@echo off
rem ------------------------------------------------------------
rem launch_test.bat
rem Reconstruit l'image Docker/Podman et lance les tests dans le conteneur
rem ------------------------------------------------------------

rem Déterminer le répertoire racine du projet (le répertoire contenant ce .bat)
set "PROJECT_ROOT=%~dp0"
rem Supprimer le backslash final éventuel
if "%PROJECT_ROOT:~-1%"=="\" set "PROJECT_ROOT=%PROJECT_ROOT:~0,-1%"

rem Se placer dans le répertoire racine du projet
pushd "%PROJECT_ROOT%"

echo Building the test image (diplowalker-tests)...
podman build -t diplowalker-tests -f Docker\Dockerfile .
if errorlevel 1 (
    echo Erreur lors de la construction de l'image.
    popd
    exit /b 1
)

echo Running the test suite inside the container...
podman run --rm -v "%PROJECT_ROOT%:/workspaces/DiploWalker" diplowalker-tests
if errorlevel 1 (
    echo Erreur lors de l'exécution des tests.
    popd
    exit /b 1
)

popd
exit /b 0
