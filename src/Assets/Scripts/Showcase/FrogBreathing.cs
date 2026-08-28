using Deform;
using UnityEngine;

namespace VirtualShowcase.Showcase
{
    /// <summary>
    ///     Drives a RadialPushDeformer positioned at the belly with a continuous breathing cycle:
    ///     a quick inhale (fast rise) followed by a slow exhale (slow fall back to the resting shape).
    ///     Unlike a symmetric sine wave, the mesh never dips below its resting shape, and because
    ///     RadialPushDeformer only affects vertices within its Radius, unrelated body parts are untouched.
    /// </summary>
    public class FrogBreathing : MonoBehaviour
    {
        #region Serialized Fields

        [Tooltip("A RadialPushDeformer whose Axis/Radius is positioned and sized to cover just the belly.")]
        [SerializeField]
        private RadialPushDeformer bellyPush;

        [Tooltip("Maximum outward displacement, in scene units (cm).")]
        [SerializeField]
        private float peakFactor = 0.3f;

        [Tooltip("Seconds for the quick inhale (rise to peak).")]
        [SerializeField]
        private float riseSeconds = 0.4f;

        [Tooltip("Seconds for the slow exhale (fall back to flat).")]
        [SerializeField]
        private float fallSeconds = 2.5f;

        [Tooltip("Pause at rest before the next breath starts.")]
        [SerializeField]
        private float restSeconds = 0.5f;

        #endregion

        private float _cycleTime;

        #region Event Functions

        private void Update()
        {
            if (bellyPush == null)
            {
                return;
            }

            float cycleLength = riseSeconds + fallSeconds + restSeconds;
            _cycleTime += Time.deltaTime;
            if (_cycleTime > cycleLength)
            {
                _cycleTime -= cycleLength;
            }

            float factor;
            if (_cycleTime < riseSeconds)
            {
                factor = Mathf.SmoothStep(0f, peakFactor, _cycleTime / riseSeconds);
            }
            else if (_cycleTime < riseSeconds + fallSeconds)
            {
                factor = Mathf.SmoothStep(peakFactor, 0f, (_cycleTime - riseSeconds) / fallSeconds);
            }
            else
            {
                factor = 0f;
            }

            bellyPush.Factor = factor;
        }

        #endregion
    }
}
