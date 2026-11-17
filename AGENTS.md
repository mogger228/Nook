# Nook development

- Keep the app on C# 5 / WPF / .NET Framework 4.x; avoid additional runtime dependencies.
- Preserve user settings, pinned apps, and the detached launch mechanism. Use a candidate exe, then graceful exit and backup before replacing the live binary.
- The project has a local `codebase-memory` MCP server, graph name `islanddock`. Use its focused symbol/relationship queries to avoid repeatedly reading entire files; confirm current source before editing. Graph results are navigation evidence, not proof of complete coverage.
- Automatic watching is disabled. Before relying on a graph after code changes, refresh it with `index_repository` (repo root, name `islanddock`, mode `fast`).
- If MCP tools are unavailable in the current session, use `scripts/CodeMemory.ps1` as a CLI fallback. For example: `./scripts/CodeMemory.ps1 search_graph --project islanddock --name-pattern RunScenario --limit 5`.
- Index caches and downloaded development tools are ignored by Git. They are not part of the Nook runtime.
