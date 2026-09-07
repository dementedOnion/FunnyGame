using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
using FunnyGame.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(args.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ?? "http://0.0.0.0:5077");

var app = builder.Build();
app.UseWebSockets();

var server = new GameServer();
server.Start();

app.MapGet("/", () => Results.Text("FunnyGame server is running. WebSocket endpoint: /ws", "text/plain"));
app.MapGet("/games", () => server.ListGames());
app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await server.HandleClientAsync(socket, context.RequestAborted);
});

await app.RunAsync();

sealed class GameServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, ClientPeer> _clients = new();
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();
    private readonly ConcurrentDictionary<string, string> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();

    public GameServer()
    {
        _rooms["lobby"] = new GameRoom("lobby", "Public Lobby", 16);
    }

    public void Start()
    {
        _ = Task.Run(TickLoopAsync);
    }

    public IReadOnlyList<GameInfo> ListGames() => _rooms.Values.Select(r => r.Info).OrderBy(g => g.Name).ToArray();

    public async Task HandleClientAsync(WebSocket socket, CancellationToken requestAborted)
    {
        var peer = new ClientPeer(socket);
        _clients[peer.PlayerId] = peer;

        try
        {
            await peer.SendAsync(new GameListMessage(ListGames()), requestAborted);

            await foreach (var message in ReadMessagesAsync(socket, requestAborted))
            {
                switch (message)
                {
                    case LoginRequest login:
                        HandleLogin(peer, login);
                        await peer.SendAsync(new ServerWelcome(peer.PlayerId, peer.SessionToken, peer.Name), requestAborted);
                        await peer.SendAsync(new GameListMessage(ListGames()), requestAborted);
                        break;
                    case HostGameRequest host:
                        var id = Slug(host.GameName);
                        _rooms.TryAdd(id, new GameRoom(id, host.GameName, Math.Clamp(host.MaxPlayers, 1, 32)));
                        JoinRoom(peer, id);
                        await BroadcastGameListsAsync(requestAborted);
                        break;
                    case JoinGameRequest join:
                        JoinRoom(peer, join.GameId);
                        await BroadcastGameListsAsync(requestAborted);
                        break;
                    case PlayerInputMessage input:
                        peer.LastInput = input;
                        break;
                    case InteractMessage interact:
                        peer.Room?.Interact(peer, interact.EntityId);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _clients.TryRemove(peer.PlayerId, out _);
            peer.Room?.Leave(peer);
            await BroadcastGameListsAsync(CancellationToken.None);
        }
    }

    private void HandleLogin(ClientPeer peer, LoginRequest login)
    {
        var name = string.IsNullOrWhiteSpace(login.Name) ? $"Player{Random.Shared.Next(1000, 9999)}" : login.Name.Trim()[..Math.Min(20, login.Name.Trim().Length)];
        var password = string.IsNullOrWhiteSpace(login.Password) ? "guest" : login.Password;
        var stored = _accounts.GetOrAdd(name, password);
        if (stored != password)
        {
            throw new InvalidOperationException("Wrong password.");
        }

        peer.Name = name;
        peer.SessionToken = Convert.ToHexString(Guid.NewGuid().ToByteArray());
    }

    private void JoinRoom(ClientPeer peer, string gameId)
    {
        if (!_rooms.TryGetValue(gameId, out var room))
        {
            _ = peer.SendAsync(new ServerNotice($"No game named '{gameId}' exists."), CancellationToken.None);
            return;
        }

        if (room.PlayerCount >= room.MaxPlayers)
        {
            _ = peer.SendAsync(new ServerNotice("That game is full."), CancellationToken.None);
            return;
        }

        peer.Room?.Leave(peer);
        room.Join(peer);
        peer.Room = room;
    }

    private async Task BroadcastGameListsAsync(CancellationToken cancellationToken)
    {
        var message = new GameListMessage(ListGames());
        await Task.WhenAll(_clients.Values.Select(c => c.SendAsync(message, cancellationToken)));
    }

    private async Task TickLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / Protocol.TickRate));
        while (await timer.WaitForNextTickAsync(_shutdown.Token))
        {
            foreach (var room in _rooms.Values)
            {
                var snapshot = room.Tick();
                await Task.WhenAll(room.Players.Select(p => p.SendAsync(snapshot, _shutdown.Token)));
            }
        }
    }

    private static async IAsyncEnumerable<NetMessage> ReadMessagesAsync(WebSocket socket, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    yield break;
                }

                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var json = Encoding.UTF8.GetString(ms.ToArray());
            var message = JsonSerializer.Deserialize<NetMessage>(json, JsonOptions);
            if (message is not null)
            {
                yield return message;
            }
        }
    }

    private static string Slug(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? $"game-{Random.Shared.Next(1000, 9999)}" : value.Trim().ToLowerInvariant();
        var chars = text.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }
}

sealed class ClientPeer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public ClientPeer(WebSocket socket)
    {
        Socket = socket;
    }

    public WebSocket Socket { get; }
    public Guid PlayerId { get; } = Guid.NewGuid();
    public string Name { get; set; } = "Guest";
    public string SessionToken { get; set; } = "";
    public PlayerInputMessage LastInput { get; set; } = new(0, 0, 0, false, 0);
    public GameRoom? Room { get; set; }
    public PlayerState State { get; set; } = new(Guid.Empty, "Guest", new(0, 1.8f, 0), new(), 0);

    public async Task SendAsync(NetMessage message, CancellationToken cancellationToken)
    {
        if (Socket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        catch
        {
        }
        finally
        {
            _sendLock.Release();
        }
    }
}

sealed class GameRoom
{
    private readonly Dictionary<Guid, ClientPeer> _players = new();
    private readonly List<EntityState> _entities =
    [
        new(Guid.NewGuid(), "Light", new(0, 4, -2), false, 0),
        new(Guid.NewGuid(), "Button", new(2, 0.5f, 2), true, 0),
        new(Guid.NewGuid(), "Crate", new(-2, 0.5f, 3), true, 0),
        new(Guid.NewGuid(), "SpawnMarker", new(0, 0.05f, 0), false, 0)
    ];

    private long _tick;

    public GameRoom(string id, string name, int maxPlayers)
    {
        Id = id;
        Name = name;
        MaxPlayers = maxPlayers;
    }

    public string Id { get; }
    public string Name { get; }
    public int MaxPlayers { get; }
    public int PlayerCount { get { lock (_players) return _players.Count; } }
    public GameInfo Info => new(Id, Name, PlayerCount, MaxPlayers);
    public IReadOnlyList<ClientPeer> Players { get { lock (_players) return _players.Values.ToArray(); } }

    public void Join(ClientPeer peer)
    {
        lock (_players)
        {
            var offset = _players.Count * 1.25f;
            peer.State = new PlayerState(peer.PlayerId, peer.Name, new(offset, Protocol.PlayerHeight, 0), new(), 0);
            _players[peer.PlayerId] = peer;
        }
    }

    public void Leave(ClientPeer peer)
    {
        lock (_players)
        {
            _players.Remove(peer.PlayerId);
        }
    }

    public void Interact(ClientPeer peer, Guid entityId)
    {
        lock (_players)
        {
            var index = _entities.FindIndex(e => e.EntityId == entityId && e.IsInteractable);
            if (index < 0)
            {
                return;
            }

            var entity = _entities[index];
            if (Vector3.Distance(peer.State.Position, entity.Position) <= 2.25f)
            {
                _entities[index] = entity with { UseCount = entity.UseCount + 1 };
            }
        }
    }

    public WorldSnapshot Tick()
    {
        lock (_players)
        {
            const float dt = 1f / Protocol.TickRate;
            foreach (var peer in _players.Values)
            {
                var input = peer.LastInput;
                var yaw = input.Yaw;
                var forward = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
                var right = new Vector3(forward.Z, 0, -forward.X);
                var wish = right * input.MoveX + forward * input.MoveZ;
                if (wish.LengthSquared() > 1)
                {
                    wish = Vector3.Normalize(wish);
                }

                var velocity = peer.State.Velocity;
                velocity.X = wish.X * 4.8f;
                velocity.Z = wish.Z * 4.8f;
                if (peer.State.Position.Y <= Protocol.PlayerHeight + 0.01f && input.Jump)
                {
                    velocity.Y = 6.5f;
                }

                velocity.Y -= 18f * dt;
                var position = peer.State.Position + velocity * dt;
                if (position.Y < Protocol.PlayerHeight)
                {
                    position.Y = Protocol.PlayerHeight;
                    velocity.Y = 0;
                }

                peer.State = new PlayerState(peer.PlayerId, peer.Name, position, velocity, yaw);
            }

            return new WorldSnapshot(Id, ++_tick, _players.Count + _entities.Count, _players.Values.Select(p => p.State).ToArray(), _entities.ToArray());
        }
    }
}
