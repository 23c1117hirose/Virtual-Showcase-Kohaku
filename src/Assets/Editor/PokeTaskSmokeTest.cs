using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualShowcase.Showcase;

/// <summary>
///     Automatic check without a screen or a person (also usable in batch mode):
///     Unity -batchmode -nographics -executeMethod PokeTaskSmokeTest.RunBatch
///     It opens FrogRoom, enters Play mode, starts one practice trial of the poke task and watches that
///     the frog really hops: the Animator reaches the Hop state, the leg bones move, the frog travels
///     from the grass to the landing spot, nothing logs an error, and the trial ends. Exit code 0 = all good.
/// </summary>
[InitializeOnLoad]
public static class PokeTaskSmokeTest
{
    private const string ScenePath = "Assets/Scenes/FrogRoom.unity";
    private const string StateKey = "PokeSmoke.State";
    private const double Timeout = 70.0;

    private static bool _hooked;
    private static double _startTime;
    private static bool _taskStarted;
    private static bool _sawHop;
    private static float _maxTravel;
    private static float _maxBoneAngle;
    private static float _maxLiftAboveStart;
    private static Vector3 _frogStart;
    private static float _startY;
    private static Quaternion _boneStart;
    private static int _errors;
    private static readonly List<string> ErrorTexts = new List<string>();

    static PokeTaskSmokeTest()
    {
        EditorApplication.update += OnUpdate;
    }

    [MenuItem("Tools/Poke Task/Run Smoke Test (Play)")]
    public static void RunFromMenu()
    {
        Begin();
    }

    public static void RunBatch()
    {
        Begin();
    }

    private static void Begin()
    {
        SessionState.SetString(StateKey, "enter");
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

        double t = EditorApplication.timeSinceStartup - _startTime;
        var manager = Object.FindObjectOfType<PokeTaskManager>(true);
        GameObject frog = GameObject.Find("FrogModel");
        GameObject animated = frog != null ? frog.transform.Find("FrogAnimated")?.gameObject : null;

        if (manager == null || frog == null || animated == null)
        {
            if (t > 10)
            {
                Finish(false, "PokeTaskManager, FrogModel or FrogAnimated not found");
            }

            return;
        }

        var animator = animated.GetComponent<Animator>();
        Transform bone = FindBone(animated.transform, "Bone.013_");

        if (!_taskStarted && t > 3)
        {
            _taskStarted = true;
            _frogStart = frog.transform.position;
            _startY = _frogStart.y;
            _boneStart = bone != null ? bone.localRotation : Quaternion.identity;
            Debug.Log($"[SmokeTest] starting a practice run. Animator controller: " +
                      $"{(animator != null && animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "NONE")}");
            manager.StartTask(true);
        }

        if (_taskStarted)
        {
            if (animator != null && animator.GetCurrentAnimatorStateInfo(0).IsName("Hop"))
            {
                _sawHop = true;
            }

            _maxTravel = Mathf.Max(_maxTravel, Vector3.Distance(frog.transform.position, _frogStart));
            _maxLiftAboveStart = Mathf.Max(_maxLiftAboveStart, frog.transform.position.y - _startY);
            if (bone != null)
            {
                _maxBoneAngle = Mathf.Max(_maxBoneAngle, Quaternion.Angle(_boneStart, bone.localRotation));
            }

            bool trialDone = manager.TrialNumber >= 1 && manager.State == PokeTaskManager.TaskState.Interval;
            if (trialDone || t > Timeout)
            {
                manager.StopTask();
                bool ok = trialDone && _sawHop && _maxTravel > 4f && _maxBoneAngle > 15f && _errors == 0;
                string reason = $"trialDone={trialDone} sawHop={_sawHop} maxTravel={_maxTravel:F1}cm " +
                                $"maxBoneAngle={_maxBoneAngle:F0}deg maxLift={_maxLiftAboveStart:F1}cm errors={_errors} " +
                                $"result={(manager.LastWasHit ? "hit" : "miss")} t={t:F0}s";
                Finish(ok, reason);
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
