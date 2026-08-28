using System.Collections;
using System.Collections.Generic;
using Leap;
using Leap.Unity;
using TMPro;
using UnityEngine;
using VirtualShowcase.Common;
using VirtualShowcase.Core;
using VirtualShowcase.Enums;
using VirtualShowcase.Utilities;

namespace VirtualShowcase.Showcase
{
    /// <summary>
    ///     Aligns the Leap Motion sensor space with the virtual scene by point correspondence.
    ///     The user touches three known points with the index fingertip; from those pairs the
    ///     sensor's rotation and position are solved directly (the scale is known analytically,
    ///     see <see cref="CurrentScale" />).
    /// </summary>
    public class LeapCalibration : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Scene objects")]
        [SerializeField]
        private UnityEngine.Transform leapProviderTransform;

        [SerializeField]
        private LeapProvider leapProvider;

        [Tooltip("Three non-collinear markers, placed on (or near) the projection plane.")]
        [SerializeField]
        private UnityEngine.Transform[] targetPoints;

        [SerializeField]
        private TMP_Text guideText;

        [Header("Hand materials")]
        [Tooltip("Renderers of the virtual hand models.")]
        [SerializeField]
        private Renderer[] handRenderers;

        [Tooltip("Black, used during normal playback so the hand only acts as an occlusion mask.")]
        [SerializeField]
        private Material occlusionMaterial;

        [Tooltip("Bright colour, used only as an optional manual sanity-check outside of calibration.")]
        [SerializeField]
        private Material calibrationMaterial;

        [Header("Target marker feedback")]
        [SerializeField]
        private Color targetIdleColor = Color.white;

        [SerializeField]
        private Color targetSamplingColor = Color.yellow;

        [SerializeField]
        private Color targetCapturedColor = Color.green;

        [Tooltip("How long the marker stays green after a point is captured, before moving on.")]
        [SerializeField]
        private float capturedFlashSeconds = 0.3f;

        [Header("Settings")]
        [SerializeField]
        private KeyCode toggleKey = KeyCode.L;

        [SerializeField]
        private KeyCode recordKey = KeyCode.Space;

        [Tooltip("Manually swap the hand between the occlusion (black) and bright material. Only works outside of calibration.")]
        [SerializeField]
        private KeyCode handMaterialToggleKey = KeyCode.K;

        [Tooltip("How long the fingertip is averaged when recording a point, to cancel out tracking jitter.")]
        [SerializeField]
        private float sampleSeconds = 0.5f;

        #endregion

        private readonly List<Vector3> _recordedSensorPoints = new List<Vector3>();

        private LeapCalibrationState _state = LeapCalibrationState.Off;
        private bool _isSampling;
        private bool _handsBright;

        public bool Enabled => _state != LeapCalibrationState.Off;

        /// <summary>
        ///     Unity units per Leap Motion meter.
        ///     One Unity unit is one centimeter (see Projection.DiagonalToWidthAndHeight),
        ///     so a meter is 100 units. The scene is always built for a
        ///     SCREEN_BASE_DIAGONAL_INCHES display, and a differently sized physical display is
        ///     compensated by the same ratio Projection.SetCameraDistance uses.
        /// </summary>
        private static float CurrentScale =>
            100f * ((float)Constants.SCREEN_BASE_DIAGONAL_INCHES / MyPrefs.ScreenSize);

        #region Event Functions

        private void OnEnable()
        {
            MyEvents.ScreenSizeChanged.AddListener((sender, size) => ApplyScale());
        }

        private void Start()
        {
            ApplyScale();
            LoadCalibration();
            SetTargetsVisible(false);
            SetHandMaterial(occlusionMaterial);
        }

        private void Update()
        {
            if (_state is LeapCalibrationState.Off or LeapCalibrationState.Done)
            {
                if (Input.GetKeyDown(toggleKey))
                {
                    StartCalibration();
                    return;
                }

                // Only allowed while not actively calibrating, so it can't be confused
                // with the (hidden) hand during point recording.
                if (Input.GetKeyDown(handMaterialToggleKey))
                {
                    _handsBright = !_handsBright;
                    SetHandMaterial(_handsBright ? calibrationMaterial : occlusionMaterial);
                }

                return;
            }

            if (Input.GetKeyDown(toggleKey))
            {
                StopCalibration();
                return;
            }

            if (!_isSampling && Input.GetKeyDown(recordKey))
            {
                StartCoroutine(RecordPoint());
            }
        }

        #endregion

        public void ToggleCalibration()
        {
            if (Enabled)
            {
                StopCalibration();
            }
            else
            {
                StartCalibration();
            }
        }

        public void StartCalibration()
        {
            if (leapProviderTransform == null || leapProvider == null)
            {
                Debug.LogWarning("[LeapCalibration] Provider is not assigned.");
                return;
            }

            if (targetPoints == null || targetPoints.Length < 3)
            {
                Debug.LogWarning("[LeapCalibration] Three target points are required.");
                return;
            }

            _recordedSensorPoints.Clear();
            _state = LeapCalibrationState.Point1;

            // Hide the (still uncalibrated) hand entirely while lining up the real fingertip
            // with the markers, instead of showing it in a colour: an unaligned virtual hand
            // right next to the target would only make the real fingertip harder to see.
            SetHandsVisible(false);
            UpdateGuideText();
        }

        public void StopCalibration()
        {
            _state = LeapCalibrationState.Off;
            _isSampling = false;

            SetTargetsVisible(false);
            SetHandsVisible(true);
            SetGuideText(string.Empty);
        }

        private void LoadCalibration()
        {
            if (leapProviderTransform == null || !MyPrefs.LeapCalibrated)
            {
                return;
            }

            leapProviderTransform.SetPositionAndRotation(MyPrefs.LeapPosition, MyPrefs.LeapRotation);
        }

        private void SaveCalibration()
        {
            MyPrefs.LeapPosition = leapProviderTransform.position;
            MyPrefs.LeapRotation = leapProviderTransform.rotation;
            MyPrefs.LeapCalibrated = true;
        }

        /// <summary>
        ///     Keeps the sensor scale in sync with the configured display size.
        ///     Position and rotation are not touched, they come from the point calibration.
        /// </summary>
        private void ApplyScale()
        {
            if (leapProviderTransform == null)
            {
                return;
            }

            leapProviderTransform.localScale = Vector3.one * CurrentScale;
        }

        /// <summary>
        ///     Averages the index fingertip over <see cref="sampleSeconds" /> and stores it in sensor space.
        /// </summary>
        private IEnumerator RecordPoint()
        {
            _isSampling = true;
            Renderer targetRenderer = GetCurrentTargetRenderer();
            SetTargetColor(targetRenderer, targetSamplingColor);
            SetGuideText("Hold still...");

            var sum = Vector3.zero;
            var samples = 0;
            float elapsed = 0f;

            while (elapsed < sampleSeconds)
            {
                if (TryGetIndexTipInSensorSpace(out Vector3 tip))
                {
                    sum += tip;
                    samples++;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            _isSampling = false;

            if (samples == 0)
            {
                SetTargetColor(targetRenderer, targetIdleColor);
                SetGuideText("No hand was tracked, try again.");
                yield break;
            }

            _recordedSensorPoints.Add(sum / samples);
            SetTargetColor(targetRenderer, targetCapturedColor);

            // Brief green flash so the user gets clear confirmation the point was captured.
            yield return new WaitForSeconds(capturedFlashSeconds);

            if (_recordedSensorPoints.Count >= 3)
            {
                Solve();
            }
            else
            {
                _state = _state.Next();
                UpdateGuideText();
            }
        }

        private Renderer GetCurrentTargetRenderer()
        {
            var index = (int)_state - 1; // Point1 is 1
            if (index < 0 || index >= targetPoints.Length || targetPoints[index] == null)
            {
                return null;
            }

            return targetPoints[index].GetComponentInChildren<Renderer>();
        }

        /// <returns>Whether a hand was tracked. The tip is returned in sensor space (meters).</returns>
        private bool TryGetIndexTipInSensorSpace(out Vector3 tip)
        {
            tip = Vector3.zero;
            List<Hand> hands = leapProvider.CurrentFrame.Hands;
            if (hands.Count == 0)
            {
                return false;
            }

            // CurrentFrame is already in world space, so undo the provider transform to get
            // the raw sensor reading back.
            Vector3 worldTip = hands[0].Fingers[1].TipPosition;
            tip = leapProviderTransform.InverseTransformPoint(worldTip);
            return true;
        }

        /// <summary>
        ///     Solves world = rotation * (sensor * scale) + position from three point pairs.
        ///     Building an orthonormal frame out of each triangle gives the exact rotation,
        ///     so no least squares fit is needed for three points.
        /// </summary>
        private void Solve()
        {
            Vector3 p0 = _recordedSensorPoints[0];
            Vector3 p1 = _recordedSensorPoints[1];
            Vector3 p2 = _recordedSensorPoints[2];

            Vector3 q0 = targetPoints[0].position;
            Vector3 q1 = targetPoints[1].position;
            Vector3 q2 = targetPoints[2].position;

            if (!TryGetTriangleFrame(p0, p1, p2, out Quaternion sensorFrame) ||
                !TryGetTriangleFrame(q0, q1, q2, out Quaternion worldFrame))
            {
                SetGuideText("The three points are too close to a line, start over.");
                _recordedSensorPoints.Clear();
                _state = LeapCalibrationState.Point1;
                return;
            }

            Quaternion rotation = worldFrame * Quaternion.Inverse(sensorFrame);

            float scale = CurrentScale;
            Vector3 sensorCentroid = (p0 + p1 + p2) / 3f;
            Vector3 worldCentroid = (q0 + q1 + q2) / 3f;
            Vector3 position = worldCentroid - rotation * (sensorCentroid * scale);

            leapProviderTransform.SetPositionAndRotation(position, rotation);
            leapProviderTransform.localScale = Vector3.one * scale;

            SaveCalibration();

            _state = LeapCalibrationState.Done;
            SetTargetsVisible(false);
            SetHandsVisible(true);
            SetGuideText("Calibration saved.");
            Debug.Log($"[LeapCalibration] position={position}, rotation={rotation.eulerAngles}, scale={scale}");
        }

        /// <returns>Whether the points are non-collinear.</returns>
        private static bool TryGetTriangleFrame(Vector3 a, Vector3 b, Vector3 c, out Quaternion frame)
        {
            frame = Quaternion.identity;

            Vector3 forward = b - a;
            Vector3 side = c - a;
            Vector3 up = Vector3.Cross(forward, side);

            if (forward.sqrMagnitude < Mathf.Epsilon || up.sqrMagnitude < Mathf.Epsilon)
            {
                return false;
            }

            frame = Quaternion.LookRotation(forward.normalized, up.normalized);
            return true;
        }

        private void SetTargetsVisible(bool visible)
        {
            foreach (UnityEngine.Transform target in targetPoints)
            {
                if (target != null)
                {
                    target.gameObject.SetActive(visible);
                }
            }
        }

        private void SetHandMaterial(Material material)
        {
            if (material == null || handRenderers == null)
            {
                return;
            }

            foreach (Renderer handRenderer in handRenderers)
            {
                if (handRenderer != null)
                {
                    handRenderer.sharedMaterial = material;
                }
            }
        }

        private void SetHandsVisible(bool visible)
        {
            if (handRenderers == null)
            {
                return;
            }

            foreach (Renderer handRenderer in handRenderers)
            {
                if (handRenderer != null)
                {
                    handRenderer.enabled = visible;
                }
            }
        }

        /// <summary>
        ///     Uses .material (an auto-instanced copy) rather than .sharedMaterial, so that
        ///     recolouring one marker doesn't affect the other two.
        /// </summary>
        private static void SetTargetColor(Renderer targetRenderer, Color color)
        {
            if (targetRenderer != null)
            {
                targetRenderer.material.color = color;
            }
        }

        private void UpdateGuideText()
        {
            var index = (int)_state; // Point1 is 1
            SetGuideText($"Touch marker {index} of 3 with your index fingertip and press '{recordKey}'");

            for (var i = 0; i < targetPoints.Length; i++)
            {
                if (targetPoints[i] == null)
                {
                    continue;
                }

                bool isCurrent = i == index - 1;
                targetPoints[i].gameObject.SetActive(isCurrent);

                if (isCurrent)
                {
                    SetTargetColor(targetPoints[i].GetComponentInChildren<Renderer>(), targetIdleColor);
                }
            }
        }

        private void SetGuideText(string text)
        {
            if (guideText != null)
            {
                guideText.text = text;
            }
        }
    }
}
