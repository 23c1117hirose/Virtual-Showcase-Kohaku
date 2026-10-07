using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VirtualShowcase.Showcase;

/// <summary>
///     Scene view tools for choosing where the frog lands (see <see cref="GrassPatch" />).
///     <list type="bullet">
///         <item>Tools → Poke Task → Edit Hop Directions: shows the field, selects it and points the Scene view
///         straight down onto the ground.</item>
///         <item>While the PokeField (or anything inside it) is selected, every clump shows a yellow handle at its
///         landing spot. Drag it: the hop angle and distance of that clump follow.</item>
///         <item>Landing spots are also drawn as yellow rings and lines whenever the Gizmos are on.</item>
///     </list>
///     Handles only work in edit mode: the field is hidden while the game runs, and anything changed in Play
///     mode is lost when it stops.
/// </summary>
[InitializeOnLoad]
public static class GrassPatchHandles
{
    static GrassPatchHandles()
    {
        SceneView.duringSceneGui += OnSceneGui;
    }

    [MenuItem("Tools/Poke Task/Edit Hop Directions")]
    public static void EditHopDirections()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Poke Task",
                "Playを止めてから実行してください。\n(Play中はフィールドが非表示で，変更も保存されません)",
                "OK");
            return;
        }

        var decorator = Object.FindObjectOfType<PokeFieldDecorator>(true);
        if (decorator == null)
        {
            Debug.LogWarning("[PokeTask] No PokeField in the open scene. Run Tools > Poke Task > Create Setup In Scene first.");
            return;
        }

        decorator.gameObject.SetActive(true);
        Selection.activeGameObject = decorator.gameObject;

        SceneView view = SceneView.lastActiveSceneView;
        if (view == null)
        {
            view = EditorWindow.GetWindow<SceneView>();
        }

        // Straight down onto the ground (screen up = away from the viewer).
        Quaternion look = decorator.transform.rotation * Quaternion.Euler(90f, 0f, 0f);
        Vector3 center = decorator.transform.TransformPoint(new Vector3(0f, 0f, -1f));
        view.LookAt(center, look, 60f, false, false);
        view.Focus();
        view.Repaint();

        Debug.Log("[PokeTask] Drag the yellow spheres in the Scene view to set where each frog lands.");
    }

    private static void OnSceneGui(SceneView sceneView)
    {
        if (Application.isPlaying || !IsFieldSelected())
        {
            return;
        }

        CompareFunction previousZTest = Handles.zTest;
        Handles.zTest = CompareFunction.Always; // never hidden behind the ground or the grass

        foreach (GrassPatch patch in Object.FindObjectsOfType<GrassPatch>())
        {
            DrawHandle(patch);
        }

        Handles.zTest = previousZTest;
    }

    /// <returns>Whether the selection is the PokeField or something inside it (a clump, the ground, ...).</returns>
    private static bool IsFieldSelected()
    {
        UnityEngine.Transform selected = Selection.activeTransform;
        if (selected == null)
        {
            return false;
        }

        return selected.GetComponentInParent<PokeFieldDecorator>() != null ||
               selected.GetComponent<PokeFieldDecorator>() != null;
    }

    private static void DrawHandle(GrassPatch patch)
    {
        Vector3 home = patch.HomePoint;
        Vector3 landing = patch.LandingPoint;

        Handles.color = new Color(0.3f, 1f, 0.3f);
        Handles.DrawWireDisc(home, patch.Up, 1f);

        Handles.color = Color.yellow;
        Handles.DrawDottedLine(home, landing, 4f);

        float size = HandleUtility.GetHandleSize(landing) * 0.14f;

        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.FreeMoveHandle(landing, size, Vector3.zero, Handles.SphereHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(patch, "Move Frog Landing");
            patch.SetLanding(moved);
            EditorUtility.SetDirty(patch);
        }

        Handles.Label(landing + patch.Up * (size * 1.6f), patch.name);
    }

    // Yellow rings and lines for the landing spots, visible with the Gizmos on, selected or not.
    [DrawGizmo(GizmoType.NonSelected | GizmoType.Pickable)]
    private static void DrawLandingGizmo(GrassPatch patch, GizmoType type)
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(patch.LandingPoint, 1f);
        Gizmos.DrawLine(patch.HomePoint, patch.LandingPoint);
    }
}
