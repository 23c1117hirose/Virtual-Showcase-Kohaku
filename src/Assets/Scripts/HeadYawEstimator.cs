using UnityEngine;
using VirtualShowcase.FaceTracking;

public class HeadYawEstimator : MonoBehaviour
{
    public float yawMultiplier = 60f;
    public float smoothing = 0.1f;

    [Header("大きな首振りの暴走を抑える補正カーブ")]
    public AnimationCurve yawCorrectionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public float maxRawOffset = 1.5f; // この値でノーマライズする(要調整)

    private float smoothedYaw = 0f;
    private float calibrationOffset = 0f;
    private bool hasDetection = false;
    private float lastRawYaw = 0f;

    public float CurrentYaw => smoothedYaw;

    void OnEnable()
    {
        Detector.FaceDetected.AddListener(OnFaceDetected);
    }

    void OnDisable()
    {
        Detector.FaceDetected.RemoveListener(OnFaceDetected);
    }

    private void OnFaceDetected(FaceDetection detection)
    {
        float earMidX = (detection.LeftEar.x + detection.RightEar.x) / 2f;
        float earSpread = Mathf.Abs(detection.RightEar.x - detection.LeftEar.x);
        if (earSpread < 0.001f) return;

        float noseOffset = (detection.Nose.x - earMidX) / earSpread;

        // 生の比率を正規化し、カーブで頭打ちにする
        float normalized = Mathf.Clamp01(Mathf.Abs(noseOffset) / maxRawOffset);
        float correctedMagnitude = yawCorrectionCurve.Evaluate(normalized);
        lastRawYaw = Mathf.Sign(noseOffset) * correctedMagnitude * yawMultiplier;

        float calibratedYaw = lastRawYaw - calibrationOffset;
        smoothedYaw += (calibratedYaw - smoothedYaw) * smoothing;
        hasDetection = true;
    }

    public void CalibrateToFront()
    {
        calibrationOffset = lastRawYaw;
    }

    void Update()
    {
        if (!hasDetection) return;
        smoothedYaw = Mathf.Clamp(smoothedYaw, -70f, 70f); // 安全のための最終クランプ
        transform.localRotation = Quaternion.Euler(0f, smoothedYaw, 0f);
    }
}