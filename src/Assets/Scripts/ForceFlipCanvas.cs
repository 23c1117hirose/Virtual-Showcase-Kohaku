using UnityEngine;

public class ForceFlipCanvas : MonoBehaviour
{
    private RectTransform rectTransform;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    void LateUpdate()
    {
        // 毎フレーム、他の処理がすべて終わった後(LateUpdate)に、
        // Scale Yが必ずマイナスになるよう強制する
        Vector3 scale = rectTransform.localScale;
        if (scale.y > 0)
        {
            scale.y = -scale.y;
            rectTransform.localScale = scale;
        }
    }
}