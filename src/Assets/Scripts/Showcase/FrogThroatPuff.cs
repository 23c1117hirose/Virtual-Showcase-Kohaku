using System.Collections;
using Deform;
using UnityEngine;

namespace VirtualShowcase.Showcase
{
    /// <summary>
    ///     Pushes the frog's throat area outward whenever <see cref="FrogVocalizer" /> starts a croak,
    ///     using a rise-hold-fall envelope so it reads as a vocal sac inflating.
    ///     Works the same way for every clip in FrogVocalizer.croakClips, no per-clip authoring needed.
    /// </summary>
    public class FrogThroatPuff : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Scene objects")]
        [Tooltip("A RadialPushDeformer whose Axis/Radius is positioned and sized to cover just the throat.")]
        [SerializeField]
        private RadialPushDeformer throatPush;

        [SerializeField]
        private FrogVocalizer vocalizer;

        [Header("Envelope")]
        [Tooltip("Maximum bulge amount.")]
        [SerializeField]
        private float peakFactor = 0.3f;

        [Tooltip("Seconds to reach peak inflation.")]
        [SerializeField]
        private float attackSeconds = 0.15f;

        [Tooltip("Seconds to hold near peak before releasing.")]
        [SerializeField]
        private float holdSeconds = 0.2f;

        [Tooltip("Seconds to deflate back to 0.")]
        [SerializeField]
        private float releaseSeconds = 0.25f;

        #endregion

        private Coroutine _puffRoutine;

        #region Event Functions

        private void OnEnable()
        {
            if (vocalizer != null)
            {
                vocalizer.OnCroak += HandleCroak;
            }
        }

        private void OnDisable()
        {
            if (vocalizer != null)
            {
                vocalizer.OnCroak -= HandleCroak;
            }
        }

        #endregion

        private void HandleCroak(AudioClip clip)
        {
            if (throatPush == null)
            {
                return;
            }

            if (_puffRoutine != null)
            {
                StopCoroutine(_puffRoutine);
            }

            _puffRoutine = StartCoroutine(PuffEnvelope());
        }

        private IEnumerator PuffEnvelope()
        {
            yield return Animate(throatPush.Factor, peakFactor, attackSeconds);
            yield return new WaitForSeconds(holdSeconds);
            yield return Animate(throatPush.Factor, 0f, releaseSeconds);
            _puffRoutine = null;
        }

        private IEnumerator Animate(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                throatPush.Factor = to;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                throatPush.Factor = Mathf.Lerp(from, to, elapsed / duration);
                elapsed += Time.deltaTime;
                yield return null;
            }

            throatPush.Factor = to;
        }
    }
}
