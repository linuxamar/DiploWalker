#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# run-tests-in-docker.sh – Lance les tests DiploWalker depuis un conteneur
# Compatible Docker.
#
# Usage :
#   ./run-tests-in-docker.sh                          # tests unitaires
#   ./run-tests-in-docker.sh -DoTests -DoPublish      # tests + publication
#   ./run-tests-in-docker.sh -Clean -Restore          # nettoyage + restauration
# ---------------------------------------------------------------------------

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# --- Vérifications préliminaires -------------------------------------------

if [[ "$(uname -s)" != "Windows_NT" ]]; then
    echo "✗ Ce script doit s'exécuter dans un conteneur Windows (Docker Desktop en mode Windows containers)."
    echo "  Sur un hôte Linux/macOS, utilisez un runner Windows en CI ou Docker Desktop sur Windows."
    exit 1
fi

if [[ ! -f "$PROJECT_ROOT/pipeline.ps1" ]]; then
    echo "✗ pipeline.ps1 introuvable. Assurez-vous d'être à la racine du dépôt DiploWalker."
    exit 1
fi

# --- Construction des arguments --------------------------------------------

PS_ARGS=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        -Clean)       PS_ARGS+=("-Clean"); shift ;;
        -Restore)     PS_ARGS+=("-Restore"); shift ;;
        -DoTests)     PS_ARGS+=("-DoTests"); shift ;;
        -DoPublish)   PS_ARGS+=("-DoPublish"); shift ;;
        -NoSignTests) PS_ARGS+=("-NoSignTests"); shift ;;
        -TestLogDir)  PS_ARGS+=("-TestLogDir"); "$2"; shift 2 ;;
        *) echo "Option inconnue : $1"; exit 1 ;;
    esac
done

# Par défaut : lancer les tests si aucun flag n'est fourni
if [[ ${#PS_ARGS[@]} -eq 0 ]]; then
    PS_ARGS+=("-DoTests")
fi

# --- Lancement --------------------------------------------------------------

echo ""
echo "══════════════════════════════════════════════════════════════"
echo " DiploWalker – Tests depuis le conteneur Windows (Docker)"
echo "══════════════════════════════════════════════════════════════"
echo ""

cd "$PROJECT_ROOT"
pwsh -File ./pipeline.ps1 "${PS_ARGS[@]}"
exit $?
