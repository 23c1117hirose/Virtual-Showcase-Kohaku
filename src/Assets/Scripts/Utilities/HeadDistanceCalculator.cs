// 新規ファイル: Assets/Scripts/Utilities/HeadDistanceCalculator.cs
using UnityEngine;
using VirtualShowcase.FaceTracking;
using VirtualShowcase.Utilities;

public static class HeadDistanceCalculator
{
    public static float GetCorrectedDistance(FaceDetection detection, float yawAngleDegrees, float minCosClamp = 0.3f)
    {
        float referenceFaceExtent = MyPrefs.ReferenceFaceExtent;
        if (referenceFaceExtent <= 0f) return -1f; // 未キャリブレーション

        float yawRadians = Mathf.Abs(yawAngleDegrees) * Mathf.Deg2Rad;
        float safeCos = Mathf.Max(Mathf.Cos(yawRadians), minCosClamp);
        float correctedExtent = detection.Extent.magnitude / safeCos;

        return referenceFaceExtent * MyPrefs.ReferenceDistanceCm / correctedExtent;
    }
}