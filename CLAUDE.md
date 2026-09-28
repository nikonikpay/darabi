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
