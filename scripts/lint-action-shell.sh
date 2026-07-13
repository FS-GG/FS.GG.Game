#!/usr/bin/env bash
#
# Lint the shell inside COMPOSITE ACTIONS — .github/actions/**/action.yml (FS.GG.Game#135).
#
# This repo keeps shell in three places, and until #135 only two of them were ever read by a linter:
#
#   1. `run:` blocks in .github/workflows/*.yml   — actionlint -shellcheck (gate.yml, workflow-lint)
#   2. scripts/*.sh and shebang files             — shellcheck directly    (gate.yml, workflow-lint)
#   3. `run:` blocks in a COMPOSITE ACTION        — NOBODY. This script.
#
# The third arrived with the cold locked restore (.github/actions/locked-restore), and neither of the
# first two can see it:
#
#   * actionlint discovers `.github/workflows/*.yml` and nothing else. It never opens an action.yml —
#     and handed one EXPLICITLY it still will not lint it, it rejects it as a malformed workflow
#     (`"on" section is missing`). The file-count guard in workflow-lint compares `linted` against the
#     WORKFLOWS on disk, so it reports a truthful 6-of-6 and says nothing about any action at all.
#   * the scripts/ pass discovers shell by extension or shebang. Shell embedded in YAML has neither.
#
# So the ~20 lines of bash that decide whether every other job in gate.yml restores would have merged
# unread, beneath two green lint steps. Verified, not assumed: an unquoted `$NUGET_PACKAGES` planted
# in the action's run: block passes both of them at exit 0.
#
# That is the FS-GG/.github#416 shape — green over an unexamined subject — and it does not get to
# arrive in the gate's own restore.
#
# THE SUBJECT IS COUNTED TWICE, from two independent derivations, because the failure mode that
# matters here is not this script going red. It is this script finding NOTHING and saying so in
# green. So the blocks PyYAML parses out are counted against the `shell:` keys grepped out of the raw
# bytes, and a disagreement is a RED — see `expected` below.
#
# Usage:  scripts/lint-action-shell.sh
#   SHELLCHECK=<path>   the shellcheck binary to use (default: `shellcheck` on PATH). CI passes the
#                       pinned one, so this gate reproduces exactly on a laptop with the same pin.
#
set -euo pipefail

SHELLCHECK="${SHELLCHECK:-shellcheck}"

# Exit 2, not 1, when the script cannot RUN: "I could not check" is a different fact from "I checked
# and it is bad", and a gate whose whole subject is not-conflating-those does not get to conflate
# them itself. (The same reasoning, and the same exit code, as scripts/lib/test-harness.sh.)
command -v python3 >/dev/null || { echo "::error::python3 is required by $0"; exit 2; }
python3 -c 'import yaml' 2>/dev/null || { echo "::error::PyYAML is required by $0 (apt: python3-yaml)"; exit 2; }
command -v "$SHELLCHECK" >/dev/null 2>&1 || { echo "::error::shellcheck not found: '$SHELLCHECK'. Set SHELLCHECK=<path>."; exit 2; }

cd "$(git rev-parse --show-toplevel)"

# DISCOVERED, not listed. A hand-maintained file list goes stale in silence, and the silence here is
# an action nobody linted.
mapfile -d '' -t actions < <(git ls-files -z -- '.github/actions/**/action.yml' '.github/actions/**/action.yaml')

# No composite actions is an HONEST zero: there is no subject, so there is nothing to report green
# over. That is not the same fact as finding actions and extracting nothing from them, which is a red
# below — and keeping the two apart is the entire point of this script.
if [ "${#actions[@]}" -eq 0 ]; then
  echo "no composite actions in the tree — nothing to lint."
  exit 0
fi

# THE INDEPENDENT COUNT. GitHub REQUIRES `shell:` on every composite `run:` step, so grepping the raw
# bytes for that key is a SECOND derivation of the number of shell blocks — not a restatement of the
# parser's. If the extractor ever goes blind (a moved key, a parse returning None), these disagree
# and this script reds instead of linting nothing and reporting success.
#
# The value is matched as "any non-space", NOT as the literal `bash`. Matching `bash` exactly would
# make a correct action written `shell: bash  # composite steps must declare one` or `shell: "bash"`
# count as zero — and the mismatch would then red the job with a message blaming the EXTRACTOR, on a
# file its author wrote correctly. That is the false accusation #238 taught this repo to refuse, and
# a lint nobody can satisfy is a lint somebody deletes.
expected=0
for a in "${actions[@]}"; do
  n=$(grep -cE '^[[:space:]]*shell:[[:space:]]*[^[:space:]]' "$a" || true)
  expected=$((expected + n))
done

outdir="$(mktemp -d)"
trap 'rm -rf "$outdir"' EXIT

# Extracted with a REAL PyYAML parse, never a regex (the #251 precedent). The manifest it prints —
# one `file<TAB>shell<TAB>origin` row per block — is what the dispatch below reads, so the mapping
# from a shellcheck finding back to the action that owns it is data, not a filename convention some
# later reader has to reverse-engineer.
python3 - "$outdir" "${actions[@]}" >"$outdir/manifest" <<'PY'
import pathlib, re, sys, yaml

outdir, paths = pathlib.Path(sys.argv[1]), sys.argv[2:]
rows, n = [], 0
for p in paths:
    doc = yaml.safe_load(pathlib.Path(p).read_text()) or {}
    for i, step in enumerate((doc.get("runs") or {}).get("steps") or []):
        if not isinstance(step, dict) or step.get("run") is None:
            continue
        shell = str(step.get("shell") or "").strip()
        # `${{ }}` becomes a VARIABLE REFERENCE, not a bare word, and the difference is why this is
        # written down. Substituting a constant makes shellcheck read `case "${{ inputs.x }}"` as
        # `case "CONST"` and fire SC2194 ("this word is constant") on a correct action — a red the
        # author cannot act on. Braced, so an expression glued to adjacent text stays one name;
        # all-caps, so shellcheck reads it as an environment variable and does not then demand an
        # assignment (SC2154). Verified all three ways.
        body = re.sub(r"\$\{\{.*?\}\}", "${GH_EXPR}", step["run"], flags=re.S)
        # The counter prefix makes the name UNIQUE. Flattening the path alone is not enough:
        # `actions/foo-bar/` and `actions/foo_bar/` collapse to the same name, and the second would
        # silently overwrite the first — one block linted, two counted, and the reader left to work
        # out why. No shebang is written, because the dialect is passed to shellcheck with -s below;
        # that also keeps every reported line number an exact offset into the run: block.
        name = f"{n:03d}.{re.sub(r'[^A-Za-z0-9]+', '_', p).strip('_')}.{i}.sh"
        (outdir / name).write_text(body)
        rows.append(f"{name}\t{shell}\t{p} (step {i})")
        n += 1
print("\n".join(rows))
PY

mapfile -t rows < <(sed -n '/./p' "$outdir/manifest")
extracted=${#rows[@]}

echo "composite actions: ${#actions[@]} · shell steps declared: $expected · extracted: $extracted"

if [ "$extracted" -ne "$expected" ]; then
  echo "::error::extracted $extracted shell block(s) from ${#actions[@]} composite action(s), but" \
       "$expected step(s) declare a 'shell:'. The extractor is not reading what the actions actually" \
       "contain, so this lint was about to run over a subject it never saw."
  exit 1
fi

if [ "$extracted" -eq 0 ]; then
  echo "::error::${#actions[@]} composite action(s) in the tree and NOT ONE shell block came out of" \
       "them. shellcheck over zero files exits 0, so this would have been a GREEN over an unread" \
       "subject."
  exit 1
fi

# EVERY block is dispatched, and an unknown dialect is a RED rather than a skip.
#
# The first draft of this gate extracted only `shell: bash` and let everything else fall through —
# and its independent count skipped the same steps, so the two agreed and the step reported "clean"
# over a `shell: sh` block with an unquoted expansion in it. A gate that silently declines to read
# part of its subject, and then reports green on the whole of it, is the exact defect this file
# exists to close; it does not get to have it on the inside.
#
# So: shellcheck can read sh/bash/dash/ksh, and those are linted in their own dialect. Anything else
# (pwsh, python, node) it cannot read at all — that is not a shrug, it is an unread block, and the
# answer is to say so and stop. Extending this lint to a new dialect is a decision somebody makes on
# purpose, not an omission nobody notices.
declare -A by_shell=()
for row in "${rows[@]}"; do
  IFS=$'\t' read -r file shell origin <<<"$row"
  case "$shell" in
    bash | sh | dash | ksh) by_shell["$shell"]+="$file " ;;
    *)
      echo "::error::$origin declares 'shell: ${shell:-<none>}', which shellcheck cannot read. This" \
           "lint would have reported GREEN over that block without ever looking at it. Use a shell" \
           "shellcheck understands (bash/sh/dash/ksh), or extend $0 to cover '${shell:-<none>}'" \
           "DELIBERATELY — do not let it fall through."
      exit 1
      ;;
  esac
done

log="$outdir/shellcheck.log"
: >"$log"
rc=0
for shell in "${!by_shell[@]}"; do
  # shellcheck disable=SC2086  # the file list is built from the manifest; the names contain no spaces
  (cd "$outdir" && "$SHELLCHECK" -x -S info -s "$shell" -f gcc ${by_shell[$shell]}) >>"$log" 2>&1 || rc=$?
done
cat "$log"

# rc 0 = clean, 1 = findings (judged below). Anything ELSE is shellcheck failing to PROCESS a file,
# and a file that could not be read is a file that was not checked.
if [ "$rc" -ge 2 ]; then
  echo "::error::shellcheck exited $rc — it could not process an extracted block. That block went" \
       "unread; this is a RED, not a shrug."
  exit 1
fi

# An unfollowed `source` means the linter ran with half its subject invisible, and it invents
# confident, WRONG SC2034s for every variable the sourced file consumes (#266 filed three of them
# before finding that out). A composite action's run: block is extracted to a temp file to be linted,
# so a RELATIVE source in one cannot resolve — which is a reason to keep shared shell in scripts/ and
# CALL it, not a reason to let the block through half-read.
if grep -q '\[SC1091\]' "$log"; then
  echo "::error::shellcheck could not follow a file sourced by a composite action (SC1091, above), so" \
       "it linted that action with the source UNREAD. Put shared shell in scripts/ and call it rather" \
       "than sourcing it from an action."
  exit 1
fi

# THE FLOOR: warning — the same floor, from the same pinned binary, as the repo's other shell. One
# standard, not two.
blocking=$(grep -cE ': (error|warning):' "$log" || true)
if [ "$blocking" -ne 0 ]; then
  echo "::error::shellcheck found $blocking finding(s) at or above 'warning' in the shell inside a" \
       "composite action. The gcc-format lines above name the extracted block; map it back with the" \
       "manifest below. Line numbers are exact offsets into that step's run: block."
  echo "--- extracted block → action"
  printf '%s\n' "${rows[@]}"
  exit 1
fi

echo "shellcheck: clean at -S warning across $extracted composite-action block(s)."
