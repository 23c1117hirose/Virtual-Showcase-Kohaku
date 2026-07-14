using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;

public class SceneSwitcher : EditorWindow
{
    [MenuItem("Tools/Scene Switcher")]
    public static void ShowWindow()
    {
        GetWindow<SceneSwitcher>("Scene Switcher");
    }

    private void OnGUI()
    {
        GUILayout.Label("シーンのクイック切り替え（自動取得）", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 「Assets/Scenes」フォルダの中にあるシーンファイルを自動検索
        string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
        
        if (guids.Length == 0)
        {
            GUILayout.Label("Assets/Scenes フォルダにシーンが見つかりません。");
            return;
        }

        // 見つかったシーンを順番にボタンにしていく
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string sceneName = Path.GetFileNameWithoutExtension(path);

            // ボタンの作成
            if (GUILayout.Button(sceneName, GUILayout.Height(30)))
            {
                // 編集中なら保存するか確認を挟んでからシーンを開く
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    EditorSceneManager.OpenScene(path);
                }
            }
        }
    }
}