using System.Linq;
using Deform;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
///     Fits the belly / throat deformers to the animated frog (Tools → Poke Task → Fit Deformers To Animated Frog).
///     The axes and radii in the scene were tuned by eye on the old static frog. The new frog has a different body,
///     so the same numbers would bulge the wrong places and too much of the body. This tool
///     1. sets the new frog on the same ground and middle as the old one, using its exact vertices
///        (the renderer bounds of a skinned mesh are only a generous box),
///     2. puts each axis at the same relative place inside the new body as it was in the old body
///        (belly and throat are on shared axis objects, the old frog's deformers are switched off),
///     3. picks each radius so that the deformer reaches the same share of the body surface as it did on the old frog.
///     It can be run again at any time: it always starts from the original axis positions.
/// </summary>
public static class FrogDeformerFit
{
    private const string ScenePath = "Assets/Scenes/FrogRoom.unity";

    // Where the axes and radii were when they were tuned on the old frog (FrogModel space).
    private static readonly Vector3 OriginalBellyPosition = new Vector3(0f, 0.73f, 0f);
    private static readonly Vector3 OriginalThroatPosition = new Vector3(0f, -0.03f, -2.87f);
    private const float OriginalBellyRadius = 2.8f;
    private const float OriginalThroatRadius = 4.5f;
    private const float TouchRadius = 2.0f;

    [MenuItem("Tools/Poke Task/Fit Deformers To Animated Frog")]
    public static void RunFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Frog deformers", "Playを止めてから実行してください。", "OK");
            return;
        }

        Run();
    }

    public static void RunBatch()
    {
        try
        {
            Run();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[DeformerFit] FAILED: " + e);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        Fit();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>Does the fitting in the open scene (the caller marks the scene dirty and saves it).</summary>
    public static void Fit()
    {
        GameObject root = GameObject.Find("FrogModel");
        Transform oldRoot = root.transform.Find("Frog_lowpoly200");
        Transform newRoot = root.transform.Find("FrogAnimated");
        if (oldRoot == null || newRoot == null)
        {
            throw new System.InvalidOperationException("Frog_lowpoly200 / FrogAnimated not found under FrogModel");
        }

        var oldFilter = oldRoot.GetComponentInChildren<MeshFilter>(true);
        var smr = newRoot.GetComponentInChildren<SkinnedMeshRenderer>();
        Transform belly = root.transform.Find("BellyPushAxis");
        Transform throat = root.transform.Find("ThroatBulgeAxis");

        // Old body (the sitting static mesh).
        Vector3[] oldWorld = WorldVertices(oldFilter.sharedMesh, oldFilter.transform);
        float[] oldWeights = VertexAreas(oldWorld, oldFilter.sharedMesh.triangles);
        Bounds oldBounds = BoundsOf(oldWorld);
        float oldBellyShare = Coverage(oldWorld, oldWeights, root.transform.TransformPoint(OriginalBellyPosition), OriginalBellyRadius);
        float oldThroatShare = Coverage(oldWorld, oldWeights, root.transform.TransformPoint(OriginalThroatPosition), OriginalThroatRadius);
        Debug.Log($"[DeformerFit] old body bounds size={oldBounds.size} | belly reaches {oldBellyShare * 100:F1}% of the surface, " +
                  $"throat {oldThroatShare * 100:F1}%");

        // 1. New body on the same ground and middle.
        Vector3[] newWorld = WorldVertices(smr.sharedMesh, smr.transform);
        Bounds newBounds = BoundsOf(newWorld);
        var shift = new Vector3(
            oldBounds.center.x - newBounds.center.x,
            oldBounds.min.y - newBounds.min.y,
            oldBounds.center.z - newBounds.center.z);
        newRoot.position += shift;
        newWorld = WorldVertices(smr.sharedMesh, smr.transform);
        newBounds = BoundsOf(newWorld);
        float[] newWeights = VertexAreas(newWorld, smr.sharedMesh.triangles);
        Debug.Log($"[DeformerFit] new body bounds size={newBounds.size} (moved by {shift}) | feet at y={newBounds.min.y:F3} (old {oldBounds.min.y:F3})");

        // 2. Same relative place inside the body.
        Vector3 bellyWorld = MapBetweenBodies(root.transform.TransformPoint(OriginalBellyPosition), oldBounds, newBounds);
        Vector3 throatWorld = MapBetweenBodies(root.transform.TransformPoint(OriginalThroatPosition), oldBounds, newBounds);
        belly.position = bellyWorld;
        throat.position = throatWorld;

        // 3. Same share of the surface.
        float bellyRadius = Mathf.Clamp(FindRadius(newWorld, newWeights, bellyWorld, oldBellyShare),
            OriginalBellyRadius * 0.5f, OriginalBellyRadius * 1.3f);
        float throatRadius = Mathf.Clamp(FindRadius(newWorld, newWeights, throatWorld, oldThroatShare),
            OriginalThroatRadius * 0.5f, OriginalThroatRadius * 1.3f);

        foreach (RadialPushDeformer deformer in smr.GetComponents<RadialPushDeformer>())
        {
            if (deformer.Axis == belly)
            {
                deformer.Radius = bellyRadius;
                EditorUtility.SetDirty(deformer);
            }
            else if (deformer.Axis == throat)
            {
                deformer.Radius = throatRadius;
                EditorUtility.SetDirty(deformer);
            }
        }

        // The dent of a touch: a wider area than before (1.5), so that it can be seen on the small frog during the task.
        foreach (TouchPushDeformer touchDeformer in new[]
                 {
                     smr.GetComponent<TouchPushDeformer>(),
                     oldFilter.GetComponent<TouchPushDeformer>()
                 })
        {
            if (touchDeformer != null)
            {
                touchDeformer.Radius = TouchRadius;
                EditorUtility.SetDirty(touchDeformer);
            }
        }

        EditorUtility.SetDirty(belly);
        EditorUtility.SetDirty(throat);
        Debug.Log($"[DeformerFit] belly axis {bellyWorld} radius {bellyRadius:F2} (was {OriginalBellyRadius}) reaches " +
                  $"{Coverage(newWorld, newWeights, bellyWorld, bellyRadius) * 100:F1}% | " +
                  $"throat axis {throatWorld} radius {throatRadius:F2} (was {OriginalThroatRadius}) reaches " +
                  $"{Coverage(newWorld, newWeights, throatWorld, throatRadius) * 100:F1}%");
    }

    private static Vector3[] WorldVertices(Mesh mesh, Transform transform)
    {
        Vector3[] local = mesh.vertices;
        var world = new Vector3[local.Length];
        for (var i = 0; i < local.Length; i++)
        {
            world[i] = transform.TransformPoint(local[i]);
        }

        return world;
    }

    private static Bounds BoundsOf(Vector3[] points)
    {
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 point in points)
        {
            bounds.Encapsulate(point);
        }

        return bounds;
    }

    /// <summary>Surface area that belongs to each vertex (a third of every triangle around it).</summary>
    private static float[] VertexAreas(Vector3[] world, int[] triangles)
    {
        var areas = new float[world.Length];
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            Vector3 a = world[triangles[i]];
            Vector3 b = world[triangles[i + 1]];
            Vector3 c = world[triangles[i + 2]];
            float third = Vector3.Cross(b - a, c - a).magnitude * 0.5f / 3f;
            areas[triangles[i]] += third;
            areas[triangles[i + 1]] += third;
            areas[triangles[i + 2]] += third;
        }

        return areas;
    }

    private static float Coverage(Vector3[] world, float[] weights, Vector3 center, float radius)
    {
        float inside = 0f;
        float total = 0f;
        for (var i = 0; i < world.Length; i++)
        {
            total += weights[i];
            if ((world[i] - center).sqrMagnitude < radius * radius)
            {
                inside += weights[i];
            }
        }

        return total > 0f ? inside / total : 0f;
    }

    private static float FindRadius(Vector3[] world, float[] weights, Vector3 center, float targetShare)
    {
        float low = 0.2f;
        float high = 15f;
        for (var i = 0; i < 30; i++)
        {
            float middle = (low + high) * 0.5f;
            if (Coverage(world, weights, center, middle) < targetShare)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) * 0.5f;
    }

    private static Vector3 MapBetweenBodies(Vector3 point, Bounds from, Bounds to)
    {
        return new Vector3(
            to.min.x + (point.x - from.min.x) / Mathf.Max(from.size.x, 1e-6f) * to.size.x,
            to.min.y + (point.y - from.min.y) / Mathf.Max(from.size.y, 1e-6f) * to.size.y,
            to.min.z + (point.z - from.min.z) / Mathf.Max(from.size.z, 1e-6f) * to.size.z);
    }
}
