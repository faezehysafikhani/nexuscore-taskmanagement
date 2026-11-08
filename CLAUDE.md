## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- When graphify-out/graph.json exists, run `graphify query "<question>"` before your first grep, search or source-file read for a codebase question, and put this rule in every subagent prompt that explores code. Read raw files only for lines the graph or an earlier read already located. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
- A committed graphify-out/graph.json is rewritten whole on every build, so a large diff to it in a pull request is expected. Do not flag it as churn or suggest skipping the graph rebuild or its hooks.

- Token budget: always pass `--budget 1500` (or less) to `graphify query`; raise it only when the answer says it was truncated and the cut nodes matter. Prefer `graphify explain "<symbol>"` / `graphify path` over a wide query.
- The graph is rebuilt for free at the start of every session (see .claude/graphify-bootstrap.sh) and after a branch switch. If a result looks stale after you switched branch inside a session, run `GRAPHIFY_FORCE=1 graphify update .` (a smaller branch has fewer nodes and graphify refuses to overwrite a bigger graph without FORCE).
- graphify-out/ is NOT committed (it differs per branch). Do not commit it.
