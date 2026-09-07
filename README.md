# FunnyGame

C# prototype for a DirectX 12 first-person multiplayer walkaround.

## Run

Start the self-hosted server:

```powershell
dotnet run --project .\src\FunnyGame.Server -- http://0.0.0.0:5077
```

Start the DirectX 12 client:

```powershell
dotnet run --project .\src\FunnyGame.Client
```

Friends can connect to `ws://YOUR_PUBLIC_OR_LAN_IP:5077/ws`. Forward TCP port `5077` on your router for internet play, or use a tunnel such as Tailscale, ZeroTier, or ngrok during testing.

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
