using UnityEditor;
using UnityEngine;
using VirtualShowcase.Showcase;

/// <summary>
///     Progress display for the experimenter (Tools → Poke Task → Monitor).
///     It lives in its own Unity editor window, so nothing is added to the participant's display
///     (no HUD in the mirrored image). Shows the session, trial progress, what the task is doing
///     right now and the running results, and has buttons to start / abort a run.
/// </summary>
public class PokeTaskMonitor : EditorWindow
{
    private PokeTaskManager _manager;
    private FrogModelSelector _selector;
    private GUIStyle _bigStyle;

    [MenuItem("Tools/Poke Task/Monitor")]
    public static void ShowWindow()
    {
        var window = GetWindow<PokeTaskMonitor>("Poke Task");
        window.minSize = new Vector2(320f, 330f);
    }

    // About 10 times per second.
    private void OnInspectorUpdate()
    {
        Repaint();
    }

    private void OnGUI()
    {
        _bigStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleCenter };

        if (_manager == null)
        {
            _manager = FindObjectOfType<PokeTaskManager>();
        }

        if (_manager == null)
        {
            EditorGUILayout.HelpBox("PokeTaskManager がシーンにありません。\nTools → Poke Task → Create Setup In Scene で作成してください。",
                MessageType.Info);
            return;
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Play すると進行状況が表示されます。", MessageType.Info);
        }

        DrawSession();
        EditorGUILayout.Space();
        DrawModelChoice();
        EditorGUILayout.Space();
        DrawProgress();
        EditorGUILayout.Space();
        DrawResults();
        EditorGUILayout.Space();
        DrawButtons();
    }

    private void DrawSession()
    {
        EditorGUILayout.LabelField("被験者", _manager.ParticipantId);
        EditorGUILayout.LabelField("条件", _manager.ConditionLabel);
        EditorGUILayout.LabelField("出現順 (Sequence)", _manager.SequenceId.ToString());
        EditorGUILayout.LabelField("設定の変更", "PokeTask の Inspector（再生前）");
    }

    /// <summary>Which frog is shown: the rigged one or the earlier static one (the F8 key does the same).</summary>
    private void DrawModelChoice()
    {
        if (_selector == null)
        {
            _selector = FindObjectOfType<FrogModelSelector>();
        }

        if (_selector == null)
        {
            EditorGUILayout.HelpBox("FrogModelSelector がありません（FrogModel に付けると切り替えできます）。", MessageType.None);
            return;
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying || _manager.IsRunning))
        {
            FrogModelChoice current = Application.isPlaying ? _selector.Current : FrogModelChoice.Animated;
            var chosen = (FrogModelChoice)EditorGUILayout.EnumPopup("カエルのモデル (F8)", current);
            if (chosen != current)
            {
                _selector.Select(chosen);
            }
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.LabelField("切り替えはPlay中に行います（選択は保存されます）");
        }
        else if (_manager.IsRunning)
        {
            EditorGUILayout.LabelField("タスク中は切り替えられません");
        }
    }

    private void DrawProgress()
    {
        string phase = _manager.IsPracticeRun ? "練習" : "本番";
        if (!_manager.IsRunning)
        {
            EditorGUILayout.LabelField("待機中", _bigStyle, GUILayout.Height(34f));
        }
        else
        {
            EditorGUILayout.LabelField($"{phase}  {_manager.TrialNumber} / {_manager.TrialCount}", _bigStyle,
                GUILayout.Height(34f));

            float progress = _manager.TrialCount > 0 ? (float)_manager.TrialNumber / _manager.TrialCount : 0f;
            Rect bar = GUILayoutUtility.GetRect(18f, 18f);
            EditorGUI.ProgressBar(bar, progress, string.Empty);
        }

        EditorGUILayout.LabelField("いまの状態", StateLabel(_manager.State));

        string patch = _manager.LastPatch >= 0 ? $"草むら {_manager.LastPatch + 1}" : "-";
        EditorGUILayout.LabelField("今回の草むら", patch);
    }

    private void DrawResults()
    {
        EditorGUILayout.LabelField("命中 / 見逃し", $"{_manager.HitCount} / {_manager.MissCount}");

        string last = "-";
        if (_manager.TrialNumber > 0 && _manager.State is PokeTaskManager.TaskState.Interval
                or PokeTaskManager.TaskState.Idle)
        {
            last = _manager.LastWasHit ? $"命中 ({_manager.LastReaction:F2} 秒)" : "見逃し";
        }

        EditorGUILayout.LabelField("直前の結果（着地から）", last);

        float mean = _manager.MeanReaction;
        EditorGUILayout.LabelField("平均反応時間（着地から）", float.IsNaN(mean) ? "-" : $"{mean:F2} 秒");
    }

    private void DrawButtons()
    {
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_manager.IsRunning))
                {
                    if (GUILayout.Button("練習を開始 (F5)", GUILayout.Height(28f)))
                    {
                        _manager.StartTask(true);
                    }

                    if (GUILayout.Button("本番を開始 (F6)", GUILayout.Height(28f)))
                    {
                        _manager.StartTask(false);
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!_manager.IsRunning))
            {
                if (GUILayout.Button("中断 (F7)", GUILayout.Height(24f)))
                {
                    _manager.StopTask();
                }
            }
        }

        string logPath = _manager.LogPath;
        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(logPath)))
        {
            if (GUILayout.Button("ログのフォルダを開く"))
            {
                EditorUtility.RevealInFinder(logPath);
            }
        }
    }

    private static string StateLabel(PokeTaskManager.TaskState state)
    {
        switch (state)
        {
            case PokeTaskManager.TaskState.Starting:
                return "開始待ち";
            case PokeTaskManager.TaskState.Cue:
                return "先行音（鳴き声）";
            case PokeTaskManager.TaskState.Hopping:
                return "カエルが飛び出し中";
            case PokeTaskManager.TaskState.Waiting:
                return "着地：つつくのを待っている";
            case PokeTaskManager.TaskState.Fleeing:
                return "カエルが逃げ帰り中";
            case PokeTaskManager.TaskState.Interval:
                return "次の試行まで待機";
            default:
                return "-";
        }
    }
}
