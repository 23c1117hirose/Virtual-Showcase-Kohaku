using System.Collections.Generic;
using System.Linq;
using Deform;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
///     Swaps the static frog of FrogRoom for the rigged, animated one (Tools → Poke Task → Swap Frog To Animated Model).
///     The animated frog is the CC0 "Schlegel's Green Tree Frog" with animations by ffish.asia / floraZia.com,
///     cut down to a single hop (see Assets/Models/FrogAnimated/LICENSE.txt).
///
///     What it does, in order:
///     1. Configures the FBX import (generic rig, readable mesh, no materials, a Hop and an Idle clip) and scales the
///        model so that it is as big as the old frog (the deformers' radii and strengths are in these units).
///     2. Creates a URP material and an Animator Controller (Idle ⇄ Hop).
///     3. Puts the new frog next to the old one under FrogModel, aligned (facing -Z, feet on the same ground).
///     4. Copies the deformers (touch push, belly, throat) onto the new skinned mesh and registers them in a Deformable.
///     5. Adds a MeshCollider (rest pose) for touching, and re-points every reference that used the old deformers.
///     6. Switches the old frog's renderer, collider and deformers off (it stays in the scene: its AudioSource,
///        FrogVocalizer and the like live on that object).
///     It can be run again: the previously added "FrogAnimated" object is replaced.
/// </summary>
public static class FrogModelSwap
{
    private const string FbxPath = "Assets/Models/FrogAnimated/frog_schlegel_hop.fbx";
    private const string TexturePath = "Assets/Models/FrogAnimated/textures/Material_baseColor.jpeg";
    private const string MaterialPath = "Assets/Materials/FrogAnimated.mat";
    private const string ControllerPath = "Assets/Animations/FrogAnimated.controller";
    private const string ScenePath = "Assets/Scenes/FrogRoom.unity";

    private const string FrogRootName = "FrogModel";
    private const string OldFrogName = "Frog_lowpoly200";
    private const string NewFrogName = "FrogAnimated";

    [MenuItem("Tools/Poke Task/Swap Frog To Animated Model")]
    public static void RunFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Frog swap", "Playを止めてから実行してください。", "OK");
            return;
        }

        Run();
    }

    /// <summary>Entry point for batch mode: Unity -batchmode -executeMethod FrogModelSwap.RunBatch</summary>
    public static void RunBatch()
    {
        try
        {
            Run();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FrogSwap] FAILED: " + e);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    public static void Run()
    {
        AssetDatabase.Refresh();

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        GameObject frogRoot = GameObject.Find(FrogRootName);
        Require(frogRoot != null, $"'{FrogRootName}' not found in the scene");

        Transform oldTransform = frogRoot.transform.Find(OldFrogName);
        Require(oldTransform != null, $"'{OldFrogName}' not found under {FrogRootName}");

        // A previous run is replaced.
        Transform previous = frogRoot.transform.Find(NewFrogName);
        if (previous != null)
        {
            Object.DestroyImmediate(previous.gameObject);
        }

        // The old frog's mesh object is somewhere inside the imported model (it carries the Deformable, the
        // deformers, the MeshCollider, the AudioSource and the FrogVocalizer).
        var oldDeformable = oldTransform.GetComponentInChildren<Deformable>(true);
        Require(oldDeformable != null, "no Deformable found in the old frog");
        GameObject oldFrog = oldDeformable.gameObject;
        var oldRenderer = oldFrog.GetComponent<MeshRenderer>();
        var oldFilter = oldFrog.GetComponent<MeshFilter>();
        Require(oldRenderer != null && oldFilter != null, "old frog is missing its MeshRenderer or MeshFilter");

        // Old deformers (identified by what they point at).
        var oldTouch = oldFrog.GetComponent<TouchPushDeformer>();
        RadialPushDeformer[] oldRadials = oldFrog.GetComponents<RadialPushDeformer>();
        RadialPushDeformer oldBelly = oldRadials.FirstOrDefault(r => r.Axis != null && r.Axis.name.Contains("Belly"));
        RadialPushDeformer oldThroat = oldRadials.FirstOrDefault(r => r.Axis != null && r.Axis.name.Contains("Throat"));
        Require(oldTouch != null && oldBelly != null && oldThroat != null,
            "old frog deformers not found (touch / belly / throat)");

        Bounds oldBounds = oldRenderer.bounds;
        Debug.Log($"[FrogSwap] old frog bounds size={oldBounds.size} center={oldBounds.center} min.y={oldBounds.min.y}");

        // 1. Import settings and scale.
        ConfigureImporter();
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        Require(fbx != null, "FBX not found: " + FbxPath);
        FitScaleTo(oldBounds);
        fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);

        // 2. Material and Animator Controller.
        Material material = CreateMaterial();
        AnimatorController controller = CreateController();

        // 3. New frog under FrogModel, next to the old one.
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx, frogRoot.transform);
        instance.name = NewFrogName;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var smr = instance.GetComponentInChildren<SkinnedMeshRenderer>();
        Require(smr != null, "no SkinnedMeshRenderer in the FBX");
        smr.sharedMaterial = material;
        smr.updateWhenOffscreen = true;
        smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        smr.receiveShadows = true;

        AlignToOld(instance, smr, oldBounds);

        var animator = instance.GetComponent<Animator>();
        if (animator == null)
        {
            animator = instance.AddComponent<Animator>();
        }

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // 4. Deformers on the skinned mesh.
        GameObject meshObject = smr.gameObject;
        var newDeformable = meshObject.AddComponent<Deformable>();
        var newTouch = meshObject.AddComponent<TouchPushDeformer>();
        var newBelly = meshObject.AddComponent<RadialPushDeformer>();
        var newThroat = meshObject.AddComponent<RadialPushDeformer>();
        EditorUtility.CopySerialized(oldTouch, newTouch);
        EditorUtility.CopySerialized(oldBelly, newBelly);
        EditorUtility.CopySerialized(oldThroat, newThroat);

        newDeformable.UpdateMode = oldDeformable.UpdateMode;
        newDeformable.CullingMode = oldDeformable.CullingMode;
        newDeformable.StripMode = oldDeformable.StripMode;
        newDeformable.NormalsRecalculation = oldDeformable.NormalsRecalculation;
        newDeformable.BoundsRecalculation = oldDeformable.BoundsRecalculation;
        newDeformable.ColliderRecalculation = oldDeformable.ColliderRecalculation;
        newDeformable.DeformerElements = new List<DeformerElement>
        {
            new DeformerElement(newTouch),
            new DeformerElement(newBelly),
            new DeformerElement(newThroat)
        };

        // 5. Collider for touching: the rest-pose (sitting) mesh, static like before.
        var collider = meshObject.AddComponent<MeshCollider>();
        collider.sharedMesh = smr.sharedMesh;

        // References that used the old deformers now use the new ones.
        var map = new Dictionary<Object, Object>
        {
            { oldTouch, newTouch },
            { oldBelly, newBelly },
            { oldThroat, newThroat }
        };
        int rewired = Rewire(map, oldFrog, meshObject);

        // 6. The old frog stays (audio etc.), but is not drawn, touched or deformed any more.
        oldRenderer.enabled = false;
        var oldCollider = oldFrog.GetComponent<MeshCollider>();
        if (oldCollider != null)
        {
            oldCollider.enabled = false;
        }

        oldDeformable.enabled = false;
        oldTouch.enabled = false;
        oldBelly.enabled = false;
        oldThroat.enabled = false;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Validate(frogRoot, instance, smr, newDeformable, newTouch, newBelly, newThroat, controller, rewired);
    }

    #region Import

    private static void ConfigureImporter()
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        Require(importer != null, "ModelImporter not found for " + FbxPath);

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.isReadable = true; // Deform reads and rewrites the vertices
        importer.importCameras = false;
        importer.importLights = false;
        importer.importBlendShapes = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.optimizeGameObjects = false;
        importer.SaveAndReimport();

        // Name the clips and set looping (clip list only exists after the first import).
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            Debug.Log($"[FrogSwap] FBX take: '{clip.name}' frames {clip.firstFrame}-{clip.lastFrame}");
            if (clip.name.Contains("Hop"))
            {
                clip.name = "Hop";
                clip.loopTime = false;
            }
            else if (clip.name.Contains("Idle"))
            {
                clip.name = "Idle";
                clip.loopTime = true;
            }
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    /// <summary>Scales the import so that the new frog is as big as the old one (up to three tries).</summary>
    private static void FitScaleTo(Bounds oldBounds)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
        float oldLength = MaxExtent(oldBounds.size);

        for (var i = 0; i < 3; i++)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var temp = (GameObject)Object.Instantiate(fbx);
            var smr = temp.GetComponentInChildren<SkinnedMeshRenderer>();
            float newLength = MaxExtent(smr.bounds.size);
            Object.DestroyImmediate(temp);

            float ratio = oldLength / Mathf.Max(newLength, 1e-6f);
            Debug.Log($"[FrogSwap] scale fit {i}: new length={newLength:F4} old length={oldLength:F4} ratio={ratio:F4} globalScale={importer.globalScale:F4}");
            if (Mathf.Abs(ratio - 1f) < 0.03f)
            {
                break;
            }

            importer.globalScale *= ratio;
            importer.SaveAndReimport();
        }
    }

    private static float MaxExtent(Vector3 size)
    {
        return Mathf.Max(size.x, Mathf.Max(size.y, size.z));
    }

    #endregion

    #region Assets

    private static Material CreateMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (material == null)
        {
            material = new Material(shader) { name = "FrogAnimated" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        Require(texture != null, "texture not found: " + TexturePath);
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.3f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static AnimatorController CreateController()
    {
        AnimationClip idle = null;
        AnimationClip hop = null;
        foreach (AnimationClip clip in AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>())
        {
            if (clip.name.StartsWith("__preview__"))
            {
                continue;
            }

            Debug.Log($"[FrogSwap] clip '{clip.name}' length={clip.length:F3}s loop={clip.isLooping}");
            if (clip.name == "Idle")
            {
                idle = clip;
            }
            else if (clip.name == "Hop")
            {
                hop = clip;
            }
        }

        Require(idle != null && hop != null, "FBX does not contain both an Idle and a Hop clip");

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
        }

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Hop", AnimatorControllerParameterType.Trigger);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState idleState = machine.AddState("Idle");
        idleState.motion = idle;
        AnimatorState hopState = machine.AddState("Hop");
        hopState.motion = hop;
        machine.defaultState = idleState;

        AnimatorStateTransition toHop = idleState.AddTransition(hopState);
        toHop.AddCondition(AnimatorConditionMode.If, 0f, "Hop");
        toHop.hasExitTime = false;
        toHop.duration = 0f;

        AnimatorStateTransition toIdle = hopState.AddTransition(idleState);
        toIdle.hasExitTime = true;
        toIdle.exitTime = 1f;
        toIdle.duration = 0.05f;

        EditorUtility.SetDirty(controller);
        return controller;
    }

    #endregion

    #region Placement

    /// <summary>
    ///     Turns the new frog so that its head points towards -Z (like the old one) and moves it so that the
    ///     middle of its body and its feet are where the old frog's were.
    /// </summary>
    private static void AlignToOld(GameObject instance, SkinnedMeshRenderer smr, Bounds oldBounds)
    {
        // Head direction from the skeleton: the end of the head chain (Bone.023) relative to the hips (Bone.002).
        Transform head = FindBone(instance.transform, "Bone.023");
        Transform hips = FindBone(instance.transform, "Bone.002_");
        if (head != null && hips != null)
        {
            Vector3 direction = head.position - hips.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 1e-8f)
            {
                float yaw = Vector3.SignedAngle(direction, Vector3.back, Vector3.up);
                instance.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * instance.transform.rotation;
                Debug.Log($"[FrogSwap] turned the new frog by {yaw:F1} degrees (head direction was {direction.normalized})");
            }
        }
        else
        {
            Debug.LogWarning("[FrogSwap] head/hips bones not found: the new frog was not turned. Check its facing by hand.");
        }

        Bounds b = smr.bounds;
        Vector3 shift = new Vector3(
            oldBounds.center.x - b.center.x,
            oldBounds.min.y - b.min.y,
            oldBounds.center.z - b.center.z);
        instance.transform.position += shift;

        Bounds after = smr.bounds;
        Debug.Log($"[FrogSwap] new frog bounds size={after.size} center={after.center} min.y={after.min.y}");
    }

    private static Transform FindBone(Transform root, string namePrefix)
    {
        return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith(namePrefix));
    }

    #endregion

    #region References

    /// <summary>Replaces every scene reference to an old deformer by the new one. Returns how many were changed.</summary>
    private static int Rewire(Dictionary<Object, Object> map, params GameObject[] skip)
    {
        var changed = 0;
        foreach (MonoBehaviour behaviour in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (behaviour == null || skip.Contains(behaviour.gameObject))
            {
                continue;
            }

            var serialized = new SerializedObject(behaviour);
            SerializedProperty property = serialized.GetIterator();
            var modified = false;
            while (property.NextVisible(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    property.objectReferenceValue == null ||
                    !map.TryGetValue(property.objectReferenceValue, out Object replacement))
                {
                    continue;
                }

                Debug.Log($"[FrogSwap] re-pointed {behaviour.GetType().Name}.{property.propertyPath} " +
                          $"({property.objectReferenceValue.GetType().Name} -> new)");
                property.objectReferenceValue = replacement;
                modified = true;
                changed++;
            }

            if (modified)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        return changed;
    }

    #endregion

    #region Checks

    private static void Validate(GameObject frogRoot, GameObject instance, SkinnedMeshRenderer smr, Deformable deformable,
        TouchPushDeformer touch, RadialPushDeformer belly, RadialPushDeformer throat, AnimatorController controller, int rewired)
    {
        var problems = new List<string>();

        if (deformable.DeformerElements.Count != 3 || deformable.DeformerElements.Any(e => e.Component == null))
        {
            problems.Add("Deformable does not have the three deformers");
        }

        if (touch.Axis == null || belly.Axis == null || throat.Axis == null)
        {
            problems.Add("a deformer has no Axis");
        }

        if (smr.sharedMesh == null || !smr.sharedMesh.isReadable)
        {
            problems.Add("the skinned mesh is missing or not readable");
        }

        if (smr.bones == null || smr.bones.Length == 0 || smr.bones.Any(b => b == null))
        {
            problems.Add("the skinned mesh has missing bones");
        }

        var animator = instance.GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController != controller)
        {
            problems.Add("Animator or controller missing");
        }

        var frogTouch = frogRoot.GetComponent<FrogTouchController>();
        if (frogTouch == null || frogTouch.touchPush != touch)
        {
            problems.Add("FrogTouchController.touchPush does not point to the new TouchPushDeformer");
        }

        var breathing = Object.FindObjectOfType<VirtualShowcase.Showcase.FrogBreathing>();
        var puff = Object.FindObjectOfType<VirtualShowcase.Showcase.FrogThroatPuff>();
        if (breathing == null || puff == null)
        {
            problems.Add("FrogBreathing / FrogThroatPuff not found");
        }

        Debug.Log($"[FrogSwap] re-pointed references: {rewired} (expected 3: touch, belly, throat)");
        Debug.Log($"[FrogSwap] skinned mesh: {smr.sharedMesh.vertexCount} vertices, {smr.bones.Length} bones, " +
                  $"renderer on '{smr.gameObject.name}'");

        if (problems.Count == 0)
        {
            Debug.Log("[FrogSwap] DONE: the animated frog is in the scene. Save was done; check it in Play.");
        }
        else
        {
            foreach (string problem in problems)
            {
                Debug.LogError("[FrogSwap] PROBLEM: " + problem);
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }

    #endregion
}
