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
| Codex A | Windows EXE packaging, session menu, and camera fix | `README.md`, `artifacts/`, `src/FunnyGame.Client/`, `src/FunnyGame.Server/Program.cs` | done | 2026-09-07 | Final executable and multiplayer position fix verified; files are safe to claim. |

## Coordination Log

| Time | Owner | Note |
| --- | --- | --- |
| 2026-09-07 | Codex A | Created coordination file so parallel workers can claim files before editing. |
| 2026-09-07 | Codex A | Completed the playable DX12 room, FPS controls, four-player synchronization, room invites, and relay deployment files. |
| 2026-09-07 | Codex A | Claimed the client menu and Windows executable packaging work. |
| 2026-09-07 | Codex A | Completed and verified the start menu, spawn/collision correction, and self-contained FunnyGame.exe package. |
| 2026-09-07 | Codex A | Reopened the claim after play-testing exposed an incorrect camera matrix layout. |
| 2026-09-07 | Codex A | Fixed Vector3 network serialization, preventing all players and entities from collapsing to the origin. |

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
