using System;
using System.Runtime.InteropServices;
using ClientPlugin.FrameGen;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace ClientPlugin.FrameGen
{
    static class FrameGenHost
    {
        public static string Error;
        public static void SetError(string value) => Error = value;
    }
}

// Patterned GPU inputs expose invalid UV sampling that uniform colors hide.
// Uses the production graphics code and the bytecode shipped in the plugin.
static class WarpRegression
{
    const int W = 65, H = 33; // Also exercise partial dispatch groups.
    static Device device;
    static DeviceContext context;
    static int failures;

    static Texture2D Texture(Format format, int width = W, int height = H) =>
        new(device, new Texture2DDescription { Width = width, Height = height,
            MipLevels = 1, ArraySize = 1, Format = format, Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            SampleDescription = new SampleDescription(1, 0) });

    static void Upload<T>(Texture2D texture, T[] values, int stride) where T : struct
    {
        using var stream = DataStream.Create(values, true, false);
        context.UpdateSubresource(new DataBox(stream.DataPointer, texture.Description.Width * stride, 0), texture, 0);
    }

    static double Half(ushort h)
    {
        int e = (h >> 10) & 31, m = h & 1023;
        if (e == 31) return m == 0 ? double.PositiveInfinity : double.NaN;
        return (e == 0 ? m * Math.Pow(2, -24) : (1 + m / 1024.0) * Math.Pow(2, e - 15)) *
               ((h & 32768) == 0 ? 1 : -1);
    }

    static ushort MotionBits(int value) => value < 0 ? (ushort)(MotionBits(-value) | 0x8000) :
        value == 0 ? (ushort)0 : value == 2 ? (ushort)0x4000 : value == 4 ? (ushort)0x4400 :
        value == 6 ? (ushort)0x4600 : value == 8 ? (ushort)0x4800 : throw new ArgumentException("motion");

    static double[] Read(Texture2D texture)
    {
        var desc = texture.Description;
        desc.BindFlags = BindFlags.None; desc.Usage = ResourceUsage.Staging; desc.CpuAccessFlags = CpuAccessFlags.Read;
        using var staging = new Texture2D(device, desc);
        context.CopyResource(texture, staging);
        var box = context.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
        try
        {
            var result = new double[W * H * 3];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) for (int ch = 0; ch < 3; ch++)
            {
                int offset = y * box.RowPitch + x * (desc.Format == Format.R16G16B16A16_Float ? 8 : 4);
                double value;
                if (desc.Format == Format.R16G16B16A16_Float)
                    value = Half((ushort)Marshal.ReadInt16(box.DataPointer, offset + ch * 2));
                else
                {
                    value = Marshal.ReadByte(box.DataPointer, offset + ch) / 255.0;
                    value = value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
                }
                result[(y * W + x) * 3 + ch] = value;
            }
            return result;
        }
        finally { context.UnmapSubresource(staging, 0); }
    }

    static double Sample(double[] colors, int x, int y, int ch) => colors[(y * W + x) * 3 + ch];

    static void Run(string name, Format format, int dx, int dy, Func<int, int, float> depthAt,
        Func<int, int, bool> fallbackAt, int motionWidth = W, int motionHeight = H,
        Func<int, int, int> motionX = null, Func<int, int, float> colorAt = null,
        bool fractional = false, bool fractionalY = false, bool coherentHistory = false,
        Func<int, int, float> previousColorAt = null, bool recoverHistory = false,
        Func<int, int, float> previousDepthAt = null)
    {
        FrameGenD3d.Release();
        using var source = Texture(Format.R32G32B32A32_Float);
        using var sourceSrv = new ShaderResourceView(device, source);
        using var dest = Texture(format);
        using var depth = Texture(Format.R32_Float);
        using var motion = Texture(Format.R16G16_Float, motionWidth, motionHeight);
        var colors = new float[W * H * 4]; var depths = new float[W * H];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            int p = y * W + x;
            // x/y gradients plus a striped channel make wrong-direction and
            // edge/clamped/borrowed motion visible in both axes.
            colors[p * 4] = colorAt?.Invoke(x, y) ?? (.02f + .2f * x / W);
            colors[p * 4 + 1] = .02f + .2f * y / H;
            colors[p * 4 + 2] = x % 3 == 0 ? .01f : .12f;
            colors[p * 4 + 3] = 1;
            depths[p] = depthAt(x, y);
        }
        var vectors = new ushort[motionWidth * motionHeight * 2];
        for (int y = 0; y < motionHeight; y++) for (int x = 0; x < motionWidth; x++)
        {
            vectors[(y * motionWidth + x) * 2] = fractional ? (ushort)0x4180 : MotionBits(motionX?.Invoke(x, y) ?? dx); // 2.75
            vectors[(y * motionWidth + x) * 2 + 1] = fractionalY ? (ushort)0x4180 : MotionBits(dy);
        }
        var previousColors = (float[])colors.Clone();
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            if (coherentHistory)
            {
                int oldX = Math.Max(0, Math.Min(W - 1, x - dx));
                int oldY = Math.Max(0, Math.Min(H - 1, y - dy));
                Array.Copy(colors, (oldY * W + oldX) * 4, previousColors, (y * W + x) * 4, 4);
            }
            if (previousColorAt != null) previousColors[(y * W + x) * 4] = previousColorAt(x, y);
        }
        var previousDepths = (float[])depths.Clone();
        if (previousDepthAt != null)
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                previousDepths[y * W + x] = previousDepthAt(x, y);
        Upload(source, previousColors, 16); Upload(depth, previousDepths, 4); Upload(motion, vectors, 4);
        if (!FrameGenD3d.EnsureColorCopies(device, dest.Description) ||
            !FrameGenD3d.CopyOrStretchToScene(device, context, source, Format.R32G32B32A32_Float, sourceSrv))
            throw new Exception(FrameGenHost.Error);
        var previousReference = Read(FrameGenD3d.SceneCopy);
        if (FrameGenD3d.Interpolate(device, context, FrameGenD3d.SceneCopy, depth, motion.NativePointer,
                FrameGenD3d.InterpCopy, W, H, 1) != 1) throw new Exception("seed");
        Upload(source, colors, 16); Upload(depth, depths, 4);
        if (!FrameGenD3d.CopyOrStretchToScene(device, context, source, Format.R32G32B32A32_Float, sourceSrv))
            throw new Exception(FrameGenHost.Error);
        var reference = Read(FrameGenD3d.SceneCopy);
        context.CopyResource(FrameGenD3d.SceneCopy, FrameGenD3d.HudCopy);
        if (FrameGenD3d.Interpolate(device, context, FrameGenD3d.SceneCopy, depth, motion.NativePointer,
                FrameGenD3d.InterpCopy, W, H, 0, W / (float)motionWidth, H / (float)motionHeight) != 0 ||
            !FrameGenD3d.BlitGeneratedFrame(device, context, dest)) throw new Exception(FrameGenHost.Error);
        var actual = Read(dest);
        double maxError = 0;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            if (!fallbackAt(x, y)) continue;
            for (int ch = 0; ch < 3; ch++)
                maxError = Math.Max(maxError, Math.Abs(Sample(actual, x, y, ch) - Sample(reference, x, y, ch)));
        }
        // Half-float round trip or at most one encoded SDR value.
        bool pass = maxError <= (format == Format.R16G16B16A16_Float ? .00025 : .004);
        Console.WriteLine((pass ? "PASS " : "FAIL ") + name + " " + format + " maxError=" + maxError.ToString("F6"));
        if (!pass) failures++;
        if (name == "center still interpolates" || name == "far coherent motion still interpolates")
        {
            int x = 30, y = 16;
            double expected = (Sample(reference, x - 1, y, 2) + Sample(previousReference, x + 1, y, 2)) / 2;
            pass = Math.Abs(Sample(actual, x, y, 2) - expected) < .004 &&
                   Math.Abs(Sample(actual, x, y, 2) - Sample(reference, x, y, 2)) > .02;
            Console.WriteLine((pass ? "PASS " : "FAIL ") + "valid interior warp remains active " + format);
            if (!pass) failures++;
        }
        if (recoverHistory)
        {
            // The rejected frame must still become the next color-history
            // sample. A coherent following frame should interpolate normally.
            var next = (float[])colors.Clone();
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                Array.Copy(colors, (y * W + Math.Min(W - 1, x + dx)) * 4, next, (y * W + x) * 4, 4);
            Upload(source, next, 16);
            if (!FrameGenD3d.CopyOrStretchToScene(device, context, source, Format.R32G32B32A32_Float, sourceSrv))
                throw new Exception(FrameGenHost.Error);
            var nextReference = Read(FrameGenD3d.SceneCopy);
            context.CopyResource(FrameGenD3d.SceneCopy, FrameGenD3d.HudCopy);
            if (FrameGenD3d.Interpolate(device, context, FrameGenD3d.SceneCopy, depth, motion.NativePointer,
                    FrameGenD3d.InterpCopy, W, H, 0) != 0 || !FrameGenD3d.BlitGeneratedFrame(device, context, dest))
                throw new Exception(FrameGenHost.Error);
            var nextActual = Read(dest);
            int testX = 31, testY = 16;
            double expected = (Sample(reference, testX + 1, testY, 2) + Sample(nextReference, testX - 1, testY, 2)) / 2;
            pass = Math.Abs(Sample(nextActual, testX, testY, 2) - expected) < .004 &&
                   Math.Abs(Sample(nextActual, testX, testY, 2) - Sample(nextReference, testX, testY, 2)) > .02;
            Console.WriteLine((pass ? "PASS " : "FAIL ") + "history resumes after surface replacement " + format);
            if (!pass) failures++;
        }
    }

    static void Main()
    {
        using (device = new Device(DriverType.Warp, DeviceCreationFlags.None))
        using (context = device.ImmediateContext)
        {
            try
            {
                foreach (var format in new[] { Format.R16G16B16A16_Float, Format.R8G8B8A8_UNorm_SRgb })
                {
                    Run("left/right unavailable UV", format, 4, 0, (x, y) => .5f, (x, y) => x < 2 || x >= W - 2);
                    Run("top/bottom unavailable UV", format, 0, -4, (x, y) => .5f, (x, y) => y < 2 || y >= H - 2);
                    Run("fractional edge filter footprint", format, 2, 0, (x, y) => .5f, (x, y) => x == 0 || x == W - 1, fractional: true);
                    Run("thin rail loses both depth samples", format, 4, 0, (x, y) => x == 5 || x == W - 6 ? .8f : .1f,
                        (x, y) => (x == 5 || x == W - 6) && y > 2 && y < H - 3, colorAt: (x, y) => x == 5 || x == W - 6 ? .4f : .01f);
                    Run("rejected current sample cannot gain weight", format, 4, 0, (x, y) => x < 6 ? .8f : .1f,
                        (x, y) => x == 6 && y > 2 && y < H - 3);
                    Run("fractional depth footprint crosses rail", format, 2, 0, (x, y) => x == 2 ? .8f : .1f,
                        (x, y) => x == 3 && y > 2 && y < H - 3, fractional: true,
                        colorAt: (x, y) => x == 2 ? .4f : .01f);
                    Run("vertical fractional depth footprint crosses rail", format, 0, 2, (x, y) => y == 2 ? .8f : .1f,
                        (x, y) => y == 3 && x > 2 && x < W - 3, fractionalY: true,
                        colorAt: (x, y) => y == 2 ? .4f : .01f);
                    Run("diagonal footprint crosses small foreground", format, 2, 2, (x, y) => x == 2 && y == 2 ? .8f : .1f,
                        (x, y) => x == 3 && y == 3, fractional: true, fractionalY: true,
                        colorAt: (x, y) => x == 2 && y == 2 ? .4f : .01f);
                    Run("internal motion does not bleed into still pixels", format, 0, 0, (x, y) => .5f,
                        (x, y) => x == 7 && y > 2 && y < H - 3, motionWidth: 32, motionHeight: 16,
                        motionX: (x, y) => x >= 4 ? 8 : 0);
                    Run("stationary edges and center", format, 0, 0, (x, y) => .5f, (x, y) => true);
                    Run("center still interpolates", format, 2, 0, (x, y) => .5f, (x, y) => x == 0 || x == W - 1,
                        coherentHistory: true);
                    Run("distant asteroid/sky depth boundary", format, 4, 0, (x, y) => x == 5 ? 1e-5f : 0f,
                        (x, y) => x == 5 && y > 2 && y < H - 3, colorAt: (x, y) => x == 5 ? .4f : .01f);
                    Run("distant neighboring voxel depths", format, 4, 0, (x, y) => x == 5 ? 2e-5f : 1e-5f,
                        (x, y) => x == 5 && y > 2 && y < H - 3, colorAt: (x, y) => x == 5 ? .4f : .01f);
                    Run("streamed surface replaces old appearance", format, 2, 0, (x, y) => 1e-5f,
                        (x, y) => x > 4 && x < W - 4 && y > 2 && y < H - 3,
                        previousColorAt: (x, y) => .8f, recoverHistory: true);
                    Run("static detail with false persistent motion", format, 2, 0, (x, y) => 1e-5f,
                        (x, y) => x > 4 && x < W - 4 && y > 2 && y < H - 3,
                        colorAt: (x, y) => x % 3 == 0 ? .01f : .4f);
                    Run("far coherent motion still interpolates", format, 2, 0, (x, y) => 1e-5f,
                        (x, y) => x == 0 || x == W - 1, coherentHistory: true);
                    // Orbiting camera uncovers sky formerly occupied by the ship.
                    // Current depth alone cannot identify the old silhouette.
                    Run("old ship silhouette cannot ghost onto sky", format, 4, 0, (x, y) => 0f,
                        (x, y) => x >= 28 && x <= 34 && y > 2 && y < H - 3,
                        colorAt: (x, y) => .01f,
                        previousColorAt: (x, y) => x >= 30 && x <= 36 ? .6f : .01f,
                        previousDepthAt: (x, y) => x >= 30 && x <= 36 ? .5f : 0f);
                    Run("fractional old ship silhouette footprint", format, 2, 0, (x, y) => 0f,
                        (x, y) => x == 28 && y > 2 && y < H - 3, fractional: true,
                        colorAt: (x, y) => .01f,
                        previousColorAt: (x, y) => x == 30 ? .6f : .01f,
                        previousDepthAt: (x, y) => x == 30 ? .5f : 0f);
                    Run("newly exposed ship cannot use sky history", format, 2, 0, (x, y) => .5f,
                        (x, y) => x > 4 && x < W - 4 && y > 2 && y < H - 3,
                        coherentHistory: true, previousDepthAt: (x, y) => 0f, recoverHistory: true);
                }
            }
            finally { context.ClearState(); FrameGenD3d.Release(); }
        }
        if (failures != 0) throw new Exception(failures + " warp regression cases failed");
    }
}
