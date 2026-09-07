# Working On

This project is shared by two Codex workers and humans. Before editing, claim the files you expect to touch here. Release the claim when the change is committed, abandoned, or handed off.

## Rules

1. Pull or refresh the repo before claiming work.
2. Add yourself to the active claims table before editing files.
3. Do not edit files claimed by someone else unless they explicitly hand them off.
4. If a task needs a claimed file, add a note in the coordination log and wait for handoff.
5. Keep claims narrow: claim exact files or folders, not the whole repo unless unavoidable.
6. Update status when you pause, finish, or need review.
7. Commit related changes together, then remove or close the claim in the same commit when possible.

## Status Values

- `claimed`: about to edit
- `editing`: actively changing
- `review`: ready for another person to inspect
- `blocked`: waiting on a decision or dependency
- `done`: finished and safe to release

## Active Claims

| Owner | Task | Files / Folders | Status | Started | Notes |
| --- | --- | --- | --- | --- | --- |
| Codex A | DX12 FPS room and central multiplayer relay | `README.md`, `Dockerfile.server`, `render.yaml`, `src/FunnyGame.Shared/`, `src/FunnyGame.Server/`, `src/FunnyGame.Client/` | done | 2026-09-07 | Built and verified; files are safe for the next worker to claim. |

## Coordination Log

| Time | Owner | Note |
| --- | --- | --- |
| 2026-09-07 | Codex A | Created coordination file so parallel workers can claim files before editing. |
| 2026-09-07 | Codex A | Completed the playable DX12 room, FPS controls, four-player synchronization, room invites, and relay deployment files. |

## Handoff Template

```text
Owner:
Task:
Files / folders:
Current status:
What changed:
What still needs doing:
Known risks:
Suggested next command:
```
