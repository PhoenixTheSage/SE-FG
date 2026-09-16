using System;
using System.Collections.Generic;
using System.Threading;
using ClientPlugin.FrameGen;
using HarmonyLib;
using Sandbox.Game.World;
using SharpDX.Direct3D11;
using VRage.Game;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Patches;

/// <summary>
/// Skip Keen PostPP HUD into the scene color, freeze Rich HUD in layout
/// view space, and draw it onto the backbuffer parented to the render camera.
/// Same camera lock as SE-DLSS <c>PostPpHudSpace</c>; no DRS/jitter path.
/// </summary>
internal static class PostPpHudPass
{
    const int PostPpBucket = 4;
    const int StaleHudMs = 200;

    [ThreadStatic]
    static bool _drawingPostPp;

    static bool _drewHudThisScene;
    static bool _skippedKeenPostPp;
    static bool _loggedSkip;
    static readonly object SnapshotLock = new();
    static readonly List<MyBillboard> PendingAdds = new(512);
    static readonly List<MyBillboard> UniqueScratch = new(512);
    static List<MyBillboard> _published = new(512);
    static List<MyBillboard> _publishScratch = new(512);
    static readonly List<MyBillboard> Snapshot = new(512);
    static readonly List<MyBillboard> CaptureRefs = new(512);
    static readonly HashSet<MyBillboard> CaptureSeen = [];
    static readonly HashSet<MyBillboard> DedupeSeen = [];
    static int _lastSubmitTick;
    static int _walkingPersistents;
    static bool _publishing;
    static bool _publishedAreViewLocal;

    public static void BeginDraw()
    {
        _drewHudThisScene = false;
        _skippedKeenPostPp = false;
    }

    public static void PrepareNextPresent()
    {
        _drewHudThisScene = false;
    }

    public static void Reset()
    {
        _drawingPostPp = false;
        _drewHudThisScene = false;
        _skippedKeenPostPp = false;
        _loggedSkip = false;
        _walkingPersistents = 0;
        _publishing = false;
        lock (SnapshotLock)
        {
            PendingAdds.Clear();
            UniqueScratch.Clear();
            _published.Clear();
            _publishScratch.Clear();
            Snapshot.Clear();
            CaptureRefs.Clear();
            CaptureSeen.Clear();
            DedupeSeen.Clear();
            _publishedAreViewLocal = false;
            _lastSubmitTick = 0;
        }
    }

    public static bool ShouldSkipKeenPostPp()
    {
        if (!FrameGenRuntime.IsLive)
            return false;
        if (!_skippedKeenPostPp)
        {
            if (!_loggedSkip)
            {
                _loggedSkip = true;
                DebugLog.Write("skip Keen RenderPostPP; redraw PostPP once after CopyToRT");
            }
        }
        _skippedKeenPostPp = true;
        return true;
    }

    public static void PublishCompletedFrame()
    {
        if (!FrameGenRuntime.IsLive || _publishing)
            return;
        if (IsRenderThread())
            return;

        _publishing = true;
        try
        {
            UniqueScratch.Clear();
            DedupeSeen.Clear();
            _walkingPersistents++;
            try
            {
                MyRenderProxy.ApplyActionOnPersistentBillboards(ConsiderPublish);
            }
            finally
            {
                _walkingPersistents--;
            }

            lock (SnapshotLock)
            {
                if (UniqueScratch.Count == 0)
                    DedupeInto(PendingAdds, UniqueScratch);
                PendingAdds.Clear();
                if (UniqueScratch.Count == 0)
                    return;

                FreezeInto(UniqueScratch, _publishScratch);
                var camera = MySector.MainCamera;
                var view = camera != null ? camera.ViewMatrix : default;
                if (camera != null && view.IsValid())
                {
                    PostPpHudSpace.ToViewLocal(_publishScratch, view);
                    _publishedAreViewLocal = true;
                }
                else
                    _publishedAreViewLocal = false;

                var published = _published;
                _published = _publishScratch;
                _publishScratch = published;
                _lastSubmitTick = Environment.TickCount;
            }
        }
        catch (Exception e)
        {
            DebugLog.Write("PublishCompletedFrame: " + e);
        }
        finally
        {
            _publishing = false;
        }
    }

    public static void NoteAdd(MyBillboard billboard)
    {
        if (!FrameGenRuntime.IsLive || !IsPostPp(billboard))
            return;
        lock (SnapshotLock)
        {
            PendingAdds.Add(billboard);
            _lastSubmitTick = Environment.TickCount;
        }
    }

    public static void NoteAdds(IEnumerable<MyBillboard> billboards)
    {
        if (!FrameGenRuntime.IsLive || billboards == null)
            return;
        lock (SnapshotLock)
        {
            foreach (var billboard in billboards)
            {
                if (!IsPostPp(billboard))
                    continue;
                PendingAdds.Add(billboard);
                _lastSubmitTick = Environment.TickCount;
            }
        }
    }

    public static void TryDrawAfterSceneBlit()
    {
        if (!FrameGenRuntime.IsLive || _drewHudThisScene)
            return;
        // Only replace Keen's pass when the prefix actually skipped it.
        // If the Harmony hook missed, Keen already blended bucket 4 — drawing
        // again stacks UiBkOpacity. Do not guess from !_keenPostPpInvoked.
        if (!_skippedKeenPostPp)
            return;
        var dest = UnwrapHudTarget(MyRender11.Backbuffer);
        var rc = MyRender11.RC;
        if (dest == null || rc == null)
            return;
        TryDrawOnto(rc, dest);
    }

    static bool TryDrawOnto(MyRenderContext rc, IRtvBindable dest)
    {
        if (_drawingPostPp || rc == null || dest == null)
            return true;
        if (!EnsurePostPpBatches(rc))
            return true;
        if (dest.Rtv == null)
            return true;

        _drawingPostPp = true;
        try
        {
            rc.ComputeShader.SetUav(0, null);
            rc.SetBlendState(MyBlendStateManager.BlendAlphaPremult);
            rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
            BindHudRtv(rc, dest);
            var vw = FrameGenRuntime.Width > 0 ? FrameGenRuntime.Width : dest.Size.X;
            var vh = FrameGenRuntime.Height > 0 ? FrameGenRuntime.Height : dest.Size.Y;
            rc.SetViewport(0f, 0f, vw, vh);
            try
            {
                MyBillboardRenderer.Render(
                    rc, null, MyBillboardRenderer.m_bucketBatches[PostPpBucket], false, true);
                _drewHudThisScene = true;
            }
            finally
            {
                rc.SetRtvNull();
            }
        }
        finally
        {
            _drawingPostPp = false;
        }

        return true;
    }

    static bool EnsurePostPpBatches(MyRenderContext rc)
    {
        if (rc == null || _drawingPostPp)
            return HasBucket(PostPpBucket);

        if (!CaptureLivePostPp())
            return false;
        if (PrepareFromSnapshot() <= 0)
            return false;

        MyBillboardRenderer.GatherInternal(rc);
        MyBillboardRenderer.TransferData(rc);
        return HasBucket(PostPpBucket);
    }

    static bool CaptureLivePostPp()
    {
        var viewLocal = false;
        var havePublished = false;
        lock (SnapshotLock)
        {
            if (HudSnapshotIsStaleLocked())
            {
                _published.Clear();
                _publishedAreViewLocal = false;
            }

            // Published persistents are one copy of each HUD quad. Do not union
            // with BillboardsRead / once-pool clones — that stacks opacity.
            if (_published.Count > 0)
            {
                FreezeInto(_published, Snapshot);
                viewLocal = _publishedAreViewLocal;
                havePublished = true;
            }
        }

        if (havePublished)
        {
            if (viewLocal)
                PostPpHudSpace.ToWorldFromViewLocal(Snapshot);
            return Snapshot.Count > 0;
        }

        CaptureRefs.Clear();
        CaptureSeen.Clear();
        try
        {
            _walkingPersistents++;
            try
            {
                MyRenderProxy.ApplyActionOnPersistentBillboards(ConsiderCapture);
            }
            finally
            {
                _walkingPersistents--;
            }
        }
        catch (Exception e)
        {
            DebugLog.Write("CaptureLivePostPp: " + e.GetType().Name);
        }

        lock (SnapshotLock)
        {
            foreach (var billboard in PendingAdds)
                ConsiderCapture(billboard);
            FreezeInto(CaptureRefs, Snapshot);
        }

        return Snapshot.Count > 0;
    }

    static IRtvBindable UnwrapHudTarget(IRtvBindable target)
    {
        if (target is ICustomTexture custom)
            return custom.SRgb ?? custom.Linear ?? target;
        return target;
    }

    static void BindHudRtv(MyRenderContext rc, IRtvBindable target)
    {
        rc.ResetTargets();
        var rtv = target?.Rtv;
        if (rtv != null && rc.DeviceContext != null)
            rc.DeviceContext.OutputMerger.SetTargets((DepthStencilView)null, 1, new[] { rtv });
        if (target != null)
            rc.SetRtv(target);
    }

    static bool HudSnapshotIsStaleLocked()
    {
        if (_published.Count == 0 || PendingAdds.Count > 0)
            return false;
        unchecked
        {
            return Environment.TickCount - _lastSubmitTick > StaleHudMs;
        }
    }

    static bool IsRenderThread()
    {
        var renderThread = MyRender11.RenderThread;
        return renderThread != null && Thread.CurrentThread == renderThread;
    }

    static void ConsiderPublish(MyBillboard billboard)
    {
        if (billboard == null || !IsPostPp(billboard) || !DedupeSeen.Add(billboard))
            return;
        UniqueScratch.Add(billboard);
    }

    static void ConsiderCapture(MyBillboard billboard)
    {
        if (billboard == null || !IsPostPp(billboard) || !CaptureSeen.Add(billboard))
            return;
        CaptureRefs.Add(billboard);
    }

    static bool IsPostPp(MyBillboard billboard) =>
        billboard is { BlendType: MyBillboard.BlendTypeEnum.PostPP };

    static void DedupeInto(List<MyBillboard> source, List<MyBillboard> dest)
    {
        dest.Clear();
        DedupeSeen.Clear();
        foreach (var billboard in source)
        {
            if (billboard == null || !DedupeSeen.Add(billboard))
                continue;
            dest.Add(billboard);
        }
    }

    static void FreezeInto(List<MyBillboard> source, List<MyBillboard> dest)
    {
        while (dest.Count < source.Count)
            dest.Add(null);
        for (var i = 0; i < source.Count; i++)
            CopyBillboard(source[i], ref dest, i);
        if (dest.Count > source.Count)
            dest.RemoveRange(source.Count, dest.Count - source.Count);
    }

    static void CopyBillboard(MyBillboard source, ref List<MyBillboard> dest, int index)
    {
        var triangle = source is MyTriangleBillboard;
        var copy = dest[index];
        if (copy == null || triangle != (copy is MyTriangleBillboard))
        {
            copy = triangle ? new MyTriangleBillboard() : new MyBillboard();
            dest[index] = copy;
        }

        copy.Material = source.Material;
        copy.BlendType = source.BlendType;
        copy.Position0 = source.Position0;
        copy.Position1 = source.Position1;
        copy.Position2 = source.Position2;
        copy.Position3 = source.Position3;
        copy.Color = source.Color;
        copy.ColorIntensity = source.ColorIntensity;
        copy.SoftParticleDistanceScale = source.SoftParticleDistanceScale;
        copy.UVOffset = source.UVOffset;
        copy.UVSize = source.UVSize;
        copy.LocalType = source.LocalType;
        copy.ParentID = source.ParentID;
        copy.DistanceSquared = source.DistanceSquared;
        copy.Reflectivity = source.Reflectivity;
        copy.AlphaCutout = source.AlphaCutout;
        copy.CustomViewProjection = source.CustomViewProjection;

        if (source is MyTriangleBillboard sourceTriangle && copy is MyTriangleBillboard copyTriangle)
        {
            copyTriangle.UV0 = sourceTriangle.UV0;
            copyTriangle.UV1 = sourceTriangle.UV1;
            copyTriangle.UV2 = sourceTriangle.UV2;
            copyTriangle.Normal0 = sourceTriangle.Normal0;
        }
    }

    static int PrepareFromSnapshot()
    {
        var counts = MyBillboardRenderer.m_bucketCounts;
        MyBillboardRenderer.m_batches.Clear();
        for (var i = 0; i < 6; i++)
            counts[i] = 0;

        foreach (var billboard in Snapshot)
            CountBillboard(billboard, counts);

        var total = 0;
        for (var i = 0; i < 6; i++)
            total += counts[i];
        if (total == 0)
        {
            MyBillboardRenderer.m_billboardCountSafe = 0;
            return 0;
        }

        var safe = total > 32768 ? 32768 : total;
        var tempSize = MyBillboardRenderer.m_tempBuffer.Length;
        while (total > tempSize)
            tempSize *= 2;
        Array.Resize(ref MyBillboardRenderer.m_tempBuffer, tempSize);

        var arrays = MyBillboardRenderer.m_arrayDataBillboards;
        var dataSize = arrays.Length;
        while (safe > dataSize)
            dataSize *= 2;
        arrays.Resize(dataSize);
        MyBillboardRenderer.m_arrayDataBillboards = arrays;

        for (var i = 0; i < 6; i++)
            MyBillboardRenderer.m_bucketBatches[i] = default;

        MyBillboardRenderer.m_lastBatchOffset = 0;
        var indices = MyBillboardRenderer.m_bucketIndices;
        indices[0] = 0;
        for (var i = 1; i < 6; i++)
            indices[i] = indices[i - 1] + counts[i - 1];

        foreach (var billboard in Snapshot)
            PlaceBillboard(billboard, indices);

        indices[0] = 0;
        for (var i = 1; i < 6; i++)
            indices[i] = indices[i - 1] + counts[i - 1];

        for (var i = 0; i < 6; i++)
            if (i != 3 && i != 4 && counts[i] > 0)
                Array.Sort(MyBillboardRenderer.m_tempBuffer, indices[i], counts[i]);

        MyBillboardRenderer.m_billboardCountSafe = safe;
        return safe;
    }

    static void CountBillboard(MyBillboard billboard, int[] counts)
    {
        if (billboard == null)
            return;
        var bucket = MyBillboardRenderer.GetBillboardBucket(billboard);
        if ((uint)bucket < 6)
            counts[bucket]++;
    }

    static void PlaceBillboard(MyBillboard billboard, int[] indices)
    {
        if (billboard == null)
            return;
        var bucket = MyBillboardRenderer.GetBillboardBucket(billboard);
        if ((uint)bucket < 6)
            MyBillboardRenderer.m_tempBuffer[indices[bucket]++] = billboard;
    }

    static bool HasBucket(int bucket)
    {
        return MyBillboardRenderer.m_bucketBatches is { } batches &&
               bucket >= 0 &&
               bucket < batches.Length &&
               batches[bucket].Count > 0;
    }
}

[HarmonyPatch(typeof(MyBillboardRenderer), nameof(MyBillboardRenderer.RenderPostPP))]
internal static class BillboardPostPpPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(MyRenderContext rc, ISrvBindable depthRead, IRtvBindable target)
    {
        _ = rc;
        _ = depthRead;
        _ = target;
        return !PostPpHudPass.ShouldSkipKeenPostPp();
    }
}

[HarmonyPatch(typeof(MyRenderProxy), nameof(MyRenderProxy.AddBillboard))]
internal static class BillboardAddPatch
{
    [HarmonyPostfix]
    private static void Postfix(MyBillboard billboard) => PostPpHudPass.NoteAdd(billboard);
}

[HarmonyPatch(typeof(MyRenderProxy), nameof(MyRenderProxy.AddBillboards))]
internal static class BillboardAddRangePatch
{
    [HarmonyPostfix]
    private static void Postfix(IEnumerable<MyBillboard> billboards) => PostPpHudPass.NoteAdds(billboards);
}

[HarmonyPatch(
    typeof(MyTransparentGeometry),
    nameof(MyTransparentGeometry.ApplyActionOnPersistentBillboards),
    typeof(Action))]
internal static class BillboardFrameCompletePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            PostPpHudPass.PublishCompletedFrame();
        }
        catch (Exception e)
        {
            DebugLog.Write("BillboardFrameCompletePatch: " + e);
        }
    }
}
