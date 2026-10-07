using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualShowcase.Showcase
{
    /// <summary>
    ///     Dresses the poke task's ground: scattered pebbles, small grass tufts and a soft fade to black
    ///     towards the edge. Black is see-through on this display (the half mirror shows nothing there),
    ///     so the fade makes the ground look like an island floating in the air instead of a hard-edged slab.
    ///     Everything is generated once, the first time the field is shown, in the field's own space
    ///     (X across, Z away from the viewer, Y up), so it follows the tilt of the field.
    ///     Pebbles and tufts stay clear of the grass clumps and of every landing spot, and inside the
    ///     fully bright part of the ground (decorations would otherwise float over the black).
    /// </summary>
    public class PokeFieldDecorator : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Ground area (field space, cm)")]
        [Tooltip("Center of the ground on the X/Z plane.")]
        [SerializeField]
        private Vector2 areaCenter = new Vector2(0f, -1f);

        [Tooltip("Width (X) and depth (Z) of the ground. The fade ellipse is inscribed in it.")]
        [SerializeField]
        private Vector2 areaSize = new Vector2(64f, 44f);

        [Tooltip("Normalized radius (0 = center, 1 = edge) where the fade to black starts.")]
        [Range(0.3f, 0.95f)]
        [SerializeField]
        private float fadeStart = 0.75f;

        [Header("Pebbles")]
        [SerializeField]
        private int pebbleCount = 45;

        [Tooltip("Pebble radius range (cm).")]
        [SerializeField]
        private Vector2 pebbleRadius = new Vector2(0.25f, 0.9f);

        [Header("Small grass tufts")]
        [SerializeField]
        private int tuftCount = 16;

        [SerializeField]
        private Vector2 tuftHeight = new Vector2(1.5f, 3.5f);

        [Header("Keep clear")]
        [Tooltip("Nothing is placed closer than this to a clump center (cm).")]
        [SerializeField]
        private float clumpClearance = 10f;

        [Tooltip("Nothing is placed closer than this to a landing spot (cm).")]
        [SerializeField]
        private float landingClearance = 4.5f;

        [SerializeField]
        private int seed = 7;

        #endregion

        private bool _built;

        #region Event Functions

        private void Start()
        {
            if (!_built)
            {
                Build();
            }
        }

        #endregion

        private void Build()
        {
            _built = true;

            var rng = new System.Random(seed);
            List<Vector2> clumps = CollectPoints(patch => patch.HomePoint);
            List<Vector2> landings = CollectPoints(patch => patch.LandingPoint);

            BuildPebbles(rng, clumps, landings);
            BuildTufts(rng, clumps, landings);
            BuildFade();
        }

        /// <returns>The given patch points converted to this object's XZ plane.</returns>
        private List<Vector2> CollectPoints(System.Func<GrassPatch, Vector3> selector)
        {
            var points = new List<Vector2>();
            foreach (GrassPatch patch in GetComponentsInChildren<GrassPatch>(true))
            {
                Vector3 local = transform.InverseTransformPoint(selector(patch));
                points.Add(new Vector2(local.x, local.z));
            }

            return points;
        }

        private void BuildPebbles(System.Random rng, List<Vector2> clumps, List<Vector2> landings)
        {
            Material[] materials =
            {
                CreateStoneMaterial(new Color(0.38f, 0.34f, 0.30f)),
                CreateStoneMaterial(new Color(0.30f, 0.28f, 0.26f)),
                CreateStoneMaterial(new Color(0.46f, 0.39f, 0.31f))
            };

            var parent = new GameObject("Pebbles").transform;
            parent.SetParent(transform, false);

            for (var i = 0; i < pebbleCount; i++)
            {
                if (!TryPickSpot(rng, clumps, landings, out Vector2 spot))
                {
                    continue;
                }

                float radius = Mathf.Lerp(pebbleRadius.x, pebbleRadius.y, (float)rng.NextDouble());
                var size = new Vector3(
                    radius * Mathf.Lerp(1.6f, 2.6f, (float)rng.NextDouble()),
                    radius * Mathf.Lerp(0.9f, 1.6f, (float)rng.NextDouble()),
                    radius * Mathf.Lerp(1.6f, 2.6f, (float)rng.NextDouble()));

                GameObject pebble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pebble.name = "Pebble";
                Destroy(pebble.GetComponent<Collider>()); // would block the finger raycast

                pebble.transform.SetParent(parent, false);
                // Half sunk into the ground.
                pebble.transform.localPosition = new Vector3(spot.x, size.y * 0.15f, spot.y);
                pebble.transform.localRotation = Quaternion.Euler(
                    (float)rng.NextDouble() * 12f,
                    (float)rng.NextDouble() * 360f,
                    (float)rng.NextDouble() * 12f);
                pebble.transform.localScale = size;

                var pebbleRenderer = pebble.GetComponent<MeshRenderer>();
                pebbleRenderer.sharedMaterial = materials[rng.Next(materials.Length)];
                pebbleRenderer.shadowCastingMode = ShadowCastingMode.On;
                pebbleRenderer.receiveShadows = true;
            }
        }

        private void BuildTufts(System.Random rng, List<Vector2> clumps, List<Vector2> landings)
        {
            var parent = new GameObject("Tufts").transform;
            parent.SetParent(transform, false);

            for (var i = 0; i < tuftCount; i++)
            {
                if (!TryPickSpot(rng, clumps, landings, out Vector2 spot))
                {
                    continue;
                }

                Mesh mesh = GrassMeshBuilder.BuildBlades(rng, 14, tuftHeight, 0.45f, 0.9f, 0.8f, 4);

                var tuft = new GameObject("Tuft");
                tuft.transform.SetParent(parent, false);
                tuft.transform.localPosition = new Vector3(spot.x, 0f, spot.y);
                tuft.AddComponent<MeshFilter>().sharedMesh = mesh;

                var tuftRenderer = tuft.AddComponent<MeshRenderer>();
                tuftRenderer.sharedMaterial = GrassMeshBuilder.GetBladeMaterial();
                tuftRenderer.shadowCastingMode = ShadowCastingMode.On;
                tuftRenderer.receiveShadows = true;
            }
        }

        /// <summary>
        ///     Picks a random point inside the fully bright part of the ground that keeps its distance
        ///     from the clumps and the landing spots.
        /// </summary>
        private bool TryPickSpot(System.Random rng, List<Vector2> clumps, List<Vector2> landings, out Vector2 spot)
        {
            spot = Vector2.zero;
            Vector2 half = areaSize * 0.5f;

            for (var attempt = 0; attempt < 40; attempt++)
            {
                var candidate = new Vector2(
                    areaCenter.x + Mathf.Lerp(-half.x, half.x, (float)rng.NextDouble()),
                    areaCenter.y + Mathf.Lerp(-half.y, half.y, (float)rng.NextDouble()));

                float normalizedRadius = new Vector2(
                    (candidate.x - areaCenter.x) / half.x,
                    (candidate.y - areaCenter.y) / half.y).magnitude;
                if (normalizedRadius > fadeStart)
                {
                    continue;
                }

                if (IsTooClose(candidate, clumps, clumpClearance) || IsTooClose(candidate, landings, landingClearance))
                {
                    continue;
                }

                spot = candidate;
                return true;
            }

            return false;
        }

        private static bool IsTooClose(Vector2 point, List<Vector2> others, float clearance)
        {
            foreach (Vector2 other in others)
            {
                if (Vector2.Distance(point, other) < clearance)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     A transparent black quad just above the ground: fully see-through in the middle, opaque black
        ///     (= nothing on the mirror) at the rim of the ellipse and beyond.
        /// </summary>
        private void BuildFade()
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "GroundFade";
            Destroy(quad.GetComponent<Collider>());

            quad.transform.SetParent(transform, false);
            // A quad faces -Z; turning it by 90 degrees around X lays it flat, facing up.
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localPosition = new Vector3(areaCenter.x, 0.04f, areaCenter.y);
            quad.transform.localScale = new Vector3(areaSize.x, areaSize.y, 1f);

            var fadeRenderer = quad.GetComponent<MeshRenderer>();
            fadeRenderer.sharedMaterial = CreateFadeMaterial(CreateFadeTexture());
            fadeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fadeRenderer.receiveShadows = false;
        }

        private Texture2D CreateFadeTexture()
        {
            const int width = 256;
            const int height = 176;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "GroundFadeMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    float u = x / (width - 1f) * 2f - 1f;
                    float v = y / (height - 1f) * 2f - 1f;
                    float radius = Mathf.Sqrt(u * u + v * v);
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, 1f, radius));
                    texture.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
                }
            }

            texture.Apply(false, false);
            return texture;
        }

        private static Material CreateFadeMaterial(Texture2D mask)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader) { name = "GroundFadeMaterial" };

            // Switch the URP Unlit material to alpha blending (what the Surface Type dropdown does).
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);

            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", mask);
            material.mainTexture = mask;
            return material;
        }

        private static Material CreateStoneMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader) { name = "PebbleMaterial" };
            material.SetColor("_BaseColor", color);
            material.color = color;
            material.SetFloat("_Smoothness", 0.3f);
            material.SetFloat("_Metallic", 0f);
            return material;
        }
    }
}
