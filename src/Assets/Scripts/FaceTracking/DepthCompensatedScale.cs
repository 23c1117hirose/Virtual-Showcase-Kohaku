using UnityEngine;
using VirtualShowcase.FaceTracking.Transform;

namespace VirtualShowcase.FaceTracking
{
    public class DepthCompensatedScale : MonoBehaviour
    {
        [SerializeField]
        private DepthBasedFovController fovController;

        [SerializeField]
        private UnityEngine.Transform targetObject; // ← 明示的に UnityEngine. を付ける

        [SerializeField]
        private Vector3 originalScale = Vector3.one;

        [Header("補正の強さ")]
        [SerializeField]
        private float compensationStrength = 1f;

        private void Update()
        {
            if (fovController == null || targetObject == null) return;

            float multiplier = fovController.CurrentMultiplier;
            float compensation = 1f + (multiplier - 1f) * compensationStrength;

            targetObject.localScale = originalScale * compensation;
        }
    }
}