# FunnyGame

C# DirectX 12 first-person multiplayer walkaround with a central online room service.

## Run

Run the room service locally for development:

```powershell
dotnet run --project .\src\FunnyGame.Server -- http://0.0.0.0:5077
```

Start the DirectX 12 client:

```powershell
dotnet run --project .\src\FunnyGame.Client
```

For internet play, deploy `Dockerfile.server` as one permanent web service. The included `render.yaml` creates a TLS-enabled WebSocket endpoint on Render. Set `Server` to `wss://YOUR-SERVICE.onrender.com/ws`; `Host Game` creates a visible four-player room there and copies a single-line invite containing the endpoint and room code. Friends paste the invite into `Server / Invite` and click `Join Game`.

The free Render plan sleeps only while idle; incoming WebSocket messages keep an active game awake. Choose an always-on paid instance before treating it as production infrastructure.

## Controls

- `WASD`: walk
- Mouse: look
- `Space`: jump
- `E`: interact with nearby interactable entities

## Current Features

- DirectX 12 window through `Vortice.Windows`
- First-person movement and jumping
- Flat walk surface in server simulation
- Room hosting and joining
- Central outbound-only room service (no player port forwarding)
- Four-player room limit
- Room-code invites and browser listings
- Smoothed remote player transforms
- Simple friend login with name plus password
- Shared objects: light, button, crate, spawn marker
- Entity counting in snapshots
- WebSocket multiplayer sync for players and entity interaction counts

## Things You Are Probably Forgetting

- NAT traversal or port forwarding for friends outside your network
- TLS and real auth before public hosting
- Server moderation: kick, ban, rate limits, name filtering
- Persisted accounts instead of in-memory passwords
- Client prediction, interpolation, and lag compensation
- Anti-cheat boundaries and server validation
- Asset pipeline, maps, collision meshes, and spawn points
- Audio, settings, key rebinding, fullscreen, and sensitivity
- Logging, crash dumps, metrics, and graceful shutdown
- Build packaging and version compatibility checks
