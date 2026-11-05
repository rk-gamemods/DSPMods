# DSP mod rules

These rules cover every Dyson Sphere Program mod worked on from this folder. First read the AGENTS.md of the repo you're in. The owner's AGENTS.md files override this file; upstream authors' don't.

## Documentation
- Docs go stale, so keep as few as is safe. A doc must hold something the code can't show and have a strong reason to exist.
- Allowed: one short README per repo, CLAUDE.md, AGENTS.md, licence and notice files, each private fork's UPSTREAM.md and CHANGES-PRIVATE.md, and DSP-Unified's design/ARCH.md. Nothing else.
- Before creating any .md or .txt file, check that list. If it isn't allowed, say it in chat or a commit message.
- The final report is your closing chat message. Results go there or in commit messages. Write a report file only when asked, outside the repo or in .local/reports/.
- Code explains itself: clear names, small functions. No comments, except a minimal one on code that must be dense to get a performance gain. Delete other comments in code you change.
- When code changes, fix or delete any doc or comment it makes wrong in the same commit.
- Keep scratch files in .local/scratch/. Delete them, and your plans, specs and examples, as soon as they're no longer needed.
- Existing docs get no free pass. In any repo you work in, delete docs that fail these rules and say what you deleted.
- Only delete files git tracks in that repo, never licence or notice files. Untracked files can't be recovered, so delete those only in .local/scratch/.
- Open Brain notes are no reason to write more docs.

## Robustness
- Mods update almost daily and the mod list changes. Code must survive that without being turned off.
- Prefer generic patterns. Name another mod only as a soft dependency, and keep working if it's missing or changed.
- Public APIs are contracts: additive changes only unless the owner approves. They must stay safe to call after their component disables itself.
- At load, each component checks that its target exists, matches the expected shape or hash, and isn't broken by another mod's live Harmony patch.
- If a check fails, disable only that component, fall back to vanilla and log one line: `[Component] disabled: <target> changed (expected X, found Y)`.
- Isolate each component's setup and run steps, so an exception in one never disables another.
- Every optimization has its own config switch.

## Architecture
- Priority: crash safety, then exact results, then speed.
- Fix bugs in the mod they belong to, not with workarounds elsewhere.
- Rewrite or replace upstream code freely when we can do better. Keep its licence headers and attribution notices.
- Optimization is CPU and simulation only: no graphics or GPU changes, nothing that could affect visual fidelity. SphereOpt's shell shaders are the exception.

## Shared optimization layer
- Never patch or target a specific mod's assembly, type, method or version. Mods opt in to faster paths by calling this layer's public API.
- Never call, reference or detect the mods above it, apart from the live Harmony patch check.

## Testing
- The owner says when the PC is free for game testing. Don't launch the game until then, and stop at once if the owner says stop or starts using the PC.
- Follow .local/game-test/README.md in the main DSPMods checkout. Worktrees don't have it.
- Use only TEST-DO-NOT-USE-* profiles. Never write to real saves or the Main profile.
- Prove exact results before timing: run the optimization on and off from the same save and compare the game state.
- Timing runs: low graphics, shadows off, vsync off, small window, a TEST-DO-NOT-USE- copy of save3 starting at planet 502.
- Measure CPU time (tick time plus main-thread frame time minus GPU wait), not FPS or UPS alone.
- One agent owns the test rig at a time.
- A package goes to a game window only with a passing offline preflight receipt for its exact bytes: every module's hooks installed with real HarmonyX, and an all-Off cold start under byte-loaded Mono.
- Only the rig owner may suspend other processes, and only while the game is running.

## Git
- Work on feature branches. Commit and push often, but only to rk-gamemods repos, never to main or an upstream remote. Merging is the owner's call.
- Build locally before pushing, because a failed CI run notifies the owner's phone.
- Push nothing while game testing is running.
