using System.Collections.Generic;
using UnityEngine;

namespace VirtualShowcase.Showcase
{
    /// <summary>
    ///     One tall-grass clump of the poke task, standing on the ground. The frog hides inside the
    ///     clump, hops out to a landing spot on bare ground next to it, and hops back when it flees.
    ///     This object also plays the spatialized cue sound from the clump and can generate the tall
    ///     grass blades around itself (they grow along this transform's up axis, so the clump follows
    ///     whatever tilt its parent field has; the outer blades lean outwards like a fountain).
    ///     The hop direction is set per clump (angle and distance, or drag the yellow handle in the
    ///     Scene view while the clump is selected).
    ///     Keep the generated grass (and everything under this object) free of colliders,
    ///     otherwise it would block the finger raycast in <see cref="FrogTouchController" />.
    /// </summary>
    public class GrassPatch : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Frog placement")]
        [Tooltip("Where the frog stands inside the clump. Defaults to this transform (a point on the ground).")]
        [SerializeField]
        private UnityEngine.Transform frogAnchor;

        [Tooltip("Direction the frog hops out, as an angle around the ground's up axis. " +
                 "0 = straight toward the viewer, +90 = to the viewer's left, -90 = to the viewer's right, 180 = away from the viewer. " +
                 "The frog faces this way.")]
        [SerializeField]
        private float hopAngleDegrees;

        [Tooltip("How far the frog hops from the middle of the clump (cm). Keep the landing spot on bare ground, outside the grass.")]
        [SerializeField]
        private float hopDistance = 9f;

        [Tooltip("Extra offset applied to both the hiding and the landing spot, in the clump's space.")]
        [SerializeField]
        private Vector3 frogLocalOffset = Vector3.zero;

        [Tooltip("Extra rotation of the frog relative to the clump, in degrees (use Y = 180 if the frog hops backwards).")]
        [SerializeField]
        private Vector3 frogEulerOffset = Vector3.zero;

        [Header("Cue sound")]
        [Tooltip("Spatialized source at this clump. Created automatically if empty.")]
        [SerializeField]
        private AudioSource cueSource;

        [Header("Detection")]
        [Tooltip("A fingertip closer than this to the clump center counts as entering the clump (scene units, cm).")]
        [SerializeField]
        private float touchRadius = 6.5f;

        [Header("Generated grass (optional)")]
        [SerializeField]
        private bool generateBlades = true;

        [Tooltip("Leave empty to use the shared generated grass material.")]
        [SerializeField]
        private Material bladeMaterial;

        [Tooltip("Total number of blades of the whole clump. More blades = a denser, fuller clump.")]
        [SerializeField]
        private int bladeCount = 330;

        [Tooltip("Blade height range in scene units (cm), at the center of the largest bunch.")]
        [SerializeField]
        private Vector2 bladeHeight = new Vector2(6f, 9.5f);

        [Tooltip("Width of a blade at its root (cm).")]
        [SerializeField]
        private float bladeWidth = 0.6f;

        [Header("Shape: three bunches (large, medium, small)")]
        [Tooltip("Radius of the largest bunch (cm). The medium and small bunches are scaled down from it.")]
        [SerializeField]
        private float spreadRadius = 3.2f;

        [Tooltip("How far the centers of the bunches are from the middle of the clump (cm). " +
                 "The arrangement is irregular and different for every clump.")]
        [SerializeField]
        private float subClumpSpread = 4.2f;

        [Tooltip("Size of the medium bunch relative to the large one.")]
        [Range(0.3f, 1f)]
        [SerializeField]
        private float mediumScale = 0.75f;

        [Tooltip("Size of the small bunch relative to the large one.")]
        [Range(0.3f, 1f)]
        [SerializeField]
        private float smallScale = 0.5f;

        [Header("Shape: how the blades hang")]
        [Tooltip("How far the outermost blades lean outwards at the root, in degrees. Blades at the center of a bunch stand " +
                 "straight up; the lean grows towards the rim (0 = all blades straight up).")]
        [SerializeField]
        private float flareDegrees = 28f;

        [Tooltip("How quickly the lean grows from the center to the rim (1 = evenly, larger = only the rim leans).")]
        [SerializeField]
        private float flarePower = 1.1f;

        [Tooltip("How much the blades arch over towards their tips, in degrees (outer blades more). " +
                 "0 = stiff straight blades, 70 = soft hanging grass, 100 = tips pointing down.")]
        [SerializeField]
        private float droopDegrees = 70f;

        [Tooltip("Height of the outermost blades relative to the blades at the center (1 = same, 0.65 = 65 %).")]
        [Range(0.2f, 1f)]
        [SerializeField]
        private float edgeHeightScale = 0.65f;

        [Tooltip("0.5 spreads the blades evenly over the circle, larger values pack more of them towards the center.")]
        [Range(0.5f, 1.2f)]
        [SerializeField]
        private float centerBias = 0.7f;

        [Tooltip("Sideways sway of the blade tips in random directions, in cm (for the largest bunch).")]
        [SerializeField]
        private float bladeBend = 1.2f;

        [SerializeField]
        private int bladeSeed = 1;

        #endregion

        private UnityEngine.Transform Anchor => frogAnchor != null ? frogAnchor : transform;

        /// <summary>The ground point inside the clump where the frog hides.</summary>
        public Vector3 HomePoint => Anchor.TransformPoint(frogLocalOffset);

        /// <summary>The ground point on bare ground where the frog lands.</summary>
        public Vector3 LandingPoint => Anchor.TransformPoint(HopOffset + frogLocalOffset);

        /// <summary>The frog faces the way it hops (its model looks along -Z, towards the viewer, at angle 0).</summary>
        public Quaternion FrogRotation =>
            Anchor.rotation * Quaternion.Euler(0f, hopAngleDegrees, 0f) * Quaternion.Euler(frogEulerOffset);

        /// <summary>Hop offset in the clump's space: straight toward the viewer (-Z), turned by the hop angle.</summary>
        private Vector3 HopOffset => Quaternion.Euler(0f, hopAngleDegrees, 0f) * new Vector3(0f, 0f, -hopDistance);

        /// <summary>Up direction of the ground this clump stands on.</summary>
        public Vector3 Up => Anchor.up;

        public Vector3 Center => Anchor.position;
        public float TouchRadius => touchRadius;

        #region Event Functions

        private void Awake()
        {
            EnsureCueSource();
        }

        private void Start()
        {
            if (generateBlades)
            {
                GenerateBlades();
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(Anchor.position, touchRadius);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(HomePoint, 0.5f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(LandingPoint, 1f);
            Gizmos.DrawLine(HomePoint, LandingPoint);
        }

        #endregion

        /// <summary>
        ///     Sets the hop direction and distance so that the frog lands on the given world point
        ///     (projected onto the ground this clump stands on). Used by the Scene view handle.
        /// </summary>
        public void SetLanding(Vector3 worldPoint)
        {
            Vector3 local = Anchor.InverseTransformPoint(worldPoint) - frogLocalOffset;
            var flat = new Vector2(local.x, local.z);
            if (flat.sqrMagnitude < 0.01f)
            {
                return;
            }

            hopDistance = Mathf.Max(2f, flat.magnitude);

            // Inverse of HopOffset: offset = (-d sin a, 0, -d cos a).
            hopAngleDegrees = Mathf.Atan2(-flat.x, -flat.y) * Mathf.Rad2Deg;
        }

        public void PlayCue(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            EnsureCueSource();
            cueSource.clip = clip;
            cueSource.Play();
        }

        private void EnsureCueSource()
        {
            if (cueSource == null)
            {
                cueSource = gameObject.AddComponent<AudioSource>();
            }

            // Steam Audio is the project's spatializer plugin (AudioManager), so only the
            // per-source flags need to be set here.
            cueSource.playOnAwake = false;
            cueSource.loop = false;
            cueSource.spatialBlend = 1f;
            cueSource.spatialize = true;
        }

        /// <summary>
        ///     Three bunches (large, medium, small) at irregular places around the middle of the clump:
        ///     the large one close to the middle, the others further out and at uneven angles. The arrangement
        ///     comes from the clump's seed, so every clump looks different but stays the same between runs.
        /// </summary>
        private List<GrassMeshBuilder.SubCluster> CreateSubClusters(System.Random rng)
        {
            float startAngle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float gap = Mathf.Deg2Rad * (95f + 50f * (float)rng.NextDouble());

            return new List<GrassMeshBuilder.SubCluster>
            {
                new GrassMeshBuilder.SubCluster
                {
                    Offset = PointAround(startAngle, subClumpSpread * 0.3f),
                    Scale = 1f
                },
                new GrassMeshBuilder.SubCluster
                {
                    Offset = PointAround(startAngle + gap, subClumpSpread * Mathf.Lerp(0.8f, 1.1f, (float)rng.NextDouble())),
                    Scale = mediumScale
                },
                new GrassMeshBuilder.SubCluster
                {
                    Offset = PointAround(startAngle + gap + Mathf.Deg2Rad * (110f + 50f * (float)rng.NextDouble()),
                        subClumpSpread * Mathf.Lerp(0.9f, 1.2f, (float)rng.NextDouble())),
                    Scale = smallScale
                }
            };
        }

        private static Vector2 PointAround(float angleRadians, float distance)
        {
            return new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians)) * distance;
        }

        /// <summary>
        ///     Builds the clump's grass (see <see cref="GrassMeshBuilder" />) as one mesh under the anchor,
        ///     so it follows the tilt of the field. The grass casts and receives shadows.
        /// </summary>
        private void GenerateBlades()
        {
            var rng = new System.Random(bladeSeed);

            Mesh mesh = GrassMeshBuilder.BuildClump(
                rng,
                CreateSubClusters(rng),
                bladeCount,
                bladeHeight,
                bladeWidth,
                spreadRadius,
                bladeBend,
                7,
                flareDegrees,
                flarePower,
                edgeHeightScale,
                centerBias,
                droopDegrees);

            var grassObject = new GameObject("GeneratedGrass");
            grassObject.transform.SetParent(Anchor, false);
            grassObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = grassObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = bladeMaterial != null ? bladeMaterial : GrassMeshBuilder.GetBladeMaterial();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
        }
    }
}
