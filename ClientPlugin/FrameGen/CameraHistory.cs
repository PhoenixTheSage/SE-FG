using System;
using VRageMath;
using VRageRender;

namespace ClientPlugin.FrameGen;

internal static class CameraHistory
{
    public static Matrix ViewProjection { get; private set; }
    public static Matrix InvViewProjection { get; private set; }
    public static Matrix PreviousViewProjection { get; private set; }
    public static bool HasPrevious { get; private set; }

    private static Vector3D _previousCameraPos;
    private static Vector3 _previousForward;
    private static float _previousFovV;
    private static bool _hasCameraSample;

    public static void Reset()
    {
        HasPrevious = false;
        _hasCameraSample = false;
        ViewProjection = default;
        InvViewProjection = default;
        PreviousViewProjection = default;
    }

    public static void BeginFrame()
    {
        PreviousViewProjection = ViewProjection;
        var env = MyRender11.Environment != null ? MyRender11.Environment.Matrices : null;
        if (env != null)
        {
            ViewProjection = env.ViewProjectionAt0;
            InvViewProjection = env.InvViewProjectionAt0;
        }
        HasPrevious = _hasCameraSample;
    }

    public static bool ConsumeCameraCut()
    {
        var env = MyRender11.Environment != null ? MyRender11.Environment.Matrices : null;
        if (env == null)
            return !HasPrevious;
        var pos = env.CameraPosition;
        var forward = env.ViewAt0.Forward;
        var fov = env.FovV;
        var cut = !HasPrevious || !_hasCameraSample;
        if (_hasCameraSample)
        {
            var dist = Vector3D.Distance(pos, _previousCameraPos);
            var align = Vector3.Dot(forward, _previousForward);
            var fovDelta = Math.Abs(fov - _previousFovV);
            if (dist > 40.0 || align < 0.82f || fovDelta > 0.04f)
                cut = true;
        }
        _previousCameraPos = pos;
        _previousForward = forward;
        _previousFovV = fov;
        _hasCameraSample = true;
        return cut;
    }

    public static void CopyToArray(Matrix matrix, float[] dest)
    {
        dest[0] = matrix.M11; dest[1] = matrix.M12; dest[2] = matrix.M13; dest[3] = matrix.M14;
        dest[4] = matrix.M21; dest[5] = matrix.M22; dest[6] = matrix.M23; dest[7] = matrix.M24;
        dest[8] = matrix.M31; dest[9] = matrix.M32; dest[10] = matrix.M33; dest[11] = matrix.M34;
        dest[12] = matrix.M41; dest[13] = matrix.M42; dest[14] = matrix.M43; dest[15] = matrix.M44;
    }
}
