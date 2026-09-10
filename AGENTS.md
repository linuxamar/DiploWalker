# AGENTS.md

## Stack

- .NET 10 (F# orientation)
- Git LFS required: run `git lfs install` after clone
- Validation: `./pipeline.ps1 -DoTests` (build + tests, ~3 min; see CLAUDE.md)

## Git conventions

- Branches: `dev` (development) → `main` (primary)
- Never add `Co-Authored-By` or "Generated with Claude Code" trailers
- Commit messages and all documentation in French (with proper accents)

## Project language

- French for: README, comments, commit messages, documentation

## Extended context

- See `CLAUDE.md` for full repository conventions and background
- `CONTEXT.md` holds session state — read it at session start
