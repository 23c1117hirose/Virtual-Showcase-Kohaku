using Deform;
using UnityEngine;
using VirtualShowcase.Utilities;

namespace VirtualShowcase.Showcase
{
    public enum FrogModelChoice
    {
        /// <summary>The rigged frog: it jumps with its own legs.</summary>
        Animated = 0,

        /// <summary>The earlier static frog: it hops with a squash-and-stretch motion done in code.</summary>
        Static = 1
    }

    /// <summary>
    ///     Lets the user choose between the two frogs under FrogModel: the rigged one (<c>FrogAnimated</c>)
    ///     and the earlier static one (<c>Frog_lowpoly200</c>). Press <see cref="toggleKey" /> (F8) to switch;
    ///     the choice is remembered (MyPrefs.FrogModelChoice). It cannot be changed while a poke task is running.
    ///     Switching turns one frog's renderer, collider and deformers on and the other one's off, and points
    ///     the touch, breathing and throat-puff scripts at the deformers of the frog that is shown. The static frog's
    ///     object also carries the audio source and the croaking, so it is never deactivated, only its drawing and
    ///     deformation are switched off.
    /// </summary>
    public class FrogModelSelector : MonoBehaviour
    {
        #region Serialized Fields

        [Tooltip("Used when nothing has been chosen yet on this PC.")]
        [SerializeField]
        private FrogModelChoice defaultChoice = FrogModelChoice.Animated;

        [SerializeField]
        private KeyCode toggleKey = KeyCode.F8;

        [Tooltip("Child of this object that holds the static frog (its mesh object carries the Deformable).")]
        [SerializeField]
        private string staticModelName = "Frog_lowpoly200";

        [Tooltip("Child of this object that is the rigged frog.")]
        [SerializeField]
        private string animatedModelName = "FrogAnimated";

        #endregion

        private FrogTouchController _frogTouch;
        private FrogBreathing _breathing;
        private FrogThroatPuff _throatPuff;
        private PokeTaskManager _task;

        private GameObject _animatedRoot;
        private FrogSet _static;
        private FrogSet _animated;
        private bool _resolved;

        /// <summary>The deformers and the parts that draw / collide, of one frog.</summary>
        private sealed class FrogSet
        {
            public Deformable Deformable;
            public TouchPushDeformer Touch;
            public RadialPushDeformer Belly;
            public RadialPushDeformer Throat;
            public MeshRenderer MeshRenderer;
            public Collider Collider;
        }

        public FrogModelChoice Current { get; private set; }
        public bool IsAvailable => _resolved;

        public event System.Action<FrogModelChoice> Changed;

        #region Event Functions

        private void Awake()
        {
            Resolve();
            if (!_resolved)
            {
                return;
            }

            int saved = MyPrefs.FrogModelChoice;
            Apply(saved >= 0 ? (FrogModelChoice)saved : defaultChoice);
        }

        private void Update()
        {
            if (_resolved && Input.GetKeyDown(toggleKey))
            {
                Toggle();
            }
        }

        #endregion

        public void Toggle()
        {
            Select(Current == FrogModelChoice.Animated ? FrogModelChoice.Static : FrogModelChoice.Animated);
        }

        /// <param name="save">Remember the choice for the next start.</param>
        public void Select(FrogModelChoice choice, bool save = true)
        {
            if (!_resolved || choice == Current)
            {
                return;
            }

            if (_task == null)
            {
                _task = FindObjectOfType<PokeTaskManager>(true);
            }

            if (_task != null && _task.IsRunning)
            {
                Debug.Log("[FrogModel] Cannot switch while the poke task is running.");
                return;
            }

            Apply(choice);
            if (save)
            {
                MyPrefs.FrogModelChoice = (int)choice;
                PlayerPrefs.Save();
            }
        }

        private void Resolve()
        {
            Transform staticRoot = transform.Find(staticModelName);
            Transform animatedRoot = transform.Find(animatedModelName);
            if (staticRoot == null || animatedRoot == null)
            {
                Debug.LogWarning($"[FrogModel] '{staticModelName}' or '{animatedModelName}' not found under {name}: " +
                                 "model switching is off.");
                return;
            }

            _animatedRoot = animatedRoot.gameObject;
            _static = BuildSet(staticRoot);
            _animated = BuildSet(animatedRoot);
            if (_static == null || _animated == null)
            {
                Debug.LogWarning("[FrogModel] a frog has no Deformable or deformers: model switching is off.");
                return;
            }

            _frogTouch = GetComponent<FrogTouchController>();
            _breathing = FindObjectOfType<FrogBreathing>(true);
            _throatPuff = FindObjectOfType<FrogThroatPuff>(true);
            _resolved = true;
        }

        private static FrogSet BuildSet(Transform root)
        {
            var deformable = root.GetComponentInChildren<Deformable>(true);
            if (deformable == null)
            {
                return null;
            }

            GameObject meshObject = deformable.gameObject;
            var set = new FrogSet
            {
                Deformable = deformable,
                Touch = meshObject.GetComponent<TouchPushDeformer>(),
                MeshRenderer = meshObject.GetComponent<MeshRenderer>(),
                Collider = meshObject.GetComponent<MeshCollider>()
            };

            foreach (RadialPushDeformer radial in meshObject.GetComponents<RadialPushDeformer>())
            {
                if (radial.Axis == null)
                {
                    continue;
                }

                if (radial.Axis.name.Contains("Belly"))
                {
                    set.Belly = radial;
                }
                else if (radial.Axis.name.Contains("Throat"))
                {
                    set.Throat = radial;
                }
            }

            return set.Touch != null && set.Belly != null && set.Throat != null ? set : null;
        }

        private void Apply(FrogModelChoice choice)
        {
            bool animated = choice == FrogModelChoice.Animated;

            // The frog that is switched off first, so that its Deformable puts the original mesh back.
            if (animated)
            {
                SetActive(_static, false);
                _animatedRoot.SetActive(true);
                SetActive(_animated, true);
            }
            else
            {
                SetActive(_animated, false);
                _animatedRoot.SetActive(false);
                SetActive(_static, true);
            }

            FrogSet shown = animated ? _animated : _static;
            if (_frogTouch != null)
            {
                _frogTouch.touchPush = shown.Touch;
            }

            if (_breathing != null)
            {
                _breathing.SetDeformer(shown.Belly);
            }

            if (_throatPuff != null)
            {
                _throatPuff.SetDeformer(shown.Throat);
            }

            Current = choice;
            Debug.Log($"[FrogModel] {(animated ? "Animated frog (own legs)" : "Static frog (code-driven hop)")}. " +
                      $"Press {toggleKey} to switch.");
            Changed?.Invoke(choice);
        }

        private static void SetActive(FrogSet set, bool active)
        {
            // Deformable first when switching off, last when switching on.
            if (!active)
            {
                set.Deformable.enabled = false;
            }

            set.Touch.enabled = active;
            set.Belly.enabled = active;
            set.Throat.enabled = active;
            set.Touch.Factor = 0f;
            set.Belly.Factor = 0f;
            set.Throat.Factor = 0f;

            if (set.MeshRenderer != null)
            {
                set.MeshRenderer.enabled = active;
            }

            if (set.Collider != null)
            {
                set.Collider.enabled = active;
            }

            if (active)
            {
                set.Deformable.enabled = true;
            }
        }
    }
}
