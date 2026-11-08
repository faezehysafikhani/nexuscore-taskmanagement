#!/usr/bin/env bash
# Keeps the graphify knowledge graph ready in every Claude Code session: a new chat, /clear, a fresh
# cloud container, another branch. AST only - no LLM, no API key, no tokens. Never fails the session.
export PATH="$HOME/.local/bin:$PATH"
cd "$(git rev-parse --show-toplevel 2>/dev/null || pwd)" || exit 0

# 1. The tool (a fresh container does not have it).
if ! command -v graphify >/dev/null 2>&1; then
  if command -v uv >/dev/null 2>&1; then uv tool install graphifyy >/dev/null 2>&1
  else pip install --user -q graphifyy >/dev/null 2>&1; fi
fi
command -v graphify >/dev/null 2>&1 || exit 0

# 2. The graph. graphify-out/ is not committed (it differs per branch), so it is built here, and rebuilt
#    when the checked-out commit changed since the last build (branch switch, pull, merge, rebase).
head="$(git rev-parse --abbrev-ref HEAD 2>/dev/null)@$(git rev-parse HEAD 2>/dev/null)"
stamp="graphify-out/.built-at"
if [ ! -f graphify-out/graph.json ]; then
  graphify extract . --code-only >/dev/null 2>&1
elif [ "$(cat "$stamp" 2>/dev/null)" != "$head" ]; then
  # FORCE: a smaller branch has fewer nodes, and graphify refuses to overwrite a bigger graph otherwise.
  GRAPHIFY_FORCE=1 graphify update . >/dev/null 2>&1 || graphify extract . --code-only --force >/dev/null 2>&1
fi
mkdir -p graphify-out && echo "$head" > "$stamp"
exit 0
