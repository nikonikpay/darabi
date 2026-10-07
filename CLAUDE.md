@AGENTS.md

# CLAUDE.md

## Git workflow (required)

- Working branch: `slice-13/web-v3` (tracks `origin/slice-13/web-v3`), unless the owner names another branch.
- After **every** completed change, commit and push right away — do not batch changes or wait to be asked:
  1. `git add` the changed files
  2. `git commit` with a clear message in the repo's existing style (Conventional Commits, e.g. `feat(web): …`, `fix(hardware): …`, `docs: …`)
  3. `git push` to the current branch's upstream
- Commit identity: `saeed-darabi <saeed.r.darabi@gmail.com>` (set in the local repo config).
- If Git is not on PATH in the shell, use `C:\Program Files\Git\cmd\git.exe`.
- Never force-push, rewrite published history, or push to `main` without the owner's explicit say-so.

## Finding files and code (graphify)

- Before searching the tree with Glob/Grep to locate a file, class, or the code behind a feature, query the knowledge graph first: `graphify query "<question>"`, `graphify explain "<name>"` or `graphify path "<A>" "<B>"` (graph in `graphify-out/graph.json`). Use the `source_location` it returns to open the file directly; fall back to Grep only if the graph has no answer.
- After a large change (new files, renames, deletions), refresh it with `/graphify . --update` so it does not go stale.
