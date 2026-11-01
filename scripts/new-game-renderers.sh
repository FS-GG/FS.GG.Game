#!/usr/bin/env bash
#
# GAME-RENDER-ADOPT-01.1 (FS.GG.Game#745): generate the SVG, Pixi and Babylon renderer module for one game-state
# type from eng/render-kit/GameRenderers.fs.template. The module declares the three adapters over one shared
# `view: State -> Scene` (SceneRenderers + BrowserRenderers), starts them on a RendererDriver, and exposes the
# conformance subject a browser test runs through RendererConformance. Only `view` is left to write.
#
#   usage: scripts/new-game-renderers.sh --namespace <Ns> --module <Name> --state <Type> [--label <text>]
#                                        [--width <px>] [--height <px>] [--output <file> | --stdout]
#
#   example: scripts/new-game-renderers.sh --namespace MyGame.Client --module Renderers --state MyGame.Model \
#              --label "My game" --output src/Client/Renderers.fs
#
# The generated sample tests/Render.Browser.Smoke/GeneratedRenderers.fs is this script's output for the smoke's
# arguments; scripts/test-render-browser-host.sh requires it to be byte-identical, compiles it with Fable and runs
# the conformance kit on it, so the template cannot rot.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATE="$SCRIPT_DIR/../eng/render-kit/GameRenderers.fs.template"

usage() {
  sed -n '8,12p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

namespace="" module="" state="" label="" width="320.0" height="180.0" output="" stdout=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --namespace) namespace="${2:-}"; shift 2 ;;
    --module) module="${2:-}"; shift 2 ;;
    --state) state="${2:-}"; shift 2 ;;
    --label) label="${2:-}"; shift 2 ;;
    --width) width="${2:-}"; shift 2 ;;
    --height) height="${2:-}"; shift 2 ;;
    --output) output="${2:-}"; shift 2 ;;
    --stdout) stdout=1; shift ;;
    -h | --help) usage ;;
    *) echo "new-game-renderers: unknown argument '$1'" >&2; usage ;;
  esac
done

[[ -n $namespace && -n $module && -n $state ]] || usage
[[ -n $output || $stdout == 1 ]] || {
  echo "new-game-renderers: give --output <file> or --stdout" >&2
  exit 2
}

# The substituted values land in F# source: accept identifiers and dotted paths only, a plain label and finite
# positive pixel sizes, so nothing can be injected through them.
ident='[A-Za-z_][A-Za-z0-9_]*'
[[ $namespace =~ ^$ident(\.$ident)*$ ]] || { echo "new-game-renderers: invalid --namespace '$namespace'" >&2; exit 2; }
[[ $module =~ ^$ident$ ]] || { echo "new-game-renderers: invalid --module '$module'" >&2; exit 2; }
[[ $state =~ ^$ident(\.$ident)*$ ]] || { echo "new-game-renderers: invalid --state '$state'" >&2; exit 2; }
[[ -n $label ]] || label="$module"
[[ $label =~ ^[A-Za-z0-9\ ._()-]+$ ]] || { echo "new-game-renderers: --label may hold letters, digits, spaces and ._()- only" >&2; exit 2; }
for size in "$width" "$height"; do
  [[ $size =~ ^[0-9]+\.[0-9]+$ && ! $size =~ ^0+\.0+$ ]] || {
    echo "new-game-renderers: sizes are positive decimals such as 320.0 (got '$size')" >&2
    exit 2
  }
done
[[ -f $TEMPLATE ]] || { echo "new-game-renderers: template not found: $TEMPLATE" >&2; exit 1; }

render() {
  sed \
    -e "s/@NAMESPACE@/$namespace/g" \
    -e "s/@MODULE@/$module/g" \
    -e "s/@STATE@/$state/g" \
    -e "s/@LABEL@/$label/g" \
    -e "s/@WIDTH@/$width/g" \
    -e "s/@HEIGHT@/$height/g" \
    "$TEMPLATE"
}

if [[ $stdout == 1 ]]; then
  render
else
  [[ ! -e $output ]] || { echo "new-game-renderers: refusing to overwrite $output" >&2; exit 1; }
  mkdir -p "$(dirname "$output")"
  render >"$output"
  echo "new-game-renderers: wrote $output (fill in $module.view)"
fi
