#!/usr/bin/env bash
# State machine presence check, run after the coverage gate against the Cobertura reports it leaves behind.
#
# A package whose sources carry an async or iterator body must report at least one state machine class. The compiler
# lowers such a body into a class of its own, a state machine, and a collector told to skip compiler-generated code
# leaves that class out of the report entirely. The body is then not reported as uncovered, it is absent, and the line
# gate passes over code it never saw. That was this repository's state until this check was added, caused by one attribute in
# tests/coverage.runsettings.template. The check is made here, against the reports the gate reads rather than against
# the configuration, so that whatever hides a state machine is caught: the shared template, a project's own
# RunSettingsFilePath, its Include line, the Directory.Build.targets that materialises the template, a broadly applied
# [ExcludeFromCodeCoverage], or the collector itself.
#
# Which packages must report one is read from their sources rather than from a list here, so that a package writing its
# first async method is checked the day it does, and no entry can rot into a blind spot. A package's sources are
# src/<package> or samples/<package>; a package whose sources cannot be found fails the check rather than passing
# silently. Comment lines are skipped when the sources are scanned; any other mention of the words errs towards
# demanding a state machine, which fails visibly instead of quietly. The class name pattern is matched, not "MoveNext":
# a hand-written enumerator has a MoveNext of its own and would satisfy the weaker check forever. The pattern takes all
# three shapes the compiler writes: <Method>d__N for a method, <Method>d__N`1 for a generic one, and <<Method>b__N_M>d
# for an async lambda or local function. Matching only the first failed Bsft.Resilience, Bsft.EventAggregator and
# Bsft.Auditing.Chat, whose only async bodies are generic, and Bsft.Apps.Http, whose only ones are endpoint lambdas.
#
# Usage: tests/check-state-machine-coverage.sh [results-root]   (default: TestResults/coverage)
set -euo pipefail

root="${1:-TestResults/coverage}"
mapfile -t reports < <(find "$root" -name coverage.cobertura.xml 2>/dev/null | sort)
if [ "${#reports[@]}" -eq 0 ]; then
  echo "::error::No coverage.cobertura.xml under $root; run the coverage gate first."
  exit 1
fi

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

declare -A seen=()
declare -A machines=()
for report in "${reports[@]}"; do
  while read -r name count; do
    seen[$name]=1
    machines[$name]=$(( ${machines[$name]:-0} + count ))
  done < <(awk '
    /<package name="/ {
      name = $0; sub(/^.*<package name="/, "", name); sub(/".*$/, "", name)
      count = 0
      next
    }
    /class name="[^"]*&lt;[^"]*&gt;d(__[0-9]+)?(`[0-9]+)?"/ { count++ }
    /<\/package>/ { if (name != "") { print name, count; name = "" } }
  ' "$report")
done

if [ "${#seen[@]}" -eq 0 ]; then
  echo "::error::The ${#reports[@]} coverage reports under $root name no package."
  exit 1
fi

# The directory holding a package's sources, or nothing when there is none.
sources_of() {
  local candidate
  for candidate in "$repo/src/$1" "$repo/samples/$1"; do
    if [ -d "$candidate" ]; then
      printf '%s\n' "$candidate"
      return
    fi
  done
}

# Whether a package's sources carry a body the compiler lowers into a state machine class.
has_lowered_body() {
  find "$1" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 \
    | xargs -0 grep -hE '(^|[^[:alnum:]_])(async|yield)[[:space:]]' \
    | grep -qvE '^[[:space:]]*(//|\*|/\*)'
}

failed=0
with_machines=0
without_bodies=0
while read -r name; do
  if [ "${machines[$name]}" -gt 0 ]; then
    with_machines=$(( with_machines + 1 ))
    continue
  fi

  sources=$(sources_of "$name")
  if [ -z "$sources" ]; then
    echo "::error::$name is reported but has no sources under src/ or samples/, so whether its async and iterator"
    echo "::error::bodies are measured cannot be decided. Name the package's directory after the package."
    failed=1
  elif has_lowered_body "$sources"; then
    echo "::error::$name writes async or iterator bodies, but its reports hold no state machine class (<Method>d__N)."
    echo "::error::Those bodies are measured nowhere, so the line gate passes over them. Check ExcludeByAttribute and"
    echo "::error::the Include line in tests/coverage.runsettings.template, a RunSettingsFilePath of the test project,"
    echo "::error::and any [ExcludeFromCodeCoverage] that was widened."
    failed=1
  else
    without_bodies=$(( without_bodies + 1 ))
  fi
done < <(printf '%s\n' "${!seen[@]}" | sort)

echo "State machine check: ${#reports[@]} reports, ${#seen[@]} packages; $with_machines report state machines," \
  "$without_bodies write no async or iterator body."
exit "$failed"
