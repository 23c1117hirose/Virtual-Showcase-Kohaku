using System.Collections.Generic;
using UnityEngine;

namespace VirtualShowcase.Showcase
{
    /// <summary>
    ///     Procedural grass for the poke task: arching, tapered blades with a lit material whose texture
    ///     is a green gradient (dark at the root, bright at the tip, a few hues so no two blades match).
    ///     Used by <see cref="GrassPatch" /> (tall clumps made of several sub-clumps) and
    ///     <see cref="PokeFieldDecorator" /> (small upright tufts).
    /// </summary>
    public static class GrassMeshBuilder
    {
        /// <summary>One small bunch inside a clump: where its center is and how big it is (1 = full size).</summary>
        public struct SubCluster
        {
            public Vector2 Offset;
            public float Scale;
        }

        private const int ColorColumns = 8;
        private const int GradientRows = 64;

        private static Material _bladeMaterial;

        private sealed class MeshData
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();

            public Mesh ToMesh()
            {
                var mesh = new Mesh { name = "GeneratedGrass" };
                if (Vertices.Count > 65000)
                {
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                }

                mesh.SetVertices(Vertices);
                mesh.SetUVs(0, Uvs);
                mesh.SetTriangles(Triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>
        ///     One round bunch of blades around the origin. See <see cref="AddCluster" /> for the parameters.
        ///     With no flare and no droop it is plain upright grass whose tips bend in random directions.
        /// </summary>
        public static Mesh BuildBlades(
            System.Random rng,
            int bladeCount,
            Vector2 heightRange,
            float width,
            float radius,
            float bend,
            int segments = 5,
            float flareDegrees = 0f,
            float flarePower = 1.2f,
            float edgeHeightScale = 1f,
            float centerBias = 0.5f,
            float droopDegrees = 0f)
        {
            var data = new MeshData();
            AddCluster(data, rng, Vector3.zero, bladeCount, heightRange, width, radius, bend, segments,
                flareDegrees, flarePower, edgeHeightScale, centerBias, droopDegrees);
            return data.ToMesh();
        }

        /// <summary>
        ///     A clump made of several bunches of different sizes (for example large, medium, small), each one
        ///     fanning out around its own center. Irregular centers and sizes break up the round, artificial look
        ///     of a single bunch. <paramref name="totalBladeCount" /> is shared between the bunches in proportion
        ///     to their area; <paramref name="heightRange" /> and <paramref name="radius" /> are for a bunch of
        ///     scale 1 and shrink with the bunch's scale.
        /// </summary>
        public static Mesh BuildClump(
            System.Random rng,
            IList<SubCluster> subClusters,
            int totalBladeCount,
            Vector2 heightRange,
            float width,
            float radius,
            float bend,
            int segments,
            float flareDegrees,
            float flarePower,
            float edgeHeightScale,
            float centerBias,
            float droopDegrees)
        {
            var data = new MeshData();

            float totalArea = 0f;
            foreach (SubCluster sub in subClusters)
            {
                totalArea += sub.Scale * sub.Scale;
            }

            foreach (SubCluster sub in subClusters)
            {
                float share = totalArea > 0f ? sub.Scale * sub.Scale / totalArea : 1f / subClusters.Count;
                int count = Mathf.Max(12, Mathf.RoundToInt(totalBladeCount * share));

                AddCluster(
                    data,
                    rng,
                    new Vector3(sub.Offset.x, 0f, sub.Offset.y),
                    count,
                    heightRange * sub.Scale,
                    width * Mathf.Lerp(0.8f, 1f, sub.Scale),
                    radius * sub.Scale,
                    bend * sub.Scale,
                    segments,
                    flareDegrees,
                    flarePower,
                    edgeHeightScale,
                    centerBias,
                    droopDegrees);
            }

            return data.ToMesh();
        }

        /// <summary>
        ///     Adds <paramref name="bladeCount" /> blades scattered inside a circle around <paramref name="origin" />
        ///     on the XZ plane (Y is up). Every blade is a ribbon that narrows to a point and exists twice with
        ///     opposite winding, so it is visible (and correctly lit) from both sides with any culling.
        ///     <list type="bullet">
        ///         <item>Flare: blades at the center stand straight up; the further a blade is from the center, the more
        ///         it leans outwards (up to <paramref name="flareDegrees" /> at the rim, following
        ///         <paramref name="flarePower" />).</item>
        ///         <item>Droop: every blade arches outwards towards its tip, up to about <paramref name="droopDegrees" />
        ///         of extra bend (more for outer blades), so the grass hangs like real, soft grass instead of
        ///         standing like stiff needles.</item>
        ///         <item>Height: shorter towards the rim (<paramref name="edgeHeightScale" /> of the height there).</item>
        ///         <item><paramref name="centerBias" />: 0.5 is even over the circle, larger packs more blades towards
        ///         the center.</item>
        ///     </list>
        /// </summary>
        private static void AddCluster(
            MeshData data,
            System.Random rng,
            Vector3 origin,
            int bladeCount,
            Vector2 heightRange,
            float width,
            float radius,
            float bend,
            int segments,
            float flareDegrees,
            float flarePower,
            float edgeHeightScale,
            float centerBias,
            float droopDegrees)
        {
            bool natural = flareDegrees > 0f || droopDegrees > 0f;
            var centers = new Vector3[segments + 1];

            for (var i = 0; i < bladeCount; i++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float distance = radius * Mathf.Pow((float)rng.NextDouble(), centerBias);
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 basePosition = origin + outward * distance;

                float normalizedDistance = radius > 0f ? Mathf.Clamp01(distance / radius) : 0f;

                // Shorter towards the rim.
                float height = Mathf.Lerp(heightRange.x, heightRange.y, (float)rng.NextDouble())
                               * Mathf.Lerp(1f, edgeHeightScale, normalizedDistance);

                float bladeWidth = natural ? width * (0.8f + 0.4f * (float)rng.NextDouble()) : width;
                float facing = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                var side = new Vector3(Mathf.Cos(facing), 0f, Mathf.Sin(facing));

                // Lean at the root: 0 at the center, flareDegrees at the rim, a bit of randomness on top.
                float leanDegrees = flareDegrees * Mathf.Pow(normalizedDistance, flarePower)
                                    * (0.75f + 0.5f * (float)rng.NextDouble());

                // Extra bend that builds up towards the tip (outer blades droop more).
                float droopTotal = droopDegrees * (0.4f + 0.6f * normalizedDistance)
                                   * (0.7f + 0.6f * (float)rng.NextDouble());

                // A little sideways sway on top, in a random direction.
                float swayAngle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                var swayDirection = new Vector3(Mathf.Cos(swayAngle), 0f, Mathf.Sin(swayAngle));
                float swayAmount = bend * (0.3f + 0.7f * (float)rng.NextDouble());

                // One of the gradient columns, picked per blade.
                float u = (rng.Next(ColorColumns) + 0.5f) / ColorColumns;

                // Walk up the blade: the direction turns from "up" towards "outwards" (and past it, downwards).
                centers[0] = basePosition;
                for (var s = 0; s < segments; s++)
                {
                    float tMid = (s + 0.5f) / segments;
                    float bendDegrees = Mathf.Min(leanDegrees + droopTotal * Mathf.Pow(tMid, 1.4f), 105f);
                    float bendRadians = bendDegrees * Mathf.Deg2Rad;
                    Vector3 direction = Vector3.up * Mathf.Cos(bendRadians) + outward * Mathf.Sin(bendRadians);
                    centers[s + 1] = centers[s] + direction * (height / segments);
                }

                for (var s = 0; s <= segments; s++)
                {
                    float t = (float)s / segments;
                    Vector3 point = centers[s] + swayDirection * (swayAmount * t * t);
                    point.y = Mathf.Max(point.y, 0.03f); // never sink into the ground
                    centers[s] = point;
                }

                AddBlade(data, centers, side * (bladeWidth * 0.5f), u, segments);
            }
        }

        /// <summary>Adds one ribbon (front and back) along the given centerline, narrowing to a point at the tip.</summary>
        private static void AddBlade(MeshData data, Vector3[] centers, Vector3 halfWidthAtRoot, float u, int segments)
        {
            List<Vector3> vertices = data.Vertices;
            List<Vector2> uvs = data.Uvs;
            List<int> triangles = data.Triangles;

            int frontStart = vertices.Count;

            // Ring of (left, right) vertex pairs from the root up to just below the tip, then the tip.
            for (var s = 0; s < segments; s++)
            {
                float t = (float)s / segments;
                Vector3 halfWidth = halfWidthAtRoot * (1f - t);

                vertices.Add(centers[s] - halfWidth);
                vertices.Add(centers[s] + halfWidth);
                uvs.Add(new Vector2(u, t));
                uvs.Add(new Vector2(u, t));
            }

            vertices.Add(centers[segments]);
            uvs.Add(new Vector2(u, 1f));

            int frontCount = vertices.Count - frontStart;

            // Front faces.
            for (var s = 0; s < segments - 1; s++)
            {
                int a = frontStart + s * 2;
                triangles.Add(a);
                triangles.Add(a + 2);
                triangles.Add(a + 1);
                triangles.Add(a + 1);
                triangles.Add(a + 2);
                triangles.Add(a + 3);
            }

            int last = frontStart + (segments - 1) * 2;
            int tip = frontStart + segments * 2;
            triangles.Add(last);
            triangles.Add(tip);
            triangles.Add(last + 1);

            // Back faces: duplicated vertices (so the normals can point the other way) with reversed winding.
            int backStart = vertices.Count;
            for (var v = 0; v < frontCount; v++)
            {
                vertices.Add(vertices[frontStart + v]);
                uvs.Add(uvs[frontStart + v]);
            }

            for (var s = 0; s < segments - 1; s++)
            {
                int a = backStart + s * 2;
                triangles.Add(a);
                triangles.Add(a + 1);
                triangles.Add(a + 2);
                triangles.Add(a + 1);
                triangles.Add(a + 3);
                triangles.Add(a + 2);
            }

            int backLast = backStart + (segments - 1) * 2;
            int backTip = backStart + segments * 2;
            triangles.Add(backLast);
            triangles.Add(backLast + 1);
            triangles.Add(backTip);
        }

        /// <summary>One shared lit material for all generated grass.</summary>
        public static Material GetBladeMaterial()
        {
            if (_bladeMaterial != null)
            {
                return _bladeMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Texture");
            }

            Texture2D gradient = CreateGradientTexture();

            _bladeMaterial = new Material(shader) { name = "GeneratedGrassMaterial" };
            _bladeMaterial.mainTexture = gradient;
            if (_bladeMaterial.HasProperty("_BaseMap"))
            {
                _bladeMaterial.SetTexture("_BaseMap", gradient);
            }

            if (_bladeMaterial.HasProperty("_BaseColor"))
            {
                _bladeMaterial.SetColor("_BaseColor", Color.white);
            }

            if (_bladeMaterial.HasProperty("_Smoothness"))
            {
                _bladeMaterial.SetFloat("_Smoothness", 0.25f);
            }

            if (_bladeMaterial.HasProperty("_Metallic"))
            {
                _bladeMaterial.SetFloat("_Metallic", 0f);
            }

            return _bladeMaterial;
        }

        /// <summary>
        ///     ColorColumns hues side by side; each column goes from a dark root to a bright, slightly yellower tip.
        /// </summary>
        private static Texture2D CreateGradientTexture()
        {
            var texture = new Texture2D(ColorColumns, GradientRows, TextureFormat.RGBA32, false)
            {
                name = "GeneratedGrassGradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var darkGreen = new Color(0.14f, 0.34f, 0.08f);
            var lightGreen = new Color(0.30f, 0.52f, 0.12f);
            var tipTint = new Color(0.08f, 0.07f, 0f);

            for (var x = 0; x < ColorColumns; x++)
            {
                Color hue = Color.Lerp(darkGreen, lightGreen, (float)x / (ColorColumns - 1));
                for (var y = 0; y < GradientRows; y++)
                {
                    float t = (float)y / (GradientRows - 1);
                    Color root = hue * 0.35f;
                    Color tip = hue * 1.25f + tipTint;
                    Color color = Color.Lerp(root, tip, Mathf.Pow(t, 0.8f));
                    color.a = 1f;
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply(false, false);
            return texture;
        }
    }
}
