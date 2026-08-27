using UnityEngine;
using Deform;
using Leap;         // 追加
using Leap.Unity;   // 追加

public class FrogTouchController : MonoBehaviour
{
    public Camera targetCamera;
    public Transform touchPoint;
    public TouchPushDeformer touchPush;
    public Vector3 restPosition = new Vector3(0, 100, 0);

    public float pushFactor = -0.5f;
    public float smoothSpeed = 8f;
    private float targetFactor = 0f;

    public AudioSource audioSource;
    public AudioClip touchReactionClip;
    public FrogVocalizer vocalizer;

    [Header("Leap Motion")]
    public LeapProvider leapProvider;      // Service Provider (Desktop) をドラッグ
    public Collider frogCollider;          // Object_6 のCollider をドラッグ
    public float leapTouchThreshold = 0.02f; // 接触とみなす距離(m)。実機で調整

    private bool _wasTouching = false;

    void Update()
    {
        if (targetCamera == null) return;

        bool touchingFrog = false;
        Vector3 hitPoint = Vector3.zero;
        Vector3 hitNormal = Vector3.up;

        // --- ① Leap Motion（優先） ---
        if (leapProvider != null && frogCollider != null)
        {
            foreach (Hand hand in leapProvider.CurrentFrame.Hands)
            {
                Finger indexFinger = hand.Fingers[1];
                Vector3 tip = indexFinger.TipPosition;

                // 大まかに「カエルの近くにいるか」だけ確認(軽い事前チェック)
                if (frogCollider.bounds.Contains(tip))
                {
                    // 正確な接触点は、指の向きに沿って実メッシュへRaycastして求める
                    Vector3 direction = indexFinger.Direction;
                    Vector3 rayOrigin = tip - direction * 3f; // 指先の少し手前から(値は要調整)

                    if (Physics.Raycast(rayOrigin, direction, out RaycastHit hit, 10f) &&
                        hit.collider.transform.IsChildOf(transform.root))
                    {
                        hitPoint = hit.point;
                        hitNormal = hit.normal;
                        touchingFrog = true;
                        break;
                    }
                }
            }
        }

        // --- ② マウス（Leapで触れていない時のフォールバック） ---
        if (!touchingFrog && Input.GetMouseButton(0))
        {
            Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.transform.IsChildOf(transform.root))
            {
                hitPoint = hit.point;
                hitNormal = hit.normal;
                touchingFrog = true;
            }
        }

        bool justTouched = touchingFrog && !_wasTouching;

        if (touchingFrog)
        {
            touchPoint.position = hitPoint;
            touchPush.PushDirection = hitNormal;
            targetFactor = pushFactor;

            if (justTouched && audioSource != null && touchReactionClip != null)
            {
                audioSource.PlayOneShot(touchReactionClip);
            }
        }
        else
        {
            targetFactor = 0f;
        }

        if (vocalizer != null)
        {
            vocalizer.isPaused = touchingFrog;
        }

        touchPush.Factor = Mathf.Lerp(touchPush.Factor, targetFactor, Time.deltaTime * smoothSpeed);
        _wasTouching = touchingFrog;
    }
}
