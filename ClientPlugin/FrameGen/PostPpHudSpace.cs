using System.Collections.Generic;
using VRageMath;
using VRageRender;

namespace ClientPlugin.FrameGen;

/// <summary>
/// Same lock SE-DLSS uses for PostPP Rich HUD: freeze quads in layout
/// view space, then parent them to the render camera at draw. Master's
/// PixelToWorld from <c>MySector.MainCamera</c> otherwise sits in last-pose
/// world space (doorway ghost) when the render camera has moved.
/// </summary>
internal static class PostPpHudSpace
{
    public static void ToViewLocal(List<MyBillboard> copies, MatrixD layoutView)
    {
        if (copies == null || copies.Count == 0 || !layoutView.IsValid())
            return;
        for (var i = 0; i < copies.Count; i++)
            TransformScreenSpace(copies[i], ref layoutView);
    }

    public static void ToWorldFromViewLocal(List<MyBillboard> copies)
    {
        if (copies == null || copies.Count == 0)
            return;
        var env = MyRender11.Environment?.Matrices;
        if (env == null)
            return;
        var renderWorld = env.InvViewD;
        if (!renderWorld.IsValid())
            return;
        for (var i = 0; i < copies.Count; i++)
            TransformScreenSpace(copies[i], ref renderWorld);
    }

    static void TransformScreenSpace(MyBillboard billboard, ref MatrixD matrix)
    {
        if (billboard == null || billboard.CustomViewProjection != -1)
            return;
        if (billboard.ParentID != uint.MaxValue)
            return;
        if (billboard.LocalType is MyBillboard.LocalTypeEnum.Line or MyBillboard.LocalTypeEnum.Point)
            return;

        Vector3D.Transform(ref billboard.Position0, ref matrix, out billboard.Position0);
        Vector3D.Transform(ref billboard.Position1, ref matrix, out billboard.Position1);
        Vector3D.Transform(ref billboard.Position2, ref matrix, out billboard.Position2);
        Vector3D.Transform(ref billboard.Position3, ref matrix, out billboard.Position3);
    }
}
