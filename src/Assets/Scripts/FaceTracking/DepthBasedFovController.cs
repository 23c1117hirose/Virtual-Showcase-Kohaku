using UnityEngine;

namespace VirtualShowcase.FaceTracking.Transform
{
    public class DepthBasedFovController : MonoBehaviour
    {
        [SerializeField]
        private FaceDistanceVolumeController distanceController;

        [Header("歪みの強さの変化範囲")]
        [SerializeField]
        private float minMultiplier = 0.85f; // 遠い時(歪みが弱くなる、望遠的)

        [SerializeField]
        private float maxMultiplier = 1.3f;  // 近い時(歪みが強くなる、広角的)

        [Header("距離の範囲(cm)")]
        [SerializeField]
        private float closeDistanceCm = 30f;

        [SerializeField]
        private float farDistanceCm = 100f;

        [Header("滑らかさ")]
        [SerializeField]
        private float smoothing = 0.1f;

        private float _smoothedMultiplier = 1f;

        public float CurrentMultiplier => _smoothedMultiplier;

        private void Update()
        {
            float targetMultiplier = 1f; // 基準値(何もない時は1.0=元のまま)

            if (distanceController != null && distanceController.HasValidDepth)
            {
                float depth = distanceController.CurrentDepthCm;
                float t = Mathf.InverseLerp(closeDistanceCm, farDistanceCm, depth);
                t = Mathf.Clamp01(t);

                // 近い(t=0)ほどmaxMultiplierに、遠い(t=1)ほどminMultiplierに近づく
                targetMultiplier = Mathf.Lerp(maxMultiplier, minMultiplier, t);
            }

            _smoothedMultiplier += (targetMultiplier - _smoothedMultiplier) * smoothing;
        }
    }
}