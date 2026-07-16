using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class FlipCanvas : MonoBehaviour
{
    private RectTransform rectTransform;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    void LateUpdate()
    {
        Vector3 scale = rectTransform.localScale;
        bool shouldBeNegative = FlipManager.IsFlipEnabled;
        bool isNegative = scale.y < 0;

        if (shouldBeNegative != isNegative)
        {
            scale.y = -scale.y;
            rectTransform.localScale = scale;
        }
    }
}