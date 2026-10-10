using System.Collections;
using System.Collections.Generic;
using Leap;
using Leap.Unity;
using UnityEngine;
using VirtualShowcase.FaceTracking;

namespace VirtualShowcase.Showcase
{
    /// <summary>
    ///     Evaluation task for the user study ("poke task").
    ///     For every trial a croak is played from one tall-grass clump (spatial audio cue), a few seconds
    ///     later the frog hops out of that clump onto bare ground, and the participant gently pokes it
    ///     with the index fingertip. After a poke (or a timeout) the frog turns around and hops back into
    ///     the grass. The same single frog object is moved between the clumps, so the existing
    ///     <see cref="FrogTouchController" /> (touch deformation, touch sound) is reused as is.
    ///     Trial sequences are generated from a fixed seed, so a given sequenceId is identical in every
    ///     condition. Per-trial results are written by <see cref="PokeTaskLogger" />.
    /// </summary>
    public class PokeTaskManager : MonoBehaviour
    {
        /// <summary>What the task is doing right now (for the experimenter's monitor).</summary>
        public enum TaskState
        {
            Idle,
            Starting,
            Cue,
            Hopping,
            Waiting,
            Fleeing,
            Interval
        }

        private struct TrialSpec
        {
            public int PatchIndex;
            public int ClipIndex;
            public float Interval;
        }

        #region Serialized Fields

        [Header("Scene objects")]
        [SerializeField]
        private GrassPatch[] patches;

        [Tooltip("Ground, grass and everything else of the task field. Only shown while the task runs.")]
        [SerializeField]
        private GameObject fieldRoot;

        [Tooltip("Controller on the frog root. The frog root (this controller's transform) is what gets moved.")]
        [SerializeField]
        private FrogTouchController frogTouch;

        [Tooltip("Its random croaking is suppressed while the task runs.")]
        [SerializeField]
        private FrogVocalizer vocalizer;

        [SerializeField]
        private LeapProvider leapProvider;

        [Header("Session")]
        [SerializeField]
        private string participantId = "P00";

        [Tooltip("Free label written to the log, e.g. ProposedMR or HMD.")]
        [SerializeField]
        private string conditionLabel = "ProposedMR";

        [Tooltip("1 or 2. The same id always gives the same trial sequence (use different ids per condition).")]
        [SerializeField]
        private int sequenceId = 1;

        [SerializeField]
        private int baseSeed = 20261000;

        [Header("Trials")]
        [SerializeField]
        private int practiceTrials = 6;

        [Tooltip("Main trials = patches x this value, each patch equally often.")]
        [SerializeField]
        private int trialsPerPatch = 4;

        [Header("Timing (seconds)")]
        [SerializeField]
        private float startDelay = 3f;

        [Tooltip("From the start of the cue sound to the frog starting to hop out.")]
        [SerializeField]
        private float cueToAppearSeconds = 2.5f;

        [Tooltip("How long the frog stays on the ground (after landing) before it counts as a miss. " +
                 "Long on purpose: this is not a speed contest.")]
        [SerializeField]
        private float visibleSeconds = 3.5f;

        [Tooltip("How long the frog stays after being touched (so the touch reaction is visible).")]
        [SerializeField]
        private float postHitSeconds = 0.6f;

        [SerializeField]
        private Vector2 intervalRange = new Vector2(1.5f, 2.5f);

        [Tooltip("A touch sooner than this after landing is flagged (finger was already waiting on the spot).")]
        [SerializeField]
        private float earlyTouchSeconds = 0.15f;

        [Header("Frog")]
        [Tooltip("Frog size during the task relative to its size in the scene (0.5 = half).")]
        [SerializeField]
        private float frogScale = 0.5f;

        [Tooltip("Flight time of one hop (out of the grass, and back into it).")]
        [SerializeField]
        private float hopSeconds = 0.4f;

        [Tooltip("Height of the hop arc (scene units, cm).")]
        [SerializeField]
        private float hopHeight = 3f;

        [Tooltip("Time the frog takes to turn around before it flees.")]
        [SerializeField]
        private float turnSeconds = 0.15f;

        [Header("Hop motion (squash and stretch)")]
        [Tooltip("The frog crouches this long before it jumps out (anticipation).")]
        [SerializeField]
        private float crouchSeconds = 0.15f;

        [Tooltip("Crouch before the escape hop (shorter, the frog is already on its way).")]
        [SerializeField]
        private float fleeCrouchSeconds = 0.1f;

        [Tooltip("Time the landing takes to settle (squash and bounce). The frog can be poked after this.")]
        [SerializeField]
        private float settleSeconds = 0.25f;

        [Tooltip("Body height while crouching (1 = normal, 0.78 = squashed to 78 %).")]
        [Range(0.5f, 1f)]
        [SerializeField]
        private float crouchHeight = 0.78f;

        [Tooltip("Body height at take-off (stretched, above 1).")]
        [Range(1f, 1.4f)]
        [SerializeField]
        private float stretchHeight = 1.15f;

        [Tooltip("Body height at the moment of landing (squashed).")]
        [Range(0.5f, 1f)]
        [SerializeField]
        private float landSquashHeight = 0.72f;

        [Tooltip("Nose-up tilt at take-off that turns into nose-down at landing, following the flight path (degrees).")]
        [SerializeField]
        private float flightPitchDegrees = 22f;

        [Tooltip("Nose-up tilt while crouching (degrees).")]
        [SerializeField]
        private float crouchPitchDegrees = 8f;

        [Header("Landing sound")]
        [SerializeField]
        private bool playLandingSound = true;

        [Tooltip("Leave empty to use a small generated thump.")]
        [SerializeField]
        private AudioClip landingClip;

        [Range(0f, 1f)]
        [SerializeField]
        private float landingVolume = 0.5f;

        [Header("Cue sound")]
        [Tooltip("Leave empty to reuse the FrogVocalizer's croak clips.")]
        [SerializeField]
        private AudioClip[] cueClips;

        [Header("Keys (experimenter)")]
        [SerializeField]
        private KeyCode startPracticeKey = KeyCode.F5;

        [SerializeField]
        private KeyCode startMainKey = KeyCode.F6;

        [SerializeField]
        private KeyCode abortKey = KeyCode.F7;

        #endregion

        private readonly List<Vector3> _tipBuffer = new List<Vector3>();

        private Coroutine _taskRoutine;
        private PokeTaskLogger _logger;

        private UnityEngine.Transform _frogRoot;
        private Renderer[] _frogRenderers;
        private bool[] _frogRendererStates;
        private Vector3 _frogStartPosition;
        private Quaternion _frogStartRotation;
        private Vector3 _frogBaseScale;
        private bool _frogTouchWasEnabled;
        private DepthCompensatedScale _depthScale;
        private bool _depthScaleWasEnabled;
        private bool _stateSaved;

        // Distance from the frog root to the lowest point of the frog, in the root's own units.
        private float _footLocal;
        private Vector3 _taskScale;

        private bool _awaitingTouch;
        private bool _hit;
        private float _hitTime;
        private float _hitError;

        private float _reactionSum;
        private int _reactionCount;
        private AudioClip _generatedThump;

        public bool IsRunning => _taskRoutine != null;

        #region Progress (read by the experimenter's monitor window)

        public TaskState State { get; private set; } = TaskState.Idle;
        public bool IsPracticeRun { get; private set; }
        public int TrialNumber { get; private set; }
        public int TrialCount { get; private set; }
        public int HitCount { get; private set; }
        public int MissCount { get; private set; }
        public int LastPatch { get; private set; } = -1;
        public bool LastWasHit { get; private set; }
        public float LastReaction { get; private set; } = float.NaN;
        public float MeanReaction => _reactionCount > 0 ? _reactionSum / _reactionCount : float.NaN;
        public string LogPath => _logger != null ? _logger.FilePath : string.Empty;
        public string ParticipantId => participantId;
        public string ConditionLabel => conditionLabel;
        public int SequenceId => sequenceId;

        #endregion

        #region Event Functions

        private void OnEnable()
        {
            if (frogTouch != null)
            {
                frogTouch.OnTouchStarted += HandleFrogTouched;
            }
        }

        private void Start()
        {
            // The field is only visible while the task runs, so it does not get in the way
            // of calibration and free observation.
            if (fieldRoot != null)
            {
                fieldRoot.SetActive(false);
            }
        }

        private void OnDisable()
        {
            if (frogTouch != null)
            {
                frogTouch.OnTouchStarted -= HandleFrogTouched;
            }

            if (IsRunning)
            {
                StopTask();
            }
        }

        private void Update()
        {
            if (IsRunning)
            {
                if (Input.GetKeyDown(abortKey))
                {
                    Debug.Log("[PokeTask] Aborted.");
                    StopTask();
                }

                return;
            }

            if (Input.GetKeyDown(startPracticeKey))
            {
                StartTask(true);
            }
            else if (Input.GetKeyDown(startMainKey))
            {
                StartTask(false);
            }
        }

        #endregion

        public void StartTask(bool practice)
        {
            if (IsRunning)
            {
                return;
            }

            if (!Validate())
            {
                return;
            }

            _taskRoutine = StartCoroutine(RunTask(practice));
        }

        public void StopTask()
        {
            if (_taskRoutine != null)
            {
                StopCoroutine(_taskRoutine);
                _taskRoutine = null;
            }

            _awaitingTouch = false;
            EndTask();
        }

        private bool Validate()
        {
            if (patches == null || patches.Length < 2)
            {
                Debug.LogWarning("[PokeTask] At least two GrassPatch objects are required.");
                return false;
            }

            foreach (GrassPatch patch in patches)
            {
                if (patch == null)
                {
                    Debug.LogWarning("[PokeTask] The patch list contains an empty slot.");
                    return false;
                }
            }

            if (frogTouch == null)
            {
                Debug.LogWarning("[PokeTask] FrogTouchController is not assigned.");
                return false;
            }

            if (GetCueClips().Length == 0)
            {
                Debug.LogWarning("[PokeTask] No cue clips (set Cue Clips or the vocalizer's croak clips).");
                return false;
            }

            return true;
        }

        private AudioClip[] GetCueClips()
        {
            if (cueClips != null && cueClips.Length > 0)
            {
                return cueClips;
            }

            return vocalizer != null && vocalizer.croakClips != null ? vocalizer.croakClips : new AudioClip[0];
        }

        #region Task flow

        private IEnumerator RunTask(bool practice)
        {
            PrepareFrog();
            _logger = new PokeTaskLogger(participantId, conditionLabel);

            List<TrialSpec> trials = BuildTrials(practice);

            IsPracticeRun = practice;
            TrialCount = trials.Count;
            TrialNumber = 0;
            HitCount = 0;
            MissCount = 0;
            LastPatch = -1;
            LastWasHit = false;
            LastReaction = float.NaN;
            _reactionSum = 0f;
            _reactionCount = 0;
            State = TaskState.Starting;

            string phase = practice ? "practice" : "main";
            Debug.Log($"[PokeTask] Start {phase}: {trials.Count} trials, participant={participantId}, " +
                      $"condition={conditionLabel}, sequence={sequenceId}, log={_logger.FilePath}");

            yield return new WaitForSeconds(startDelay);

            for (var i = 0; i < trials.Count; i++)
            {
                yield return RunTrial(trials[i], i + 1, practice);
                State = TaskState.Interval;
                yield return new WaitForSeconds(trials[i].Interval);
            }

            Debug.Log($"[PokeTask] Finished {phase}.");
            _taskRoutine = null;
            EndTask();
        }

        private IEnumerator RunTrial(TrialSpec spec, int trialNumber, bool practice)
        {
            GrassPatch patch = patches[spec.PatchIndex];
            AudioClip[] clips = GetCueClips();
            AudioClip clip = clips[spec.ClipIndex % clips.Length];

            var record = new PokeTrialRecord
            {
                Participant = participantId,
                Condition = conditionLabel,
                Sequence = sequenceId,
                IsPractice = practice,
                Trial = trialNumber,
                Patch = spec.PatchIndex,
                Clip = spec.ClipIndex
            };

            TrialNumber = trialNumber;
            LastPatch = spec.PatchIndex;
            State = TaskState.Cue;

            // 1. Cue: the sound comes from the clump, the frog is still hidden.
            float cueStart = Time.realtimeSinceStartup;
            patch.PlayCue(clip);
            record.TipToPatchAtCue = NearestTipDistance(patch.Center);
            record.TipToPatchMinBeforeAppear = record.TipToPatchAtCue;

            while (Time.realtimeSinceStartup - cueStart < cueToAppearSeconds)
            {
                float distance = NearestTipDistance(patch.Center);
                if (!float.IsNaN(distance) &&
                    (float.IsNaN(record.TipToPatchMinBeforeAppear) || distance < record.TipToPatchMinBeforeAppear))
                {
                    record.TipToPatchMinBeforeAppear = distance;
                }

                yield return null;
            }

            // 2. The frog hops out of the grass onto the bare ground. It cannot be touched while it flies.
            record.TipToPatchAtAppear = NearestTipDistance(patch.Center);

            _hit = false;
            _awaitingTouch = false;
            State = TaskState.Hopping;
            ApplyFrogPose(patch, 0f, 0f);
            SetFrogVisible(true);
            float appearTime = Time.realtimeSinceStartup;
            yield return HopSequence(patch, 0f, 1f, 0f, crouchSeconds, true);

            // 3. Landed and settled: from now on it can be poked (reaction times start here).
            float landingTime = Time.realtimeSinceStartup;
            State = TaskState.Waiting;
            _awaitingTouch = true;
            SetFrogTouchable(true);

            // A finger already resting in another clump at this moment is not counted as an entry.
            var inside = new bool[patches.Length];
            UpdateInsideStates(inside, spec.PatchIndex);

            var frames = 0;
            var trackedFrames = 0;
            var wrongEntries = 0;

            while (!_hit && Time.realtimeSinceStartup - landingTime < visibleSeconds)
            {
                frames++;
                wrongEntries += UpdateInsideStates(inside, spec.PatchIndex);
                if (_tipBuffer.Count > 0)
                {
                    trackedFrames++;
                }

                yield return null;
            }

            _awaitingTouch = false;

            record.Hit = _hit;
            record.WrongPatchEntries = wrongEntries;
            record.HandTrackedRatio = frames > 0 ? (float)trackedFrames / frames : float.NaN;

            if (_hit)
            {
                record.ReactionFromAppear = _hitTime - appearTime;
                record.ReactionFromLanding = _hitTime - landingTime;
                record.ReactionFromCue = _hitTime - cueStart;
                record.HitError = _hitError;
                record.EarlyTouch = record.ReactionFromLanding < earlyTouchSeconds;
            }

            // 4. Flee: turn around and hop back into the grass.
            if (_hit)
            {
                yield return new WaitForSeconds(postHitSeconds);
            }

            State = TaskState.Fleeing;
            SetFrogTouchable(false);
            yield return TurnFrog(patch, 1f, turnSeconds);
            yield return HopSequence(patch, 1f, 0f, 180f, fleeCrouchSeconds, false);
            SetFrogVisible(false);

            LastWasHit = record.Hit;
            if (record.Hit)
            {
                HitCount++;
                LastReaction = record.ReactionFromLanding;
                _reactionSum += record.ReactionFromLanding;
                _reactionCount++;
            }
            else
            {
                MissCount++;
            }

            _logger.Write(record);
            Debug.Log($"[PokeTask] {(practice ? "practice" : "main")} {trialNumber}: patch={spec.PatchIndex} " +
                      $"{(record.Hit ? $"hit {record.ReactionFromLanding:F2}s after landing" : "miss")}");
        }

        #endregion

        #region Trial sequence

        private List<TrialSpec> BuildTrials(bool practice)
        {
            int seed = baseSeed + sequenceId + (practice ? 1000 : 0);
            var rng = new System.Random(seed);

            int count = practice ? practiceTrials : patches.Length * trialsPerPatch;
            List<int> order = BuildPatchOrder(rng, count);
            int clipCount = GetCueClips().Length;

            var trials = new List<TrialSpec>(count);
            for (var i = 0; i < count; i++)
            {
                trials.Add(new TrialSpec
                {
                    PatchIndex = order[i],
                    ClipIndex = rng.Next(clipCount),
                    Interval = Mathf.Lerp(intervalRange.x, intervalRange.y, (float)rng.NextDouble())
                });
            }

            return trials;
        }

        /// <summary>
        ///     Shuffled blocks that contain every patch once, so all patches appear equally often,
        ///     and the same patch never appears twice in a row.
        /// </summary>
        private List<int> BuildPatchOrder(System.Random rng, int count)
        {
            var order = new List<int>(count);
            var block = new List<int>(patches.Length);

            while (order.Count < count)
            {
                block.Clear();
                for (var i = 0; i < patches.Length; i++)
                {
                    block.Add(i);
                }

                for (int i = block.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (block[i], block[j]) = (block[j], block[i]);
                }

                if (order.Count > 0 && block.Count > 1 && block[0] == order[order.Count - 1])
                {
                    (block[0], block[block.Count - 1]) = (block[block.Count - 1], block[0]);
                }

                foreach (int patchIndex in block)
                {
                    if (order.Count < count)
                    {
                        order.Add(patchIndex);
                    }
                }
            }

            return order;
        }

        #endregion

        #region Touch handling

        private void HandleFrogTouched(Vector3 hitPoint)
        {
            if (!_awaitingTouch)
            {
                return;
            }

            _awaitingTouch = false;
            _hit = true;
            _hitTime = Time.realtimeSinceStartup;
            _hitError = Vector3.Distance(hitPoint, GetFrogCenter());
        }

        private Vector3 GetFrogCenter()
        {
            var hasBounds = false;
            var bounds = new Bounds();

            foreach (Renderer frogRenderer in _frogRenderers)
            {
                if (frogRenderer == null || !frogRenderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = frogRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(frogRenderer.bounds);
                }
            }

            return hasBounds ? bounds.center : _frogRoot.position;
        }

        #endregion

        #region Fingertips

        /// <returns>Number of tracked hands. Index fingertips are stored in the tip buffer (world space).</returns>
        private int CollectTips()
        {
            _tipBuffer.Clear();
            if (leapProvider == null)
            {
                return 0;
            }

            List<Hand> hands = leapProvider.CurrentFrame.Hands;
            foreach (Hand hand in hands)
            {
                _tipBuffer.Add(hand.Fingers[1].TipPosition);
            }

            return hands.Count;
        }

        /// <returns>Distance in scene units (cm) from the nearest index fingertip, or NaN with no hand.</returns>
        private float NearestTipDistance(Vector3 point)
        {
            if (CollectTips() == 0)
            {
                return float.NaN;
            }

            var best = float.MaxValue;
            foreach (Vector3 tip in _tipBuffer)
            {
                best = Mathf.Min(best, Vector3.Distance(tip, point));
            }

            return best;
        }

        /// <summary>
        ///     Tracks which non-target clumps a fingertip is currently inside.
        ///     Refreshes the tip buffer as a side effect.
        /// </summary>
        /// <returns>Number of outside-to-inside transitions since the last call.</returns>
        private int UpdateInsideStates(bool[] inside, int targetIndex)
        {
            CollectTips();

            var entries = 0;
            for (var i = 0; i < patches.Length; i++)
            {
                if (i == targetIndex)
                {
                    continue;
                }

                var isInside = false;
                foreach (Vector3 tip in _tipBuffer)
                {
                    if (Vector3.Distance(tip, patches[i].Center) <= patches[i].TouchRadius)
                    {
                        isInside = true;
                        break;
                    }
                }

                if (isInside && !inside[i])
                {
                    entries++;
                }

                inside[i] = isInside;
            }

            return entries;
        }

        #endregion

        #region Frog handling

        private void PrepareFrog()
        {
            _frogRoot = frogTouch.transform;

            if (!_stateSaved)
            {
                _frogStartPosition = _frogRoot.position;
                _frogStartRotation = _frogRoot.rotation;
                _frogBaseScale = _frogRoot.localScale;
                _frogTouchWasEnabled = frogTouch.enabled;

                _frogRenderers = _frogRoot.GetComponentsInChildren<Renderer>(true);
                _frogRendererStates = new bool[_frogRenderers.Length];
                for (var i = 0; i < _frogRenderers.Length; i++)
                {
                    _frogRendererStates[i] = _frogRenderers[i].enabled;
                }

                // Measured before anything is moved: how far the lowest point of the frog is below its root,
                // so the feet (and not the model's pivot) end up on the ground.
                _footLocal = ComputeFootDistance() / Mathf.Max(_frogRoot.lossyScale.y, 0.0001f);

                // DepthCompensatedScale rewrites the frog's scale every frame, which would undo the task scale.
                _depthScale = frogTouch.GetComponent<DepthCompensatedScale>();
                if (_depthScale != null)
                {
                    _depthScaleWasEnabled = _depthScale.enabled;
                    _depthScale.enabled = false;
                }

                _stateSaved = true;
            }

            _taskScale = _frogBaseScale * frogScale;

            if (vocalizer != null)
            {
                vocalizer.autoCroak = false;
            }

            if (fieldRoot != null)
            {
                fieldRoot.SetActive(true);
            }

            SetFrogVisible(false);
            SetFrogTouchable(false);
        }

        /// <summary>
        ///     Puts the frog back where it was, hides the task field and re-enables everything
        ///     that was suppressed, so the scene can be used for free observation after the task.
        /// </summary>
        private void EndTask()
        {
            State = TaskState.Idle;

            if (_stateSaved && _frogRoot != null)
            {
                _frogRoot.SetPositionAndRotation(_frogStartPosition, _frogStartRotation);
                _frogRoot.localScale = _frogBaseScale;

                for (var i = 0; i < _frogRenderers.Length; i++)
                {
                    if (_frogRenderers[i] != null)
                    {
                        _frogRenderers[i].enabled = _frogRendererStates[i];
                    }
                }

                frogTouch.enabled = _frogTouchWasEnabled;

                if (_depthScale != null)
                {
                    _depthScale.enabled = _depthScaleWasEnabled;
                }

                _stateSaved = false;
            }

            if (vocalizer != null)
            {
                vocalizer.autoCroak = true;
            }

            if (fieldRoot != null)
            {
                fieldRoot.SetActive(false);
            }

            if (_logger != null)
            {
                Debug.Log($"[PokeTask] Log saved: {_logger.FilePath}");
                _logger.Dispose();
                _logger = null;
            }
        }

        private float ComputeFootDistance()
        {
            Vector3 up = _frogRoot.up;
            Vector3 origin = _frogRoot.position;
            var lowest = 0f;
            var found = false;

            foreach (Renderer frogRenderer in _frogRenderers)
            {
                if (frogRenderer == null)
                {
                    continue;
                }

                Bounds bounds = frogRenderer.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, sign);
                    float height = Vector3.Dot(corner - origin, up);
                    if (!found || height < lowest)
                    {
                        lowest = height;
                        found = true;
                    }
                }
            }

            return found ? Mathf.Max(0f, -lowest) : 0f;
        }

        private void SetFrogVisible(bool visible)
        {
            for (var i = 0; i < _frogRenderers.Length; i++)
            {
                if (_frogRenderers[i] != null)
                {
                    _frogRenderers[i].enabled = visible && _frogRendererStates[i];
                }
            }
        }

        private void SetFrogTouchable(bool touchable)
        {
            if (!touchable && frogTouch.touchPush != null)
            {
                frogTouch.touchPush.Factor = 0f;
            }

            frogTouch.enabled = touchable;
        }

        /// <param name="along">0 = inside the clump, 1 = on the landing spot.</param>
        /// <param name="yawDegrees">Extra turn around the ground's up axis (180 = facing back into the grass).</param>
        /// <param name="squashY">Body height factor along the frog's own up axis (1 = normal). Width and depth
        /// change the other way, so the volume stays about the same.</param>
        /// <param name="pitchDegrees">Nose-up tilt (negative = nose-down) around the axis across the hop.</param>
        private void ApplyFrogPose(GrassPatch patch, float along, float yawDegrees, float squashY = 1f, float pitchDegrees = 0f)
        {
            Vector3 up = patch.Up;
            Vector3 ground = Vector3.Lerp(patch.HomePoint, patch.LandingPoint, along);
            float arc = hopHeight * 4f * along * (1f - along);

            float widthFactor = 1f / Mathf.Sqrt(squashY);
            Vector3 scale = new Vector3(_taskScale.x * widthFactor, _taskScale.y * squashY, _taskScale.z * widthFactor);

            // The feet stay on the ground while the body is squashed or stretched.
            Vector3 position = ground + up * (_footLocal * scale.y + arc);
            Quaternion rotation = Quaternion.AngleAxis(yawDegrees, up) * patch.FrogRotation;

            if (!Mathf.Approximately(pitchDegrees, 0f))
            {
                // The frog's model looks along -Z (see GrassPatch.FrogRotation).
                Vector3 head = rotation * Vector3.back;
                Vector3 across = Vector3.Cross(up, head).normalized;
                rotation = Quaternion.AngleAxis(-pitchDegrees, across) * rotation;
            }

            _frogRoot.SetPositionAndRotation(position, rotation);
            _frogRoot.localScale = scale;
        }

        /// <summary>
        ///     One hop with animation principles instead of just sliding along a curve:
        ///     crouch (anticipation), take-off stretch, flight along a parabola with the nose following the
        ///     path, and (if <paramref name="settle" />) a squash on landing that bounces back.
        /// </summary>
        private IEnumerator HopSequence(GrassPatch patch, float from, float to, float yawDegrees, float crouchTime, bool settle)
        {
            // 1. Crouch.
            var elapsed = 0f;
            while (elapsed < crouchTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, elapsed / crouchTime);
                ApplyFrogPose(patch, from, yawDegrees, Mathf.Lerp(1f, crouchHeight, k), crouchPitchDegrees * k);
                elapsed += Time.deltaTime;
                yield return null;
            }

            // 2. Flight: stretched at take-off, back to normal in the air; the nose rises, then dips.
            const float launchPortion = 0.15f;
            elapsed = 0f;
            while (elapsed < hopSeconds)
            {
                float s = elapsed / hopSeconds;
                float height = s < launchPortion
                    ? Mathf.Lerp(crouchHeight, stretchHeight, Mathf.SmoothStep(0f, 1f, s / launchPortion))
                    : Mathf.Lerp(stretchHeight, 1f, Mathf.SmoothStep(0f, 1f, (s - launchPortion) / (1f - launchPortion)));
                float launchBlend = Mathf.SmoothStep(0f, 1f, s / launchPortion);
                float pitch = Mathf.Lerp(crouchPitchDegrees, flightPitchDegrees, launchBlend) * (1f - 2f * s);

                ApplyFrogPose(patch, Mathf.Lerp(from, to, s), yawDegrees, height, pitch);
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!settle)
            {
                ApplyFrogPose(patch, to, yawDegrees);
                yield break;
            }

            // 3. Landing: an instant squash that bounces back like a spring, while the nose levels out.
            PlayLandingSound();
            elapsed = 0f;
            while (elapsed < settleSeconds)
            {
                float decay = Mathf.Exp(-9f * elapsed);
                float bounce = Mathf.Cos(2f * Mathf.PI * 2.2f * elapsed);
                float height = 1f - (1f - landSquashHeight) * decay * bounce;
                float pitch = -flightPitchDegrees * (1f - Mathf.SmoothStep(0f, 1f, elapsed / settleSeconds));

                ApplyFrogPose(patch, to, yawDegrees, height, pitch);
                elapsed += Time.deltaTime;
                yield return null;
            }

            ApplyFrogPose(patch, to, yawDegrees);
        }

        private void PlayLandingSound()
        {
            if (!playLandingSound)
            {
                return;
            }

            // The frog's own audio source is spatialized and moves with the frog.
            AudioSource source = vocalizer != null && vocalizer.audioSource != null
                ? vocalizer.audioSource
                : frogTouch.audioSource;
            if (source == null)
            {
                return;
            }

            AudioClip clip = landingClip != null ? landingClip : GetThumpClip();
            source.PlayOneShot(clip, landingVolume);
        }

        /// <summary>A soft low "thump": a short decaying tone that drops in pitch, plus a tiny burst of noise.</summary>
        private AudioClip GetThumpClip()
        {
            if (_generatedThump != null)
            {
                return _generatedThump;
            }

            const int sampleRate = 44100;
            const float duration = 0.18f;
            int count = Mathf.RoundToInt(sampleRate * duration);
            var data = new float[count];
            var rng = new System.Random(3);

            for (var i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate;
                float tone = Mathf.Sin(2f * Mathf.PI * 85f * t * (1f + 0.6f * Mathf.Exp(-t * 20f))) * Mathf.Exp(-t * 28f);
                float noise = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 60f);
                data[i] = tone * 0.8f + noise * 0.15f;
            }

            _generatedThump = AudioClip.Create("FrogLandingThump", count, 1, sampleRate, false);
            _generatedThump.SetData(data, 0);
            return _generatedThump;
        }

        private IEnumerator TurnFrog(GrassPatch patch, float along, float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                ApplyFrogPose(patch, along, Mathf.Lerp(0f, 180f, elapsed / seconds));
                elapsed += Time.deltaTime;
                yield return null;
            }

            ApplyFrogPose(patch, along, 180f);
        }

        #endregion
    }
}
