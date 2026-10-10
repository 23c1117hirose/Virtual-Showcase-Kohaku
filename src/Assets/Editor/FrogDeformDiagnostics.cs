using System.Linq;
using Deform;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
///     Prints how the belly / throat / touch deformers sit on the old (static) and the new (animated) frog,
///     to find out why a bulge may look too big: units of the mesh (vertex space vs world), where the axes are
///     relative to the body, and what share of the vertices each deformer reaches.
///     Unity -batchmode -nographics -executeMethod FrogDeformDiagnostics.RunBatch
/// </summary>
public static class FrogDeformDiagnostics
{
    private const string ScenePath = "Assets/Scenes/FrogRoom.unity";

    public static void RunBatch()
    {
        try
        {
            Run();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Diag] FAILED: " + e);
        }

        EditorApplication.Exit(0);
    }

    private static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject root = GameObject.Find("FrogModel");
        Transform newRoot = root.transform.Find("FrogAnimated");
        var smr = newRoot.GetComponentInChildren<SkinnedMeshRenderer>();
        var oldFilter = root.transform.Find("Frog_lowpoly200").GetComponentInChildren<MeshFilter>(true);

        Debug.Log("[Diag] FrogModel lossyScale=" + root.transform.lossyScale);
        Debug.Log("[Diag] FrogAnimated localScale=" + newRoot.localScale + " lossy=" + newRoot.lossyScale + " rot=" + newRoot.eulerAngles);
        Debug.Log("[Diag] new SMR object '" + smr.gameObject.name + "' localScale=" + smr.transform.localScale +
                  " lossy=" + smr.transform.lossyScale);
        Debug.Log("[Diag] new mesh: vertices=" + smr.sharedMesh.vertexCount + " bounds(local) size=" + smr.sharedMesh.bounds.size +
                  " center=" + smr.sharedMesh.bounds.center + " | world bounds size=" + smr.bounds.size + " center=" + smr.bounds.center);
        Debug.Log("[Diag] old mesh: vertices=" + oldFilter.sharedMesh.vertexCount + " bounds(local) size=" + oldFilter.sharedMesh.bounds.size +
                  " center=" + oldFilter.sharedMesh.bounds.center + " | lossy=" + oldFilter.transform.lossyScale);

        Transform belly = root.transform.Find("BellyPushAxis");
        Transform throat = root.transform.Find("ThroatBulgeAxis");
        Transform touch = root.transform.Find("TouchPoint");
        Debug.Log("[Diag] axes (world): belly=" + belly.position + " throat=" + throat.position + " touch=" + touch.position);

        Report("OLD", oldFilter.sharedMesh, oldFilter.transform, belly, throat, 2.8f, 4.5f);
        Report("NEW", smr.sharedMesh, smr.transform, belly, throat, 2.8f, 4.5f);

        var deformable = smr.GetComponent<Deformable>();
        Debug.Log("[Diag] new deformers: " + string.Join(", ",
            deformable.DeformerElements.Select(e => e.Component.GetType().Name + "(r=" +
                                                    ((e.Component as RadialPushDeformer) != null ? ((RadialPushDeformer)e.Component).Radius.ToString() :
                                                        ((TouchPushDeformer)e.Component).Radius.ToString()) + ")")));

        var breathing = Object.FindObjectOfType<VirtualShowcase.Showcase.FrogBreathing>();
        var puff = Object.FindObjectOfType<VirtualShowcase.Showcase.FrogThroatPuff>();
        Debug.Log("[Diag] FrogBreathing peak=" + new SerializedObject(breathing).FindProperty("peakFactor").floatValue +
                  " | FrogThroatPuff peak=" + new SerializedObject(puff).FindProperty("peakFactor").floatValue);
    }

    private static void Report(string label, Mesh mesh, Transform meshTransform, Transform belly, Transform throat,
        float bellyRadius, float throatRadius)
    {
        Vector3[] vertices = mesh.vertices;
        int bellyCount = 0;
        int throatCount = 0;
        var worldBounds = new Bounds(meshTransform.TransformPoint(vertices[0]), Vector3.zero);
        var bellyAxisLocalToWorld = belly.localToWorldMatrix;
        var throatAxisLocalToWorld = throat.localToWorldMatrix;

        foreach (Vector3 v in vertices)
        {
            Vector3 world = meshTransform.TransformPoint(v);
            worldBounds.Encapsulate(world);
            if (belly.InverseTransformPoint(world).magnitude < bellyRadius)
            {
                bellyCount++;
            }

            if (throat.InverseTransformPoint(world).magnitude < throatRadius)
            {
                throatCount++;
            }
        }

        float volumeRatio = mesh.bounds.size.x * mesh.bounds.size.y * mesh.bounds.size.z;
        Debug.Log($"[Diag] {label}: bind-pose world bounds size={worldBounds.size} center={worldBounds.center} " +
                  $"| vertices within belly radius {bellyRadius}: {bellyCount} ({100f * bellyCount / vertices.Length:F1}%) " +
                  $"| within throat radius {throatRadius}: {throatCount} ({100f * throatCount / vertices.Length:F1}%)");
        Debug.Log($"[Diag] {label}: belly axis relative to body bounds: " +
                  $"{(belly.position - worldBounds.min) } (from min corner), throat axis: {(throat.position - worldBounds.min)}");
    }
}
