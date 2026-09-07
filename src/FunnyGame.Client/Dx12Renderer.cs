using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FunnyGame.Shared;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace FunnyGame.Client;

sealed unsafe class Dx12Renderer : IDisposable
{
    private const int FrameCount = 2;
    private const int MaxVertices = 32768;
    private const Format ColorFormat = Format.R8G8B8A8_UNorm;
    private const Format DepthFormat = Format.D32_Float;

    public static IReadOnlyList<EntityState> LocalEntities { get; } =
    [
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), "Light", new(0, 4, -2), false, 0),
        new(Guid.Parse("10000000-0000-0000-0000-000000000002"), "Button", new(2, 0.45f, 2), true, 0),
        new(Guid.Parse("10000000-0000-0000-0000-000000000003"), "Crate", new(-2, 0.5f, 3), true, 0),
        new(Guid.Parse("10000000-0000-0000-0000-000000000004"), "SpawnMarker", new(0, 0.03f, 0), false, 0)
    ];

    private readonly ID3D12Device _device;
    private readonly ID3D12CommandQueue _queue;
    private readonly IDXGISwapChain3 _swapChain;
    private readonly ID3D12DescriptorHeap _rtvHeap;
    private readonly ID3D12DescriptorHeap _dsvHeap;
    private readonly ID3D12CommandAllocator _allocator;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12PipelineState _pipelineState;
    private readonly ID3D12Resource _vertexBuffer;
    private readonly ID3D12Resource _constantBuffer;
    private readonly ID3D12Fence _fence;
    private readonly ID3D12Resource[] _targets = new ID3D12Resource[FrameCount];
    private readonly List<Vertex> _vertices = new(MaxVertices);
    private void* _vertexData;
    private void* _constantData;
    private ID3D12Resource? _depthBuffer;
    private int _rtvDescriptorSize;
    private int _frameIndex;
    private int _width;
    private int _height;
    private ulong _fenceValue = 1;
    private bool _disposed;

    public Dx12Renderer(IntPtr hwnd, int width, int height)
    {
        _width = width;
        _height = height;

        D3D12CreateDevice(null, FeatureLevel.Level_11_0, out ID3D12Device? device).CheckError();
        _device = device ?? throw new InvalidOperationException("DirectX 12 is not available on this computer.");
        _queue = _device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));

        using var factory = CreateDXGIFactory2<IDXGIFactory4>(false);
        var swapChainDescription = new SwapChainDescription1
        {
            BufferCount = FrameCount,
            Width = (uint)width,
            Height = (uint)height,
            Format = ColorFormat,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = SampleDescription.Default
        };
        using var swapChain = factory.CreateSwapChainForHwnd(_queue, hwnd, swapChainDescription);
        _swapChain = swapChain.QueryInterface<IDXGISwapChain3>();
        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;

        _rtvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, FrameCount));
        _dsvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, 1));
        _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _allocator = _device.CreateCommandAllocator(CommandListType.Direct);
        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, _allocator, null);
        _commandList.Close();

        var rootFlags = RootSignatureFlags.AllowInputAssemblerInputLayout |
            RootSignatureFlags.DenyHullShaderRootAccess |
            RootSignatureFlags.DenyDomainShaderRootAccess |
            RootSignatureFlags.DenyGeometryShaderRootAccess |
            RootSignatureFlags.DenyPixelShaderRootAccess;
        var cameraBuffer = new RootDescriptor1(0, 0, RootDescriptorFlags.DataStaticWhileSetAtExecute);
        _rootSignature = _device.CreateRootSignature(new RootSignatureDescription1(
            rootFlags,
            [new RootParameter1(RootParameterType.ConstantBufferView, cameraBuffer, ShaderVisibility.Vertex)]));

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Scene.hlsl");
        var vertexShader = CompileShader(shaderPath, "VSMain", "vs_5_1");
        var pixelShader = CompileShader(shaderPath, "PSMain", "ps_5_1");
        var pipelineDescription = new GraphicsPipelineStateDescription
        {
            RootSignature = _rootSignature,
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            InputLayout = new InputLayoutDescription(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 12, 0)
            ]),
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RasterizerState = RasterizerDescription.CullNone,
            BlendState = BlendDescription.Opaque,
            DepthStencilState = DepthStencilDescription.Default,
            RenderTargetFormats = [ColorFormat],
            DepthStencilFormat = DepthFormat,
            SampleDescription = SampleDescription.Default
        };
        _pipelineState = _device.CreateGraphicsPipelineState<ID3D12PipelineState>(pipelineDescription);

        var vertexBufferSize = (ulong)(MaxVertices * sizeof(Vertex));
        _vertexBuffer = _device.CreateCommittedResource(
            HeapType.Upload,
            ResourceDescription.Buffer(vertexBufferSize),
            ResourceStates.GenericRead);
        fixed (void** vertexData = &_vertexData)
        {
            _vertexBuffer.Map(0, vertexData).CheckError();
        }

        _constantBuffer = _device.CreateCommittedResource(
            HeapType.Upload,
            ResourceDescription.Buffer(256),
            ResourceStates.GenericRead);
        fixed (void** constantData = &_constantData)
        {
            _constantBuffer.Map(0, constantData).CheckError();
        }

        _fence = _device.CreateFence(0);
        CreateWindowResources();
    }

    public void Resize(int width, int height)
    {
        if (_disposed || width == _width && height == _height)
        {
            return;
        }

        WaitForGpu();
        _width = width;
        _height = height;
        ReleaseWindowResources();
        _swapChain.ResizeBuffers(FrameCount, (uint)width, (uint)height, ColorFormat, SwapChainFlags.None).CheckError();
        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;
        CreateWindowResources();
    }

    public void Render(Vector3 cameraPosition, float yaw, float pitch, WorldSnapshot? snapshot, Guid localPlayerId)
    {
        if (_disposed || _width < 1 || _height < 1)
        {
            return;
        }

        BuildScene(cameraPosition, snapshot, localPlayerId);
        var forward = Vector3.Normalize(new Vector3(
            MathF.Sin(yaw) * MathF.Cos(pitch),
            -MathF.Sin(pitch),
            MathF.Cos(yaw) * MathF.Cos(pitch)));
        var view = Matrix4x4.CreateLookAt(cameraPosition, cameraPosition + forward, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, (float)_width / _height, 0.05f, 150f);
        var viewProjection = Matrix4x4.Transpose(Matrix4x4.Multiply(view, projection));
        Unsafe.Copy(_constantData, ref viewProjection);

        var vertexSpan = CollectionsMarshal.AsSpan(_vertices);
        fixed (Vertex* source = vertexSpan)
        {
            Buffer.MemoryCopy(source, _vertexData, MaxVertices * sizeof(Vertex), vertexSpan.Length * sizeof(Vertex));
        }

        _allocator.Reset();
        _commandList.Reset(_allocator, _pipelineState);
        var target = _targets[_frameIndex];
        _commandList.ResourceBarrierTransition(target, ResourceStates.Present, ResourceStates.RenderTarget);

        var rtv = _rtvHeap.GetCPUDescriptorHandleForHeapStart() + _frameIndex * _rtvDescriptorSize;
        var dsv = _dsvHeap.GetCPUDescriptorHandleForHeapStart();
        _commandList.RSSetViewport(new Viewport(0, 0, _width, _height, 0, 1));
        _commandList.RSSetScissorRect(new RectI(0, 0, _width, _height));
        _commandList.OMSetRenderTargets(rtv, dsv);
        _commandList.ClearRenderTargetView(rtv, new Color4(0.08f, 0.13f, 0.2f, 1));
        _commandList.ClearDepthStencilView(dsv, ClearFlags.Depth, 1f, 0);

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetGraphicsRootConstantBufferView(0, _constantBuffer.GPUVirtualAddress);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.IASetVertexBuffers(0, new VertexBufferView(
            _vertexBuffer.GPUVirtualAddress,
            (uint)(vertexSpan.Length * sizeof(Vertex)),
            (uint)sizeof(Vertex)));
        _commandList.DrawInstanced((uint)vertexSpan.Length, 1, 0, 0);

        _commandList.ResourceBarrierTransition(target, ResourceStates.RenderTarget, ResourceStates.Present);
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        _swapChain.Present(1, PresentFlags.None).CheckError();
        WaitForGpu();
        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;
    }

    private void BuildScene(Vector3 cameraPosition, WorldSnapshot? snapshot, Guid localPlayerId)
    {
        _vertices.Clear();
        AddGround();
        AddRoomShell();

        foreach (var entity in snapshot?.Entities ?? LocalEntities)
        {
            switch (entity.Kind)
            {
                case "Light":
                    AddBox(new Vector3(entity.Position.X, 1.8f, entity.Position.Z), new Vector3(0.12f, 3.6f, 0.12f), new Vector4(0.24f, 0.27f, 0.3f, 1));
                    AddBox(new Vector3(entity.Position.X + 0.38f, 3.5f, entity.Position.Z), new Vector3(0.85f, 0.12f, 0.12f), new Vector4(0.24f, 0.27f, 0.3f, 1));
                    AddBox(new Vector3(entity.Position.X + 0.78f, 3.34f, entity.Position.Z), new Vector3(0.52f, 0.28f, 0.52f), new Vector4(0.82f, 0.78f, 0.58f, 1));
                    AddBox(new Vector3(entity.Position.X + 0.78f, 3.12f, entity.Position.Z), new Vector3(0.2f), new Vector4(1f, 0.94f, 0.55f, 1));
                    break;
                case "Button":
                    var buttonColor = entity.UseCount % 2 == 0
                        ? new Vector4(0.08f, 0.55f, 0.95f, 1)
                        : new Vector4(0.15f, 0.95f, 0.42f, 1);
                    AddBox(new Vector3(entity.Position.X, 0.35f, entity.Position.Z), new Vector3(0.8f, 0.7f, 0.8f), new Vector4(0.2f, 0.23f, 0.27f, 1));
                    AddBox(new Vector3(entity.Position.X, 0.76f, entity.Position.Z), new Vector3(0.55f, 0.12f, 0.55f), buttonColor);
                    break;
                case "Crate":
                    AddBox(entity.Position, new Vector3(1f), new Vector4(0.65f, 0.33f, 0.12f, 1));
                    AddBox(new Vector3(entity.Position.X, entity.Position.Y, entity.Position.Z - 0.51f), new Vector3(1.08f, 0.12f, 0.06f), new Vector4(0.32f, 0.16f, 0.06f, 1));
                    AddBox(new Vector3(entity.Position.X, entity.Position.Y, entity.Position.Z + 0.51f), new Vector3(1.08f, 0.12f, 0.06f), new Vector4(0.32f, 0.16f, 0.06f, 1));
                    break;
                case "SpawnMarker":
                    AddBox(entity.Position, new Vector3(1.4f, 0.06f, 1.4f), new Vector4(0.18f, 0.72f, 0.56f, 1));
                    break;
            }
        }

        if (snapshot is null)
        {
            return;
        }

        foreach (var player in snapshot.Players.Where(player =>
                     player.PlayerId != localPlayerId &&
                     Vector3.DistanceSquared(player.Position, cameraPosition) > 0.25f))
        {
            var bodyCenter = player.Position - new Vector3(0, Protocol.PlayerHeight / 2f, 0);
            AddBox(bodyCenter, new Vector3(0.65f, Protocol.PlayerHeight, 0.65f), new Vector4(0.9f, 0.18f, 0.15f, 1));
        }
    }

    private void AddRoomShell()
    {
        var wall = new Vector4(0.32f, 0.36f, 0.4f, 1);
        var trim = new Vector4(0.12f, 0.15f, 0.18f, 1);
        AddBox(new Vector3(0, 1.5f, 10), new Vector3(20, 3, 0.2f), wall);
        AddBox(new Vector3(-10, 1.5f, 0), new Vector3(0.2f, 3, 20), wall);
        AddBox(new Vector3(10, 1.5f, 0), new Vector3(0.2f, 3, 20), wall);
        AddBox(new Vector3(0, 0.08f, 9.82f), new Vector3(20, 0.16f, 0.16f), trim);
        AddBox(new Vector3(-9.82f, 0.08f, 0), new Vector3(0.16f, 0.16f, 20), trim);
        AddBox(new Vector3(9.82f, 0.08f, 0), new Vector3(0.16f, 0.16f, 20), trim);
    }

    private void AddGround()
    {
        var lightPosition = new Vector2(0, 2);
        for (var x = -20; x < 20; x++)
        {
            for (var z = -20; z < 20; z++)
            {
                var center = new Vector2(x + 0.5f, z + 0.5f);
                var light = Math.Clamp(1f - Vector2.Distance(center, lightPosition) / 14f, 0, 1);
                var checker = ((x + z) & 1) == 0 ? 0.04f : 0f;
                var color = new Vector4(0.16f + light * 0.12f + checker, 0.21f + light * 0.1f + checker, 0.19f + light * 0.05f + checker, 1);
                AddQuad(
                    new Vector3(x, 0, z),
                    new Vector3(x, 0, z + 1),
                    new Vector3(x + 1, 0, z + 1),
                    new Vector3(x + 1, 0, z),
                    color);
            }
        }
    }

    private void AddBox(Vector3 center, Vector3 size, Vector4 color)
    {
        var half = size / 2f;
        var p000 = center + new Vector3(-half.X, -half.Y, -half.Z);
        var p001 = center + new Vector3(-half.X, -half.Y, half.Z);
        var p010 = center + new Vector3(-half.X, half.Y, -half.Z);
        var p011 = center + new Vector3(-half.X, half.Y, half.Z);
        var p100 = center + new Vector3(half.X, -half.Y, -half.Z);
        var p101 = center + new Vector3(half.X, -half.Y, half.Z);
        var p110 = center + new Vector3(half.X, half.Y, -half.Z);
        var p111 = center + new Vector3(half.X, half.Y, half.Z);

        AddQuad(p001, p101, p111, p011, Shade(color, 0.9f));
        AddQuad(p100, p000, p010, p110, Shade(color, 0.55f));
        AddQuad(p000, p001, p011, p010, Shade(color, 0.7f));
        AddQuad(p101, p100, p110, p111, Shade(color, 0.75f));
        AddQuad(p010, p011, p111, p110, color);
        AddQuad(p000, p100, p101, p001, Shade(color, 0.45f));
    }

    private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 color)
    {
        AddVertex(a, color);
        AddVertex(b, color);
        AddVertex(c, color);
        AddVertex(a, color);
        AddVertex(c, color);
        AddVertex(d, color);
    }

    private void AddVertex(Vector3 position, Vector4 color)
    {
        if (_vertices.Count >= MaxVertices)
        {
            throw new InvalidOperationException("Scene exceeded the prototype vertex budget.");
        }

        _vertices.Add(new Vertex(position, color));
    }

    private static Vector4 Shade(Vector4 color, float amount) => new(color.X * amount, color.Y * amount, color.Z * amount, color.W);

    private static byte[] CompileShader(string path, string entryPoint, string target)
    {
        return Compiler.CompileFromFile(
            path,
            entryPoint,
            target,
            ShaderFlags.OptimizationLevel3,
            EffectFlags.None).ToArray();
    }

    private void CreateWindowResources()
    {
        var rtv = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        for (var i = 0; i < FrameCount; i++)
        {
            _targets[i] = _swapChain.GetBuffer<ID3D12Resource>((uint)i);
            _device.CreateRenderTargetView(_targets[i], null, rtv + i * _rtvDescriptorSize);
        }

        var depthDescription = ResourceDescription.Texture2D(
            DepthFormat,
            (uint)_width,
            (uint)_height,
            1,
            1,
            1,
            0,
            ResourceFlags.AllowDepthStencil);
        _depthBuffer = _device.CreateCommittedResource(
            HeapType.Default,
            depthDescription,
            ResourceStates.DepthWrite,
            new ClearValue(DepthFormat, 1f, 0));
        _device.CreateDepthStencilView(_depthBuffer, null, _dsvHeap.GetCPUDescriptorHandleForHeapStart());
    }

    private void ReleaseWindowResources()
    {
        foreach (var target in _targets)
        {
            target?.Dispose();
        }

        _depthBuffer?.Dispose();
        _depthBuffer = null;
    }

    private void WaitForGpu()
    {
        var value = _fenceValue++;
        _queue.Signal(_fence, value).CheckError();
        while (_fence.CompletedValue < value)
        {
            Thread.Yield();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        WaitForGpu();
        _disposed = true;
        ReleaseWindowResources();
        _vertexBuffer.Unmap(0);
        _constantBuffer.Unmap(0);
        _fence.Dispose();
        _constantBuffer.Dispose();
        _vertexBuffer.Dispose();
        _pipelineState.Dispose();
        _rootSignature.Dispose();
        _commandList.Dispose();
        _allocator.Dispose();
        _dsvHeap.Dispose();
        _rtvHeap.Dispose();
        _swapChain.Dispose();
        _queue.Dispose();
        _device.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Vertex(Vector3 Position, Vector4 Color);
}
