using UnityEngine;
using VirtualShowcase.FaceTracking;
using VirtualShowcase.Utilities; // MyPrefsの名前空間

public class FaceDistanceVolumeController : MonoBehaviour
{
    public AudioSource audioSource;
    public Transform listenerReference;
    public HeadYawEstimator yawEstimator;

    [Header("前後距離(cm)によるキャリブレーション")]
    public float closeDistanceCm = 30f;
    public float farDistanceCm = 80f;

    [Header("前後距離による音量カーブ(強調用)")]
    public AnimationCurve depthVolumeCurve = AnimationCurve.Linear(0, 1, 1, 0);

    [Header("3Dユークリッド距離による、自然な減衰(緩やか)")]
    public float euclideanMinDistance = 40f;
    public float euclideanMaxDistance = 100f;
    public AnimationCurve euclideanVolumeCurve = AnimationCurve.Linear(0, 1, 1, 0.7f);

    [Header("顔サイズ基準の距離推定(対策2+3)")]
    // public float referenceFaceExtent = -1f; ← これは削除
    // public float referenceDistanceCm = 50f; ← これも削除
    public float minCosClamp = 0.3f;

    private float currentDepth = 50f;
    private float smoothedDepth = 50f;
    public float smoothing = 0.1f;

    public float CurrentDepthCm => smoothedDepth;
    public bool HasValidDepth => MyPrefs.ReferenceFaceExtent > 0f;

    void OnEnable()
    {
        Detector.FaceDetected.AddListener(OnFaceDetected);
    }

    void OnDisable()
    {
        Detector.FaceDetected.RemoveListener(OnFaceDetected);
    }

    // キャリブレーション完了時に、CalibrationControllerから呼んでもらう
    public void CalibrateFaceExtent(FaceDetection detection, float distanceCm)
    {
        MyPrefs.ReferenceFaceExtent = detection.Extent.magnitude;
        MyPrefs.ReferenceDistanceCm = distanceCm;
    }

    private void OnFaceDetected(FaceDetection detection)
    {
        float referenceFaceExtent = MyPrefs.ReferenceFaceExtent;
        if (referenceFaceExtent <= 0f) return;

        float yawAngle = yawEstimator != null ? Mathf.Abs(yawEstimator.CurrentYaw) : 0f;
        float yawRadians = yawAngle * Mathf.Deg2Rad;
        float safeCos = Mathf.Max(Mathf.Cos(yawRadians), minCosClamp);
        float correctedExtent = detection.Extent.magnitude / safeCos;

        currentDepth = referenceFaceExtent * MyPrefs.ReferenceDistanceCm / correctedExtent;
    }

    void Update()
    {
        if (audioSource == null || listenerReference == null) return;

        smoothedDepth += (currentDepth - smoothedDepth) * smoothing;
        float depthT = Mathf.InverseLerp(closeDistanceCm, farDistanceCm, smoothedDepth);
        depthT = Mathf.Clamp01(depthT);
        float depthVolume = depthVolumeCurve.Evaluate(depthT);

        float euclideanDist = Vector3.Distance(transform.position, listenerReference.position);
        float euclideanT = Mathf.InverseLerp(euclideanMinDistance, euclideanMaxDistance, euclideanDist);
        euclideanT = Mathf.Clamp01(euclideanT);
        float euclideanVolume = euclideanVolumeCurve.Evaluate(euclideanT);

        audioSource.volume = depthVolume * euclideanVolume;

        //Debug.Log($"currentDepth={currentDepth}, referenceFaceExtent={MyPrefs.ReferenceFaceExtent}, FinalVolume={audioSource.volume}");
    }
}