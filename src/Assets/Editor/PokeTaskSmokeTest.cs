using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualShowcase.Showcase;

/// <summary>
///     Automatic check without a screen or a person (also usable in batch mode):
///     Unity -batchmode -nographics -executeMethod PokeTaskSmokeTest.RunBatch        (rigged frog)
///     Unity -batchmode -nographics -executeMethod PokeTaskSmokeTest.RunBatchStatic  (earlier static frog)
///     It opens FrogRoom, enters Play mode, selects the frog model, starts one practice trial of the poke task and
///     watches that the frog really does what it should: hops out (rigged: the Animator reaches the Hop state and the
///     leg bones move), travels from the grass to the landing spot, turns around by 180 degrees (rigged: with the
///     TurnHop clip), nothing logs an error, deformations stay small, and the trial ends. Exit code 0 = all good.
/// </summary>
[InitializeOnLoad]
public static class PokeTaskSmokeTest
{
    private const string ScenePath = "Assets/Scenes/FrogRoom.unity";
    private const string StateKey = "PokeSmoke.State";
    private const string ChoiceKey = "PokeSmoke.Choice";
    private const double Timeout = 70.0;

    private static bool _hooked;
    private static double _startTime;
    private static bool _modelSelected;
    private static bool _taskStarted;
    private static bool _sawHop;
    private static bool _sawTurnHop;
    private static float _maxTravel;
    private static float _maxBoneAngle;
    private static float _maxLiftAboveStart;
    private static Vector3 _frogStart;
    private static float _startY;
    private static Quaternion _boneStart;
    private static bool _haveLanding;
    private static Quaternion _landingRotation;
    private static Quaternion _landingBone;
    private static float _maxFleeYaw;
    private static float _maxFleeBone;
    private static int _errors;
    private static float _maxDeform;
    private static float _nextDeformSample;
    private static Vector3[] _originalVertices;
    private static readonly List<string> ErrorTexts = new List<string>();

    static PokeTaskSmokeTest()
    {
        EditorApplication.update += OnUpdate;
    }

    [MenuItem("Tools/Poke Task/Run Smoke Test (Play)")]
    public static void RunFromMenu()
    {
        Begin(FrogModelChoice.Animated);
    }

    public static void RunBatch()
    {
        Begin(FrogModelChoice.Animated);
    }

    public static void RunBatchStatic()
    {
        Begin(FrogModelChoice.Static);
    }

    private static void Begin(FrogModelChoice choice)
    {
        SessionState.SetString(StateKey, "enter");
        SessionState.SetInt(ChoiceKey, (int)choice);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            _errors++;
            if (ErrorTexts.Count < 20)
            {
                ErrorTexts.Add($"[{type}] {condition}");
            }
        }
    }

    private static void OnUpdate()
    {
        string state = SessionState.GetString(StateKey, string.Empty);
        if (string.IsNullOrEmpty(state) || state == "done" || !EditorApplication.isPlaying)
        {
            return;
        }

        if (!_hooked)
        {
            _hooked = true;
            _startTime = EditorApplication.timeSinceStartup;
            Application.logMessageReceived += OnLog;
            Debug.Log("[SmokeTest] Play mode entered.");
        }

        var choice = (FrogModelChoice)SessionState.GetInt(ChoiceKey, 0);
        bool rigged = choice == FrogModelChoice.Animated;

        double t = EditorApplication.timeSinceStartup - _startTime;
        var manager = Object.FindObjectOfType<PokeTaskManager>(true);
        GameObject frog = GameObject.Find("FrogModel");
        Transform animatedTransform = frog != null ? frog.transform.Find("FrogAnimated") : null;
        GameObject animated = animatedTransform != null ? animatedTransform.gameObject : null;
        var selector = frog != null ? frog.GetComponent<FrogModelSelector>() : null;

        if (manager == null || frog == null || animated == null || selector == null)
        {
            if (t > 10)
            {
                Finish(false, "PokeTaskManager, FrogModel, FrogAnimated or FrogModelSelector not found");
            }

            return;
        }

        // Choose the frog model first (not remembered).
        if (!_modelSelected && t > 2)
        {
            _modelSelected = true;
            selector.Select(choice, false);
            Debug.Log($"[SmokeTest] frog model: {selector.Current}");
            return;
        }

        var animator = animated.GetComponent<Animator>();
        Transform bone = FindBone(animated.transform, "Bone.013_");

        if (_modelSelected && !_taskStarted && t > 3)
        {
            _taskStarted = true;
            _frogStart = frog.transform.position;
            _startY = _frogStart.y;
            _boneStart = bone != null ? bone.localRotation : Quaternion.identity;

            // Without a screen the renderer is never "visible", so Deform would skip the update. Force it for this check.
            foreach (Deform.Deformable deformable in Object.FindObjectsOfType<Deform.Deformable>(true))
            {
                deformable.CullingMode = Deform.CullingMode.AlwaysUpdate;
            }

            Debug.Log($"[SmokeTest] starting a practice run. Rigged frog active={animated.activeInHierarchy}, " +
                      $"controller: {(animator != null && animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "NONE")}");
            manager.StartTask(true);
        }

        if (_taskStarted)
        {
            if (rigged && animator != null)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                _sawHop |= info.IsName("Hop");
                _sawTurnHop |= info.IsName("TurnHop");
            }

            _maxTravel = Mathf.Max(_maxTravel, Vector3.Distance(frog.transform.position, _frogStart));
            _maxLiftAboveStart = Mathf.Max(_maxLiftAboveStart, frog.transform.position.y - _startY);
            if (bone != null && rigged)
            {
                _maxBoneAngle = Mathf.Max(_maxBoneAngle, Quaternion.Angle(_boneStart, bone.localRotation));
            }

            // Turning around: compare with the pose at the moment of landing.
            if (manager.State == PokeTaskManager.TaskState.Waiting && !_haveLanding)
            {
                _haveLanding = true;
                _landingRotation = frog.transform.rotation;
                _landingBone = bone != null ? bone.localRotation : Quaternion.identity;
            }

            if (_haveLanding && manager.State == PokeTaskManager.TaskState.Fleeing)
            {
                _maxFleeYaw = Mathf.Max(_maxFleeYaw, Quaternion.Angle(_landingRotation, frog.transform.rotation));
                if (bone != null && rigged)
                {
                    _maxFleeBone = Mathf.Max(_maxFleeBone, Quaternion.Angle(_landingBone, bone.localRotation));
                }
            }

            if (rigged)
            {
                SampleDeformation(animated, t);
            }

            bool trialDone = manager.TrialNumber >= 1 && manager.State == PokeTaskManager.TaskState.Interval;
            if (trialDone || t > Timeout)
            {
                manager.StopTask();

                bool common = trialDone && _maxTravel > 4f && _maxFleeYaw > 150f && _errors == 0;
                bool ok = rigged
                    ? common && _sawHop && _sawTurnHop && _maxBoneAngle > 15f && _maxFleeBone > 10f && _maxDeform < 1f &&
                      animated.activeInHierarchy
                    : common && !animated.activeInHierarchy && !_sawHop;
                string reason = $"model={selector.Current} trialDone={trialDone} sawHop={_sawHop} sawTurnHop={_sawTurnHop} " +
                                $"maxTravel={_maxTravel:F1}cm maxBoneAngle={_maxBoneAngle:F0}deg maxLift={_maxLiftAboveStart:F1}cm " +
                                $"turnedBy={_maxFleeYaw:F0}deg legsMovedWhileTurning={_maxFleeBone:F0}deg " +
                                $"maxDeformation={_maxDeform:F3}cm errors={_errors} result={(manager.LastWasHit ? "hit" : "miss")} t={t:F0}s";
                Finish(ok, reason);
            }
        }
    }

    /// <summary>
    ///     How far the deformers (breathing, throat, touch) move a vertex from its original place, in world cm.
    ///     Must stay well below a centimetre; a unit mix-up would make it hundreds.
    /// </summary>
    private static void SampleDeformation(GameObject animated, double t)
    {
        if (t < _nextDeformSample)
        {
            return;
        }

        _nextDeformSample = (float)t + 0.25f;

        var smr = animated.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null || smr.sharedMesh == null)
        {
            return;
        }

        if (_originalVertices == null)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Models/FrogAnimated/frog_schlegel_hop.fbx"))
            {
                if (asset is Mesh mesh && mesh.vertexCount == smr.sharedMesh.vertexCount)
                {
                    _originalVertices = mesh.vertices;
                    break;
                }
            }
        }

        if (_originalVertices == null)
        {
            return;
        }

        Vector3[] current = smr.sharedMesh.vertices;
        if (current.Length != _originalVertices.Length)
        {
            return;
        }

        float unit = smr.transform.lossyScale.x;
        for (var i = 0; i < current.Length; i++)
        {
            float moved = (current[i] - _originalVertices[i]).magnitude * unit;
            if (moved > _maxDeform)
            {
                _maxDeform = moved;
            }
        }
    }

    private static Transform FindBone(Transform root, string prefix)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith(prefix))
            {
                return child;
            }
        }

        return null;
    }

    private static void Finish(bool ok, string summary)
    {
        SessionState.SetString(StateKey, "done");
        Application.logMessageReceived -= OnLog;
        Debug.Log((ok ? "[SmokeTest] PASS: " : "[SmokeTest] FAIL: ") + summary);
        foreach (string text in ErrorTexts)
        {
            Debug.Log("[SmokeTest] error seen: " + text);
        }

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(ok ? 0 : 2);
        }
        else
        {
            EditorApplication.isPlaying = false;
        }
    }
}
