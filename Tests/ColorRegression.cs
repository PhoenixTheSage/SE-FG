using System;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using ClientPlugin.FrameGen;
namespace ClientPlugin.FrameGen { static class FrameGenHost { public static string Error; public static void SetError(string s) { Error = s; } } }
static class ColorRegression
{
    const int W = 64, H = 16;
    static Device d; static DeviceContext c;
    static Texture2D Tex(Format f, BindFlags bind) => new Texture2D(d, new Texture2DDescription { Width = W, Height = H, MipLevels = 1, ArraySize = 1, Format = f, SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = bind });
    static void Check(bool b, string s) { if (!b) throw new Exception(s + " " + FrameGenHost.Error); }
    static byte[] Read(Texture2D tex)
    {
        var td = tex.Description; td.BindFlags = BindFlags.None; td.Usage = ResourceUsage.Staging; td.CpuAccessFlags = CpuAccessFlags.Read;
        using (var staging = new Texture2D(d, td)) { c.CopyResource(tex, staging); var box = c.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None); var bytes = new byte[box.RowPitch]; Marshal.Copy(box.DataPointer, bytes, 0, bytes.Length); c.UnmapSubresource(staging, 0); return bytes; }
    }
    static double Encode(double linear) => 255 * (linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055);
    static double Value(byte[] bytes, Format f, int x, int channel)
    {
        if (f == Format.R16G16B16A16_Float) { int h = BitConverter.ToUInt16(bytes, x * 8 + channel * 2), e = (h >> 10) & 31, m = h & 1023; return (e == 0 ? m * Math.Pow(2, -24) : (1 + m / 1024.0) * Math.Pow(2, e - 15)) * ((h & 32768) == 0 ? 1 : -1); }
        double v = bytes[x * 4 + channel] / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
    static double Sample(byte[] bytes, Format f, double x, int channel) { int i = (int)Math.Floor(x); double t = x - i; return Value(bytes, f, i, channel) * (1 - t) + Value(bytes, f, i + 1, channel) * t; }
    static void Main()
    {
        using (d = new Device(DriverType.Warp, DeviceCreationFlags.None))
        {
            c = d.ImmediateContext;
            foreach (var format in new[] { Format.R16G16B16A16_Float, Format.R8G8B8A8_UNorm_SRgb })
            {
                FrameGenD3d.Release();
                foreach (bool stars in new[] { false, true })
                {
                    using (var src = Tex(Format.R32G32B32A32_Float, BindFlags.ShaderResource))
                    using (var srv = new ShaderResourceView(d, src))
                    using (var dst = Tex(format, BindFlags.RenderTarget | BindFlags.ShaderResource))
                    using (var depth = Tex(Format.R32_Float, BindFlags.ShaderResource | BindFlags.RenderTarget))
                    using (var dr = new RenderTargetView(d, depth))
                    using (var mv = Tex(Format.R16G16_Float, BindFlags.ShaderResource | BindFlags.RenderTarget))
                    using (var mr = new RenderTargetView(d, mv))
                    using (var reactive = Tex(Format.R32_Float, BindFlags.ShaderResource | BindFlags.RenderTarget))
                    using (var rr = new RenderTargetView(d, reactive))
                    using (var rs = new ShaderResourceView(d, reactive))
                    {
                        Check(FrameGenD3d.EnsureColorCopies(d, dst.Description), "copies");
                        var colors = new float[W * H * 4];
                        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) { int i = (y * W + x) * 4; colors[i] = stars ? (float)(0.008 * Math.Exp(-Math.Pow((x - 24) / 0.85, 2)) + 0.03 * Math.Exp(-Math.Pow((x - 40) / 1.5, 2))) : 0.0001f * (x + 1); colors[i + 1] = colors[i] * 0.5f; colors[i + 2] = colors[i] * 0.25f; colors[i + 3] = 1; }
                        using (var stream = DataStream.Create(colors, true, false)) c.UpdateSubresource(new DataBox(stream.DataPointer, W * 16, 0), src, 0);
                        c.ClearRenderTargetView(dr, new SharpDX.Mathematics.Interop.RawColor4(0, 0, 0, 0));
                        Check(FrameGenD3d.CopyOrStretchToScene(d, c, src, Format.R32G32B32A32_Float, srv), "capture");
                        FrameGenD3d.Interpolate(d, c, FrameGenD3d.SceneCopy, depth, mv.NativePointer, FrameGenD3d.InterpCopy, W, H, 1);
                        // No HUD in this regression: match the production scene-to-HUD seed.
                        c.CopyResource(FrameGenD3d.SceneCopy, FrameGenD3d.HudCopy);
                        var reference = Read(FrameGenD3d.SceneCopy);
                        foreach (float motion in new[] { 0f, 2f, 2.75f })
                        {
                            c.ClearRenderTargetView(mr, new SharpDX.Mathematics.Interop.RawColor4(motion, 0, 0, 0));
                            Check(FrameGenD3d.Interpolate(d, c, FrameGenD3d.SceneCopy, depth, mv.NativePointer, FrameGenD3d.InterpCopy, W, H, 0) == 0, "interpolate");
                            Check(FrameGenD3d.BlitGeneratedFrame(d, c, dst), "present");
                            var actual = Read(dst);
                            for (int x = 4; x < W - 4; x++) for (int channel = 0; channel < 3; channel++)
                            {
                                double expected = motion == 0 ? Value(reference, format, x, channel) :
                                    (Sample(reference, format, x + motion / 2, channel) + Sample(reference, format, x - motion / 2, channel)) / 2;
                                double observed = Value(actual, format, x, channel);
                                double error = format == Format.R16G16B16A16_Float ? Math.Abs(expected - observed) : Math.Abs(Encode(expected) - Encode(observed));
                                Check(error <= (format == Format.R16G16B16A16_Float ? Math.Abs(expected) * 0.002 + 0.000001 : 1.01),
                                    "Color mismatch: " + format + " motion=" + motion + " x=" + x + " expected=" + expected + " observed=" + observed);
                            }
                            Console.WriteLine("PASS " + (stars ? "faint stars" : "dark gradient") + " round trip " + format + " motion=" + motion);

                        }
                        foreach (float rejection in new[] { 0f, 1f })
                        {
                            c.ClearRenderTargetView(mr, new SharpDX.Mathematics.Interop.RawColor4(2, 0, 0, 0));
                            c.ClearRenderTargetView(rr, new SharpDX.Mathematics.Interop.RawColor4(rejection, 0, 0, 0));
                            Check(FrameGenD3d.Interpolate(d, c, FrameGenD3d.SceneCopy, depth, mv.NativePointer, FrameGenD3d.InterpCopy, W, H, 0, 1, 1, rs) == 0, "reactive interpolate");
                            Check(FrameGenD3d.BlitGeneratedFrame(d, c, dst), "reactive present");
                            var actual = Read(dst);
                            for (int x = 4; x < W - 4; x++) for (int channel = 0; channel < 3; channel++)
                            {
                                double expected = rejection == 1 ? Value(reference, format, x, channel) :
                                    (Value(reference, format, x - 1, channel) + Value(reference, format, x + 1, channel)) / 2;
                                double observed = Value(actual, format, x, channel);
                                double error = format == Format.R16G16B16A16_Float ? Math.Abs(expected - observed) : Math.Abs(Encode(expected) - Encode(observed));
                                Check(error <= (format == Format.R16G16B16A16_Float ? Math.Abs(expected) * 0.002 + 0.000001 : 1.01), "reactive coverage mismatch");
                            }
                            Console.WriteLine("PASS volume rejection=" + rejection + " " + format + " stars=" + stars);
                        }
                    }
                }
            }
            FrameGenD3d.Release();
        }
    }
}

