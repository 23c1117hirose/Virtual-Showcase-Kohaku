using UnityEngine;
using UnityEngine.Events;

public static class FlipManager
{
    private const string PREF_KEY = "displayFlipEnabled";

    public static UnityEvent<bool> FlipChanged = new UnityEvent<bool>();

    public static bool IsFlipEnabled
    {
        get => PlayerPrefs.GetInt(PREF_KEY, 0) == 1; // デフォルトはオフ(0)
        private set => PlayerPrefs.SetInt(PREF_KEY, value ? 1 : 0);
    }

    public static void SetFlip(bool enabled)
    {
        IsFlipEnabled = enabled;
        FlipChanged.Invoke(enabled);
    }

    public static void Toggle()
    {
        SetFlip(!IsFlipEnabled);
    }
}