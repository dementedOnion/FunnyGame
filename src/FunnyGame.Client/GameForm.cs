using System.Diagnostics;
using System.Net.WebSockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
using FunnyGame.Shared;
using Vortice.DXGI;
using Vortice.Direct3D12;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;
using DrawingColor = System.Drawing.Color;

namespace FunnyGame.Client;

public sealed class GameForm : Form
{
    private readonly HashSet<Keys> _keys = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly NetworkClient _network = new();
    private readonly Button _connectButton = new() { Text = "Host / Join", Width = 100, Height = 28 };
    private readonly TextBox _serverBox = new() { Text = "ws://127.0.0.1:5077/ws", Width = 190 };
    private readonly TextBox _nameBox = new() { Text = "Player", Width = 80 };
    private readonly TextBox _passwordBox = new() { Text = "friends", Width = 80, UseSystemPasswordChar = true };
    private readonly ComboBox _gameBox = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDown };
    private Dx12Renderer? _renderer;
    private float _yaw;
    private float _pitch;
    private Vector3 _localPosition = new(0, Protocol.PlayerHeight, -4);
    private bool _mouseCaptured;

    public GameForm()
    {
        Text = "FunnyGame - DirectX 12 Multiplayer FPS";
        Width = 1280;
        Height = 720;
        DoubleBuffered = false;
        KeyPreview = true;

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = DrawingColor.FromArgb(28, 31, 36),
            Padding = new Padding(6),
        };
        top.Controls.AddRange([new Label { Text = "Server", ForeColor = DrawingColor.White, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _serverBox, new Label { Text = "Name", ForeColor = DrawingColor.White, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nameBox, new Label { Text = "Password", ForeColor = DrawingColor.White, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _passwordBox, new Label { Text = "Game", ForeColor = DrawingColor.White, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _gameBox, _connectButton]);
        Controls.Add(top);

        _connectButton.Click += async (_, _) => await ConnectOrJoinAsync();
        KeyDown += (_, e) => _keys.Add(e.KeyCode);
        KeyUp += (_, e) => _keys.Remove(e.KeyCode);
        MouseDown += (_, _) => CaptureMouse();
        MouseMove += OnMouseMove;
        Deactivate += (_, _) => ReleaseMouse();
        FormClosed += (_, _) => _network.Dispose();

        _timer.Interval = 1;
        _timer.Tick += async (_, _) => await FrameAsync();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _renderer = new Dx12Renderer(Handle, ClientSize.Width, ClientSize.Height);
        _timer.Start();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _renderer?.Resize(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
    }

    private async Task ConnectOrJoinAsync()
    {
        if (!_network.IsConnected)
        {
            await _network.ConnectAsync(new Uri(_serverBox.Text), _nameBox.Text, _passwordBox.Text);
        }

        var game = string.IsNullOrWhiteSpace(_gameBox.Text) ? "lobby" : _gameBox.Text.Trim();
        if (_network.Games.Any(g => g.GameId.Equals(game, StringComparison.OrdinalIgnoreCase)))
        {
            await _network.SendAsync(new JoinGameRequest(game));
        }
        else
        {
            await _network.SendAsync(new HostGameRequest(game, 16));
        }

        RefreshGameList();
        CaptureMouse();
    }

    private async Task FrameAsync()
    {
        var dt = (float)_clock.Elapsed.TotalSeconds;
        _clock.Restart();
        RefreshGameList();

        var moveX = (_keys.Contains(Keys.D) ? 1 : 0) - (_keys.Contains(Keys.A) ? 1 : 0);
        var moveZ = (_keys.Contains(Keys.W) ? 1 : 0) - (_keys.Contains(Keys.S) ? 1 : 0);
        var jump = _keys.Contains(Keys.Space);

        if (_network.IsConnected)
        {
            await _network.SendAsync(new PlayerInputMessage(moveX, moveZ, _yaw, jump, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0));
        }
        else
        {
            LocalMove(moveX, moveZ, jump, dt);
        }

        if (_keys.Contains(Keys.E))
        {
            var target = _network.CurrentSnapshot?.Entities.OrderBy(e => Vector3.Distance(e.Position, _localPosition)).FirstOrDefault(e => e.IsInteractable);
            if (target is not null && Vector3.Distance(target.Position, _localPosition) <= 2.25f)
            {
                await _network.SendAsync(new InteractMessage(target.EntityId));
            }
        }

        var snapshot = _network.CurrentSnapshot;
        var local = snapshot?.Players.FirstOrDefault(p => p.PlayerId == _network.PlayerId);
        if (local is not null)
        {
            _localPosition = local.Position;
        }

        _renderer?.Render(_localPosition, _yaw, _pitch, snapshot);
        Text = $"FunnyGame - entities: {snapshot?.EntityCount ?? 4} players: {snapshot?.Players.Count ?? 1} WASD Space E";
    }

    private void LocalMove(int moveX, int moveZ, bool jump, float dt)
    {
        var forward = new Vector3(MathF.Sin(_yaw), 0, MathF.Cos(_yaw));
        var right = new Vector3(forward.Z, 0, -forward.X);
        var wish = right * moveX + forward * moveZ;
        if (wish.LengthSquared() > 1)
        {
            wish = Vector3.Normalize(wish);
        }

        _localPosition += wish * 4.8f * dt;
        _localPosition.Y = Protocol.PlayerHeight;
    }

    private void RefreshGameList()
    {
        var selected = _gameBox.Text;
        _gameBox.Items.Clear();
        foreach (var game in _network.Games)
        {
            _gameBox.Items.Add(game.GameId);
        }

        _gameBox.Text = string.IsNullOrWhiteSpace(selected) ? "lobby" : selected;
    }

    private void CaptureMouse()
    {
        _mouseCaptured = true;
        Cursor.Hide();
        CenterCursor();
    }

    private void ReleaseMouse()
    {
        _mouseCaptured = false;
        Cursor.Show();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_mouseCaptured)
        {
            return;
        }

        var center = PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        _yaw += (Cursor.Position.X - center.X) * 0.0025f;
        _pitch = Math.Clamp(_pitch + (Cursor.Position.Y - center.Y) * 0.0025f, -1.2f, 1.2f);
        CenterCursor();
    }

    private void CenterCursor()
    {
        Cursor.Position = PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
    }
}

sealed class NetworkClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientWebSocket _socket = new();

    public Guid PlayerId { get; private set; }
    public bool IsConnected => _socket.State == WebSocketState.Open;
    public IReadOnlyList<GameInfo> Games { get; private set; } = [];
    public WorldSnapshot? CurrentSnapshot { get; private set; }

    public async Task ConnectAsync(Uri uri, string name, string password)
    {
        await _socket.ConnectAsync(uri, CancellationToken.None);
        _ = Task.Run(ReceiveLoopAsync);
        await SendAsync(new LoginRequest(name, password));
    }

    public async Task SendAsync(NetMessage message)
    {
        if (!IsConnected)
        {
            return;
        }

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];
        while (_socket.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(buffer, CancellationToken.None);
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
                    CurrentSnapshot = snapshot;
                    break;
            }
        }
    }

    public void Dispose()
    {
        _socket.Dispose();
    }
}

sealed class Dx12Renderer : IDisposable
{
    private const int FrameCount = 2;
    private readonly IntPtr _hwnd;
    private ID3D12Device _device;
    private IDXGISwapChain3 _swapChain;
    private ID3D12CommandQueue _queue;
    private ID3D12DescriptorHeap _rtvHeap;
    private ID3D12CommandAllocator _allocator;
    private ID3D12GraphicsCommandList _commandList;
    private readonly ID3D12Resource[] _targets = new ID3D12Resource[FrameCount];
    private int _rtvDescriptorSize;
    private int _frameIndex;

    public Dx12Renderer(IntPtr hwnd, int width, int height)
    {
        _hwnd = hwnd;
        D3D12CreateDevice(null, Vortice.Direct3D.FeatureLevel.Level_11_0, out ID3D12Device? device).CheckError();
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _queue = _device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
        using var factory = CreateDXGIFactory2<IDXGIFactory4>(false);
        var desc = new SwapChainDescription1
        {
            BufferCount = FrameCount,
            Width = (uint)width,
            Height = (uint)height,
            Format = Format.R8G8B8A8_UNorm,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1, 0)
        };
        using var swap = factory.CreateSwapChainForHwnd(_queue, hwnd, desc);
        _swapChain = swap.QueryInterface<IDXGISwapChain3>();
        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;
        _rtvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, FrameCount));
        _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _allocator = _device.CreateCommandAllocator(CommandListType.Direct);
        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, _allocator, null);
        _commandList.Close();
        CreateRenderTargets();
    }

    public void Resize(int width, int height)
    {
        foreach (var target in _targets)
        {
            target?.Dispose();
        }

        _swapChain.ResizeBuffers(FrameCount, (uint)width, (uint)height, Format.R8G8B8A8_UNorm, SwapChainFlags.None);
        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;
        CreateRenderTargets();
    }

    public void Render(Vector3 cameraPosition, float yaw, float pitch, WorldSnapshot? snapshot)
    {
        var clear = new Color4(0.08f, 0.1f, 0.13f, 1);
        _allocator.Reset();
        _commandList.Reset(_allocator);
        var target = _targets[_frameIndex];
        _commandList.ResourceBarrierTransition(target, ResourceStates.Present, ResourceStates.RenderTarget);
        var handle = _rtvHeap.GetCPUDescriptorHandleForHeapStart() + _frameIndex * _rtvDescriptorSize;
        _commandList.OMSetRenderTargets(handle);
        _commandList.ClearRenderTargetView(handle, clear);

        // Prototype renderer: clears through DirectX 12 now; geometry rendering is the next layer.
        _commandList.ResourceBarrierTransition(target, ResourceStates.RenderTarget, ResourceStates.Present);
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        _swapChain.Present(1, PresentFlags.None);
        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;
    }

    private void CreateRenderTargets()
    {
        var handle = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        for (var i = 0; i < FrameCount; i++)
        {
            _targets[i] = _swapChain.GetBuffer<ID3D12Resource>((uint)i);
            _device.CreateRenderTargetView(_targets[i], null, handle + i * _rtvDescriptorSize);
        }
    }

    public void Dispose()
    {
        foreach (var target in _targets)
        {
            target?.Dispose();
        }

        _commandList.Dispose();
        _allocator.Dispose();
        _rtvHeap.Dispose();
        _swapChain.Dispose();
        _queue.Dispose();
        _device.Dispose();
    }
}
