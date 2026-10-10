using UnityEngine;
using Deform;
using Leap;         // 追加
using Leap.Unity;   // 追加

public class FrogTouchController : MonoBehaviour
{
    public Camera targetCamera;
    public Transform touchPoint;
    public TouchPushDeformer touchPush;

    [Header("Dent depth (scene cm at the frog's normal size; the frog's own scale is applied automatically)")]
    [Tooltip("Mouse click: how deep the surface is pressed in.")]
    public float mouseDent = 2.2f;

    [Tooltip("Fingertip: dent when it just touches the surface.")]
    public float leapMinDent = 0.5f;

    [Tooltip("Fingertip: dent when it is pushed in by 'Leap Full Press Depth' or more.")]
    public float leapMaxDent = 2.4f;

    [Tooltip("Fingertip: how far past the surface it has to go for the deepest dent (normal-size cm). " +
             "Between touching and this depth the dent grows evenly.")]
    public float leapFullPressDepth = 2.5f;

    [Tooltip("Fingertip: counts as touching up to this far before the surface (normal-size cm), to forgive tracking noise.")]
    public float leapContactMargin = 0.4f;

    [Tooltip("How wide the dented area is (normal-size cm, around the touch point). Applied to the touch deformer.")]
    public float dentRadius = 2.0f;

    public float smoothSpeed = 8f;
    private float targetFactor = 0f;

    public AudioSource audioSource;
    public AudioClip touchReactionClip;
    public FrogVocalizer vocalizer;

    [Header("Leap Motion")]
    public LeapProvider leapProvider;      // Service Provider (Desktop) をドラッグ
    public Collider frogCollider;          // FrogTouchVolume の Capsule Collider をドラッグ

    private bool _wasTouching = false;

    // ← 追加:触れ始めた瞬間を他スクリプトに通知(評価実験のタスク用。引数は接触点のワールド座標)
    public event System.Action<Vector3> OnTouchStarted;

    // ← 追加:無効化→有効化の際に、古い接触状態が残って「触れ始め」を誤検出しないようにする
    void OnEnable()
    {
        _wasTouching = false;
    }

    void Update()
    {
        if (targetCamera == null) return;

        bool touchingFrog = false;
        Vector3 hitPoint = Vector3.zero;
        Vector3 hitNormal = Vector3.up;
        float dent = 0f; // 凹みの深さ(カエルが通常の大きさのときのcm)

        // タスク中はカエルが縮小されるので、指先の判定距離なども同じ比率で縮める。
        float frogScale = Mathf.Max(transform.root.lossyScale.x, 0.0001f);

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
                    float rayBack = 3f * frogScale; // 指先の少し手前から(値は要調整。カエルの大きさに比例)
                    Vector3 rayOrigin = tip - direction * rayBack;

                    if (Physics.Raycast(rayOrigin, direction, out RaycastHit hit, 10f * frogScale) &&
                        hit.collider.transform.IsChildOf(transform.root))
                    {
                        // 指先が表面より奥に入った深さ(正)。表面の手前にいるときは負。
                        float depth = (rayBack - hit.distance) / frogScale;

                        // 表面に触れた(または、ごく近い)ときだけ「触れた」とする。
                        if (depth >= -leapContactMargin)
                        {
                            hitPoint = hit.point;
                            hitNormal = hit.normal;
                            touchingFrog = true;

                            // 軽く触れたら浅く、グッと押し込んだら深く凹む。
                            float press = Mathf.Clamp01(Mathf.Max(depth, 0f) / Mathf.Max(leapFullPressDepth, 0.01f));
                            dent = Mathf.Lerp(leapMinDent, leapMaxDent, press);
                            break;
                        }
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
                dent = mouseDent;
            }
        }

        // 凹みの広さ(タスク中のカエルは小さいので、見やすいよう広めにする)。モデルを切り替えても常に適用される。
        if (touchPush != null && !Mathf.Approximately(touchPush.Radius, dentRadius))
        {
            touchPush.Radius = dentRadius;
        }

        bool justTouched = touchingFrog && !_wasTouching;

        if (touchingFrog)
        {
            touchPoint.position = hitPoint;
            touchPush.PushDirection = hitNormal;
            targetFactor = -dent; // 負＝表面の内側へへこむ(変形処理側でカエルの拡大率を掛ける)

            if (justTouched && audioSource != null && touchReactionClip != null)
            {
                audioSource.PlayOneShot(touchReactionClip);
            }

            if (justTouched)
            {
                OnTouchStarted?.Invoke(hitPoint); // ← 追加
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
