using System.Numerics;
using System.Text.Json.Serialization;

namespace FunnyGame.Shared;

public static class Protocol
{
    public const int TickRate = 30;
    public const int MaxPlayers = 4;
    public const float PlayerRadius = 0.35f;
    public const float PlayerHeight = 1.8f;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LoginRequest), "login")]
[JsonDerivedType(typeof(HostGameRequest), "host")]
[JsonDerivedType(typeof(JoinGameRequest), "join")]
[JsonDerivedType(typeof(PlayerInputMessage), "input")]
[JsonDerivedType(typeof(InteractMessage), "interact")]
[JsonDerivedType(typeof(ServerWelcome), "welcome")]
[JsonDerivedType(typeof(GameListMessage), "games")]
[JsonDerivedType(typeof(WorldSnapshot), "snapshot")]
[JsonDerivedType(typeof(ServerNotice), "notice")]
public abstract record NetMessage;

public sealed record LoginRequest(string Name, string Password) : NetMessage;
public sealed record HostGameRequest(string GameName, int MaxPlayers) : NetMessage;
public sealed record JoinGameRequest(string GameId) : NetMessage;
public sealed record PlayerInputMessage(float MoveX, float MoveZ, float Yaw, bool Jump, double ClientTime) : NetMessage;
public sealed record InteractMessage(Guid EntityId) : NetMessage;

public sealed record ServerWelcome(Guid PlayerId, string SessionToken, string Name) : NetMessage;
public sealed record GameListMessage(IReadOnlyList<GameInfo> Games) : NetMessage;
public sealed record ServerNotice(string Text) : NetMessage;

public sealed record GameInfo(string GameId, string Name, int PlayerCount, int MaxPlayers);

public sealed record WorldSnapshot(
    string GameId,
    long Tick,
    int EntityCount,
    IReadOnlyList<PlayerState> Players,
    IReadOnlyList<EntityState> Entities) : NetMessage;

public sealed record PlayerState(Guid PlayerId, string Name, Vector3 Position, Vector3 Velocity, float Yaw);

public sealed record EntityState(Guid EntityId, string Kind, Vector3 Position, bool IsInteractable, int UseCount);
