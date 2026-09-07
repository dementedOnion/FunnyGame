using System.Diagnostics;
using System.Net.WebSockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FunnyGame.Shared;
using DrawingColor = System.Drawing.Color;

namespace FunnyGame.Client;

public sealed class GameForm : Form
{
    private readonly HashSet<Keys> _keys = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly NetworkClient _network = new();
    private readonly Button _hostButton = new() { Text = "Host Game", Width = 92, Height = 28 };
    private readonly Button _joinButton = new() { Text = "Join Game", Width = 92, Height = 28 };
    private readonly Button _inviteButton = new() { Text = "Copy Invite", Width = 92, Height = 28, Enabled = false };
    private readonly Button _resumeButton = new() { Text = "Resume", Width = 190, Height = 34, Visible = false };
    private readonly Button _exitButton = new() { Text = "Exit", Width = 190, Height = 34 };
    private readonly TextBox _serverBox = new()
    {
        Text = Environment.GetEnvironmentVariable("FUNNYGAME_SERVER_URL") ?? "ws://127.0.0.1:5077/ws",
        Width = 190
    };
    private readonly TextBox _nameBox = new() { Text = "Player", Width = 80 };
    private readonly TextBox _passwordBox = new() { Text = "friends", Width = 80, UseSystemPasswordChar = true };
    private readonly ComboBox _gameBox = new() { Text = "my-game", Width = 130, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Panel _viewport = new() { Dock = DockStyle.Fill, BackColor = DrawingColor.Black, TabStop = true };
    private readonly Panel _menuPanel = new() { Dock = DockStyle.Fill, BackColor = DrawingColor.FromArgb(17, 21, 26) };
    private Dx12Renderer? _renderer;
    private float _yaw;
    private float _pitch;
    private Vector3 _localPosition = new(0, Protocol.PlayerHeight, -4);
    private float _verticalVelocity;
    private bool _mouseCaptured;
    private bool _interactWasDown;
    private string _invite = "";
    private string _gameListSignature = "";
    private int _frameRunning;

    public GameForm()
    {
        Text = "FunnyGame - DirectX 12 Multiplayer FPS";
        Width = 1280;
        Height = 720;
        DoubleBuffered = false;
        KeyPreview = true;

        Controls.Add(_viewport);
        Controls.Add(_menuPanel);
        BuildMenu();
        _menuPanel.BringToFront();

        _hostButton.Click += async (_, _) => await HostAsync();
        _joinButton.Click += async (_, _) => await JoinAsync();
        _inviteButton.Click += (_, _) => Clipboard.SetText(_invite);
        _resumeButton.Click += (_, _) => EnterGame();
        _exitButton.Click += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                OpenMenu();
                return;
            }

            _keys.Add(e.KeyCode);
        };
        KeyUp += (_, e) => _keys.Remove(e.KeyCode);
        _viewport.MouseDown += (_, _) => CaptureMouse();
        _viewport.MouseMove += OnMouseMove;
        _viewport.Resize += (_, _) => _renderer?.Resize(Math.Max(1, _viewport.ClientSize.Width), Math.Max(1, _viewport.ClientSize.Height));
        Deactivate += (_, _) => ReleaseMouse();
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _renderer?.Dispose();
            _network.Dispose();
        };

        _timer.Interval = 16;
        _timer.Tick += async (_, _) => await FrameAsync();
    }

    private void BuildMenu()
    {
        var center = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
        };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 440));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var menu = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(24),
        };
        menu.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        menu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "FunnyGame",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 28, FontStyle.Bold),
            ForeColor = DrawingColor.White,
            Margin = new Padding(0, 0, 0, 22),
        };
        menu.Controls.Add(title, 0, 0);
        menu.SetColumnSpan(title, 2);

        AddMenuField(menu, "Server / Invite", _serverBox, 1);
        AddMenuField(menu, "Name", _nameBox, 2);
        AddMenuField(menu, "Password", _passwordBox, 3);
        AddMenuField(menu, "Game", _gameBox, 4);

        _serverBox.Dock = DockStyle.Fill;
        _nameBox.Dock = DockStyle.Fill;
        _passwordBox.Dock = DockStyle.Fill;
        _gameBox.Dock = DockStyle.Fill;

        var sessionButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 14, 0, 4),
        };
        sessionButtons.Controls.AddRange([_hostButton, _joinButton, _inviteButton]);
        menu.Controls.Add(sessionButtons, 0, 5);
        menu.SetColumnSpan(sessionButtons, 2);

        var appButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 10, 0, 0),
        };
        appButtons.Controls.AddRange([_resumeButton, _exitButton]);
        menu.Controls.Add(appButtons, 0, 6);
        menu.SetColumnSpan(appButtons, 2);

        center.Controls.Add(menu, 1, 1);
        _menuPanel.Controls.Add(center);
    }

    private static void AddMenuField(TableLayoutPanel menu, string text, Control control, int row)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = DrawingColor.FromArgb(205, 211, 218),
            Margin = new Padding(0, 8, 10, 8),
        };
        control.Margin = new Padding(0, 6, 0, 6);
        menu.Controls.Add(label, 0, row);
        menu.Controls.Add(control, 1, row);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _renderer = new Dx12Renderer(_viewport.Handle, Math.Max(1, _viewport.ClientSize.Width), Math.Max(1, _viewport.ClientSize.Height));
        _timer.Start();
    }

    private async Task HostAsync()
    {
        SetConnectionButtons(false);
        try
        {
            var (server, _) = ParseInvite(_serverBox.Text);
            _serverBox.Text = server.ToString();
            await EnsureConnectedAsync(server);

            var gameName = string.IsNullOrWhiteSpace(_gameBox.Text) ? "my-game" : _gameBox.Text.Trim();
            await _network.SendAsync(new HostGameRequest(gameName, Protocol.MaxPlayers));
            var gameId = Slug(gameName);
            _gameBox.Text = gameId;
            _invite = $"{server}|{gameId}";
            _inviteButton.Enabled = true;
            Clipboard.SetText(_invite);
            MessageBox.Show(
                $"Your four-player room is online. Send this invite to your friend (it is already copied):\n\n{_invite}",
                "Game online",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            EnterGame(resetView: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not host game", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _hostButton.Text = "Host Game";
            SetConnectionButtons(true);
        }
    }

    private async Task JoinAsync()
    {
        SetConnectionButtons(false);
        try
        {
            var (server, invitedGame) = ParseInvite(_serverBox.Text);
            _serverBox.Text = server.ToString();
            if (!string.IsNullOrWhiteSpace(invitedGame))
            {
                _gameBox.Text = invitedGame;
            }

            await EnsureConnectedAsync(server);
            var gameId = _gameBox.SelectedItem is GameListItem selected
                ? selected.Info.GameId
                : _gameBox.Text.Trim();
            await _network.SendAsync(new JoinGameRequest(string.IsNullOrWhiteSpace(gameId) ? "lobby" : gameId));
            EnterGame(resetView: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not join game", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetConnectionButtons(true);
        }
    }

    private async Task EnsureConnectedAsync(Uri server)
    {
        if (!_network.IsConnected)
        {
            await _network.ConnectAsync(server, _nameBox.Text, _passwordBox.Text);
        }
    }

    private void SetConnectionButtons(bool enabled)
    {
        _hostButton.Enabled = enabled;
        _joinButton.Enabled = enabled;
    }

    private async Task FrameAsync()
    {
        if (Interlocked.Exchange(ref _frameRunning, 1) != 0)
        {
            return;
        }

        try
        {
            var dt = (float)_clock.Elapsed.TotalSeconds;
            _clock.Restart();
            RefreshGameList();

            var menuOpen = _menuPanel.Visible;
            var moveX = menuOpen ? 0 : (_keys.Contains(Keys.D) ? 1 : 0) - (_keys.Contains(Keys.A) ? 1 : 0);
            var moveZ = menuOpen ? 0 : (_keys.Contains(Keys.W) ? 1 : 0) - (_keys.Contains(Keys.S) ? 1 : 0);
            var jump = !menuOpen && _keys.Contains(Keys.Space);

            if (_network.IsConnected)
            {
                await _network.SendAsync(new PlayerInputMessage(moveX, moveZ, _yaw, jump, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0));
            }
            else
            {
                LocalMove(moveX, moveZ, jump, dt);
            }

            var interactDown = !menuOpen && _keys.Contains(Keys.E);
            if (interactDown && !_interactWasDown)
            {
                var target = (_network.CurrentSnapshot?.Entities ?? Dx12Renderer.LocalEntities)
                    .Where(e => e.IsInteractable)
                    .OrderBy(e => Vector3.Distance(e.Position, _localPosition))
                    .FirstOrDefault();
                if (target is not null && Vector3.Distance(target.Position, _localPosition) <= 2.25f)
                {
                    await _network.SendAsync(new InteractMessage(target.EntityId));
                }
            }
            _interactWasDown = interactDown;

            var snapshot = _network.CurrentSnapshot;
            var local = snapshot?.Players.FirstOrDefault(p => p.PlayerId == _network.PlayerId);
            if (local is not null)
            {
                _localPosition = local.Position;
            }

            _renderer?.Render(_localPosition, _yaw, _pitch, _network.GetRenderSnapshot(), _network.PlayerId);
            Text = "FunnyGame";
        }
        finally
        {
            Volatile.Write(ref _frameRunning, 0);
        }
    }

    private void LocalMove(int moveX, int moveZ, bool jump, float dt)
    {
        var forward = new Vector3(MathF.Sin(_yaw), 0, MathF.Cos(_yaw));
        var right = new Vector3(-forward.Z, 0, forward.X);
        var wish = right * moveX + forward * moveZ;
        if (wish.LengthSquared() > 1)
        {
            wish = Vector3.Normalize(wish);
        }

        _localPosition += wish * 4.8f * dt;
        _localPosition = ConstrainToRoom(_localPosition, Dx12Renderer.LocalEntities);
        if (_localPosition.Y <= Protocol.PlayerHeight + 0.01f && jump)
        {
            _verticalVelocity = 5.5f;
        }

        _verticalVelocity -= 14f * dt;
        _localPosition.Y += _verticalVelocity * dt;
        if (_localPosition.Y < Protocol.PlayerHeight)
        {
            _localPosition.Y = Protocol.PlayerHeight;
            _verticalVelocity = 0;
        }
    }

    private static Vector3 ConstrainToRoom(Vector3 position, IReadOnlyList<EntityState> entities)
    {
        position.X = Math.Clamp(position.X, -9.3f, 9.3f);
        position.Z = MathF.Min(position.Z, 9.3f);

        foreach (var entity in entities)
        {
            var obstacleRadius = entity.Kind switch
            {
                "Light" => 0.35f,
                "Button" => 0.65f,
                "Crate" => 0.72f,
                _ => 0f,
            };
            if (obstacleRadius == 0)
            {
                continue;
            }

            var delta = new Vector2(position.X - entity.Position.X, position.Z - entity.Position.Z);
            var minimumDistance = Protocol.PlayerRadius + obstacleRadius;
            if (delta.LengthSquared() >= minimumDistance * minimumDistance)
            {
                continue;
            }

            var direction = delta.LengthSquared() < 0.0001f ? new Vector2(0, -1) : Vector2.Normalize(delta);
            position.X = entity.Position.X + direction.X * minimumDistance;
            position.Z = entity.Position.Z + direction.Y * minimumDistance;
        }

        return position;
    }

    private void RefreshGameList()
    {
        var signature = string.Join('|', _network.Games.Select(game => $"{game.GameId}:{game.PlayerCount}:{game.MaxPlayers}"));
        if (signature == _gameListSignature)
        {
            return;
        }

        _gameListSignature = signature;
        var selected = _gameBox.Text;
        _gameBox.Items.Clear();
        foreach (var game in _network.Games)
        {
            _gameBox.Items.Add(new GameListItem(game));
        }

        _gameBox.Text = string.IsNullOrWhiteSpace(selected) ? "lobby" : selected;
    }

    private void CaptureMouse()
    {
        if (_mouseCaptured)
        {
            return;
        }

        Cursor.Clip = _viewport.RectangleToScreen(_viewport.ClientRectangle);
        CenterCursor();
        _mouseCaptured = true;
        for (var i = 0; i < 16 && NativeMethods.ShowCursor(false) >= 0; i++)
        {
        }
        _viewport.Focus();
    }

    private void EnterGame(bool resetView = false)
    {
        if (resetView)
        {
            _yaw = 0;
            _pitch = 0;
        }

        _menuPanel.Hide();
        _resumeButton.Visible = true;
        CaptureMouse();
    }

    private void OpenMenu()
    {
        ReleaseMouse();
        _keys.Clear();
        _menuPanel.Show();
        _menuPanel.BringToFront();
    }

    private void ReleaseMouse()
    {
        if (!_mouseCaptured)
        {
            return;
        }

        _mouseCaptured = false;
        Cursor.Clip = Rectangle.Empty;
        for (var i = 0; i < 16 && NativeMethods.ShowCursor(true) < 0; i++)
        {
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_mouseCaptured)
        {
            return;
        }

        var center = _viewport.PointToScreen(new Point(_viewport.ClientSize.Width / 2, _viewport.ClientSize.Height / 2));
        _yaw -= (Cursor.Position.X - center.X) * 0.0025f;
        _pitch = Math.Clamp(_pitch + (Cursor.Position.Y - center.Y) * 0.0025f, -1.2f, 1.2f);
        CenterCursor();
    }

    private void CenterCursor()
    {
        Cursor.Position = _viewport.PointToScreen(new Point(_viewport.ClientSize.Width / 2, _viewport.ClientSize.Height / 2));
    }

    private static (Uri Server, string? GameId) ParseInvite(string text)
    {
        var pieces = text.Trim().Split('|', 2, StringSplitOptions.TrimEntries);
        return (new Uri(pieces[0]), pieces.Length == 2 ? pieces[1] : null);
    }

    private static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private sealed record GameListItem(GameInfo Info)
    {
        public override string ToString() => $"{Info.Name} ({Info.PlayerCount}/{Info.MaxPlayers})";
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);
    }
}

sealed class NetworkClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true,
    };
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _snapshotLock = new();
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _connectionCancellation;
    private WorldSnapshot? _previousSnapshot;
    private WorldSnapshot? _currentSnapshot;
    private long _snapshotReceivedAt;
    private bool _disposed;

    public Guid PlayerId { get; private set; }
    public bool IsConnected => Volatile.Read(ref _socket)?.State == WebSocketState.Open;
    public IReadOnlyList<GameInfo> Games { get; private set; } = [];
    public WorldSnapshot? CurrentSnapshot
    {
        get
        {
            lock (_snapshotLock)
            {
                return _currentSnapshot;
            }
        }
    }

    public async Task ConnectAsync(Uri uri, string name, string password)
    {
        await _connectLock.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsConnected)
            {
                return;
            }

            DisposeConnection();
            var socket = new ClientWebSocket();
            var cancellation = new CancellationTokenSource();
            try
            {
                await socket.ConnectAsync(uri, cancellation.Token);
            }
            catch
            {
                cancellation.Dispose();
                socket.Dispose();
                throw;
            }

            _socket = socket;
            _connectionCancellation = cancellation;
            PlayerId = Guid.Empty;
            Games = [];
            lock (_snapshotLock)
            {
                _previousSnapshot = null;
                _currentSnapshot = null;
            }

            _ = Task.Run(() => ReceiveLoopAsync(socket, cancellation.Token));
            await SendAsync(new LoginRequest(name, password));
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task SendAsync(NetMessage message)
    {
        var socket = Volatile.Read(ref _socket);
        if (socket?.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync();
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public WorldSnapshot? GetRenderSnapshot()
    {
        lock (_snapshotLock)
        {
            if (_currentSnapshot is null || _previousSnapshot is null)
            {
                return _currentSnapshot;
            }

            var elapsed = (Stopwatch.GetTimestamp() - _snapshotReceivedAt) / (double)Stopwatch.Frequency;
            var amount = Math.Clamp((float)(elapsed * Protocol.TickRate), 0, 1);
            var previousPlayers = _previousSnapshot.Players.ToDictionary(player => player.PlayerId);
            var players = _currentSnapshot.Players.Select(current =>
            {
                if (!previousPlayers.TryGetValue(current.PlayerId, out var previous))
                {
                    return current;
                }

                return current with
                {
                    Position = Vector3.Lerp(previous.Position, current.Position, amount),
                    Velocity = Vector3.Lerp(previous.Velocity, current.Velocity, amount),
                    Yaw = LerpAngle(previous.Yaw, current.Yaw, amount)
                };
            }).ToArray();

            return _currentSnapshot with { Players = players };
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                switch (JsonSerializer.Deserialize<NetMessage>(json, JsonOptions))
                {
                    case ServerWelcome welcome:
                        PlayerId = welcome.PlayerId;
                        break;
                    case GameListMessage list:
                        Games = list.Games;
                        break;
                    case WorldSnapshot snapshot:
                        lock (_snapshotLock)
                        {
                            _previousSnapshot = _currentSnapshot;
                            _currentSnapshot = snapshot;
                            _snapshotReceivedAt = Stopwatch.GetTimestamp();
                        }
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _socket, null, socket), socket))
            {
                socket.Dispose();
            }
        }
    }

    private static float LerpAngle(float from, float to, float amount)
    {
        var delta = MathF.IEEERemainder(to - from, MathF.Tau);
        return from + delta * amount;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeConnection();
    }

    private void DisposeConnection()
    {
        var cancellation = Interlocked.Exchange(ref _connectionCancellation, null);
        var socket = Interlocked.Exchange(ref _socket, null);
        cancellation?.Cancel();
        socket?.Abort();
        socket?.Dispose();
        cancellation?.Dispose();
    }
}
