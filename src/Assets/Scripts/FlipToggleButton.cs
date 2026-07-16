using UnityEngine;
using TMPro;

public class FlipToggleButton : MonoBehaviour
{
    [SerializeField]
    private TMP_Text buttonText;

    void Start()
    {
        UpdateLabel();
    }

    // ボタンのOnClickから呼ぶ
    public void OnToggleClicked()
    {
        FlipManager.Toggle();
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        if (buttonText != null)
        {
            buttonText.text = FlipManager.IsFlipEnabled ? "FLIP: ON" : "FLIP: OFF";
        }
    }
}