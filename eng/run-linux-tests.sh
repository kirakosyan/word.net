#!/usr/bin/env bash
set -euo pipefail
case "$(uname -m)" in
  aarch64) triplet=aarch64-linux-gnu ;;
  x86_64) triplet=x86_64-linux-gnu ;;
  *) echo 'Unsupported validation architecture' >&2; exit 1 ;;
esac
export WORDNET_TESSERACT_LIBRARY="/usr/lib/$triplet/libtesseract.so.5"
mkdir -p "${RESULTS_DIRECTORY:-/results}"
{
  uname -m
  dotnet --info
  printf '\nExecutable-memory override: %s\n' "${DOTNET_EnableWriteXorExecute:-runtime default}"
  printf 'Package under test: %s\n' "${WORDNET_PACKAGE_VERSION:-project reference}"
} | tee "${RESULTS_DIRECTORY:-/results}/environment.txt"
package=()
if [[ -n "${WORDNET_PACKAGE_VERSION:-}" ]]; then
  package=("-p:WordnetPackageVersion=$WORDNET_PACKAGE_VERSION")
fi
for framework in net8.0 net10.0; do
  dotnet test tests/wordnet.Conversion.Tests -c Release -f "$framework" --no-build --no-restore "${package[@]}" \
    --logger "trx;LogFileName=$framework.trx" --results-directory "${RESULTS_DIRECTORY:-/results}"
done
