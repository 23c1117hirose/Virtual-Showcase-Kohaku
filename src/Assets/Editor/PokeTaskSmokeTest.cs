using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualShowcase.Showcase;

/// <summary>
///     Automatic check without a screen or a person (also usable in batch mode):
///     Unity -batchmode -nographics -executeMethod PokeTaskSmokeTest.RunBatch        (rigged frog)
///     Unity -batchmode -nographics -executeMethod PokeTaskSmokeTest.RunBatchStatic  (earlier static frog)
///     Unity -batchmode -nographics -executeMethod PokeTaskSmokeTest.RunBatchSwitch  (static frog, then switch to the rigged one)
///     It opens FrogRoom, enters Play mode, selects the frog model, starts one practice trial of the poke task and
///     watches that the frog really does what it should: hops out (rigged: the Animator reaches the Hop state and the
///     leg bones move), travels from the grass to the landing spot, turns around by 180 degrees (rigged: with the
///     TurnHop clip), nothing logs an error, deformations stay small, and the trial ends.
///     The Switch run reproduces "task with one frog, then switch to the other, then task again": the chosen frog must
///     be visible afterwards, a pressed-in dent must have the expected depth, and the second task must work.
///     Exit code 0 = all good.
/// </summary>
[InitializeOnLoad]
public static class PokeTaskSmokeTest
{
    private const string ScenePath = "Assets/Scenes/FrogRoom.unity";
    private const string StateKey = "PokeSmoke.State";
    private const string ChoiceKey = "PokeSmoke.Choice";
    private const string SwitchKey = "PokeSmoke.Switch";
    private const double Timeout = 70.0;

    private static bool _hooked;
    private static double _startTime;
    private static int _step;
    private static double _stepStart;
    private static bool _rigged;
    private static bool _firstOk;
    private static string _firstSummary = string.Empty;
    private static string _extraSummary = string.Empty;
    private static bool _extraOk = true;

    // trackers of the running trial
    private static double _trialStart;
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
    private static float _maxDeform;
    private static float _nextDeformSample;
    private static Vector3[] _originalVertices;

    private static int _errors;
    private static readonly List<string> ErrorTexts = new List<string>();

    static PokeTaskSmokeTest()
    {
        EditorApplication.update += OnUpdate;
    }

    [MenuItem("Tools/Poke Task/Run Smoke Test (Play)")]
    public static void RunFromMenu()
    {
        Begin(FrogModelChoice.Animated, false);
    }

    public static void RunBatch()
    {
        Begin(FrogModelChoice.Animated, false);
    }

    public static void RunBatchStatic()
    {
        Begin(FrogModelChoice.Static, false);
    }

    public static void RunBatchSwitch()
    {
        Begin(FrogModelChoice.Static, true);
    }

    private static void Begin(FrogModelChoice choice, bool switchAfter)
    {
        SessionState.SetString(StateKey, "enter");
        SessionState.SetInt(ChoiceKey, (int)choice);
        SessionState.SetBool(SwitchKey, switchAfter);
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
        bool switchAfter = SessionState.GetBool(SwitchKey, false);
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

        switch (_step)
        {
            case 0: // choose the first frog model (not remembered)
                if (t > 2)
                {
                    selector.Select(choice, false);
                    Debug.Log($"[SmokeTest] frog model: {selector.Current}");
                    _rigged = choice == FrogModelChoice.Animated;
                    _step = 1;
                    _stepStart = t;
                }

                break;

            case 1: // first trial
                if (t - _stepStart > 1)
                {
                    StartTrial(manager, frog, animated, t);
                    _step = 2;
                }

                break;

            case 2:
            {
                if (TrackTrial(manager, frog, animated, t, out string summary, out bool ok))
                {
                    _firstSummary = summary;
                    _firstOk = ok;
                    if (!switchAfter)
                    {
                        Finish(ok, summary);
                    }
                    else
                    {
                        _step = 3;
                        _stepStart = t;
                    }
                }

                break;
            }

            case 3: // switch to the rigged frog after the task, like a person pressing F8
                if (t - _stepStart > 1.5)
                {
                    selector.Select(FrogModelChoice.Animated, false);
                    Debug.Log($"[SmokeTest] switched to: {selector.Current}");
                    _step = 4;
                    _stepStart = t;
                }

                break;

            case 4: // is the rigged frog really there? then press a dent into it
                if (t - _stepStart > 1)
                {
                    CheckChosenFrogVisible(animated, selector);
                    BeginDentTest(frog, animated);
                    _step = 5;
                    _stepStart = t;
                }

                break;

            case 5: // measure the dent, then run a second task with the rigged frog
                if (t - _stepStart > 0.6)
                {
                    EndDentTest(frog, animated);
                    _rigged = true;
                    StartTrial(manager, frog, animated, t);
                    _step = 6;
                }

                break;

            case 6:
            {
                if (TrackTrial(manager, frog, animated, t, out string summary, out bool ok))
                {
                    Finish(_firstOk && ok && _extraOk,
                        $"FIRST[{_firstSummary}] SECOND[{summary}] {_extraSummary}");
                }

                break;
            }
        }
    }

    #region Trial

    private static void StartTrial(PokeTaskManager manager, GameObject frog, GameObject animated, double t)
    {
        _trialStart = t;
        _sawHop = false;
        _sawTurnHop = false;
        _maxTravel = 0f;
        _maxBoneAngle = 0f;
        _maxLiftAboveStart = 0f;
        _haveLanding = false;
        _maxFleeYaw = 0f;
        _maxFleeBone = 0f;
        _maxDeform = 0f;
        _nextDeformSample = (float)t + 1.5f; // a dent pressed in just before has to be pulled back first

        Transform bone = FindBone(animated.transform, "Bone.013_");
        _frogStart = frog.transform.position;
        _startY = _frogStart.y;
        _boneStart = bone != null ? bone.localRotation : Quaternion.identity;

        // Without a screen the renderer is never "visible", so Deform would skip the update. Force it for this check.
        foreach (Deform.Deformable deformable in Object.FindObjectsOfType<Deform.Deformable>(true))
        {
            deformable.CullingMode = Deform.CullingMode.AlwaysUpdate;
        }

        var animator = animated.GetComponent<Animator>();
        Debug.Log($"[SmokeTest] starting a practice run. Rigged frog active={animated.activeInHierarchy}, " +
                  $"controller: {(animator != null && animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "NONE")}");
        manager.StartTask(true);
    }

    /// <returns>True when the trial is over (finished or timed out); then summary and ok are set.</returns>
    private static bool TrackTrial(PokeTaskManager manager, GameObject frog, GameObject animated, double t,
        out string summary, out bool ok)
    {
        summary = string.Empty;
        ok = false;

        var animator = animated.GetComponent<Animator>();
        Transform bone = FindBone(animated.transform, "Bone.013_");

        if (_rigged && animator != null)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            _sawHop |= info.IsName("Hop");
            _sawTurnHop |= info.IsName("TurnHop");
        }

        _maxTravel = Mathf.Max(_maxTravel, Vector3.Distance(frog.transform.position, _frogStart));
        _maxLiftAboveStart = Mathf.Max(_maxLiftAboveStart, frog.transform.position.y - _startY);
        if (bone != null && _rigged)
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
            if (bone != null && _rigged)
            {
                _maxFleeBone = Mathf.Max(_maxFleeBone, Quaternion.Angle(_landingBone, bone.localRotation));
            }
        }

        if (_rigged)
        {
            SampleDeformation(animated, t);
        }

        bool trialDone = manager.TrialNumber >= 1 && manager.State == PokeTaskManager.TaskState.Interval;
        if (!trialDone && t - _trialStart <= Timeout)
        {
            return false;
        }

        manager.StopTask();

        bool common = trialDone && _maxTravel > 4f && _maxFleeYaw > 150f && _errors == 0;
        ok = _rigged
            ? common && _sawHop && _sawTurnHop && _maxBoneAngle > 15f && _maxFleeBone > 10f && _maxDeform < 1f &&
              animated.activeInHierarchy
            : common && !animated.activeInHierarchy && !_sawHop;
        summary = $"rigged={_rigged} trialDone={trialDone} sawHop={_sawHop} sawTurnHop={_sawTurnHop} " +
                  $"maxTravel={_maxTravel:F1}cm maxBoneAngle={_maxBoneAngle:F0}deg maxLift={_maxLiftAboveStart:F1}cm " +
                  $"turnedBy={_maxFleeYaw:F0}deg legsMovedWhileTurning={_maxFleeBone:F0}deg " +
                  $"maxDeformation={_maxDeform:F3}cm errors={_errors} result={(manager.LastWasHit ? "hit" : "miss")}";
        return true;
    }

    #endregion

    #region Switch and dent checks

    private static void CheckChosenFrogVisible(GameObject animated, FrogModelSelector selector)
    {
        var smr = animated.GetComponentInChildren<SkinnedMeshRenderer>(true);
        bool visible = smr != null && smr.enabled && smr.gameObject.activeInHierarchy && animated.activeInHierarchy &&
                       selector.Current == FrogModelChoice.Animated;
        _extraOk &= visible;
        _extraSummary += $"rigged frog visible after switching={visible} ";
        Debug.Log($"[SmokeTest] after the switch: renderer enabled={smr != null && smr.enabled} object active={animated.activeInHierarchy}");
    }

    private static Vector3 _dentPoint;

    /// <summary>Presses a dent of 2.2 cm (the mouse value) into the highest point of the frog, like a click.</summary>
    private static void BeginDentTest(GameObject frog, GameObject animated)
    {
        var touch = frog.GetComponent<FrogTouchController>();
        var smr = animated.GetComponentInChildren<SkinnedMeshRenderer>();
        if (touch == null || smr == null || touch.touchPush == null)
        {
            _extraOk = false;
            _extraSummary += "dent test: missing parts ";
            return;
        }

        float highest = float.MinValue;
        foreach (Vector3 vertex in smr.sharedMesh.vertices)
        {
            Vector3 world = smr.transform.TransformPoint(vertex);
            if (world.y > highest)
            {
                highest = world.y;
                _dentPoint = world;
            }
        }

        _maxDeform = 0f;
        _nextDeformSample = 0f;
        touch.enabled = false; // otherwise it would pull the dent back to zero every frame
        touch.touchPoint.position = _dentPoint;
        touch.touchPush.PushDirection = Vector3.up;
        touch.touchPush.Factor = -touch.mouseDent;
    }

    private static void EndDentTest(GameObject frog, GameObject animated)
    {
        var touch = frog.GetComponent<FrogTouchController>();
        if (touch == null || touch.touchPush == null)
        {
            return;
        }

        SampleDeformation(animated, double.MaxValue); // forces a sample now
        float expected = touch.mouseDent * frog.transform.lossyScale.x;
        bool good = _maxDeform > expected * 0.6f && _maxDeform < expected * 1.4f;
        _extraOk &= good;
        _extraSummary += $"dent={_maxDeform:F2}cm (expected about {expected:F2}) ok={good} ";
        Debug.Log($"[SmokeTest] dent test: moved {_maxDeform:F2} cm, expected about {expected:F2} cm");

        touch.touchPush.Factor = 0f;
        touch.touchPoint.position = new Vector3(0f, 100f, 0f);
        touch.enabled = true;
        _maxDeform = 0f;
    }

    #endregion

    /// <summary>
    ///     How far the deformers (breathing, throat, touch) move a vertex from its original place, in world cm.
    ///     Must stay well below a centimetre while only breathing; a unit mix-up would make it hundreds.
    /// </summary>
    private static void SampleDeformation(GameObject animated, double t)
    {
        if (t < _nextDeformSample)
        {
            return;
        }

        _nextDeformSample = t > 1e15 ? 0f : (float)t + 0.25f;

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
        Debug.Log((ok ? "[SmokeTest] PASS: " : "[SmokeTest] FAIL: ") + summary + $" errors={_errors}");
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
