using UnityEngine;
using Deform;

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

    void Update()
    {
        if (targetCamera == null) return;

        bool touchingFrog = false; // ← 今フレーム、実際にカエルに触れているか

        if (Input.GetMouseButton(0))
        {
            Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.transform.IsChildOf(transform.root))
                {
                    touchPoint.position = hit.point;
                    touchPush.PushDirection = hit.normal;
                    targetFactor = pushFactor;
                    touchingFrog = true;

                    // クリックした瞬間、かつカエルに当たった時だけ音を鳴らす
                    if (Input.GetMouseButtonDown(0))
                    {
                        if (audioSource != null && touchReactionClip != null)
                        {
                            audioSource.PlayOneShot(touchReactionClip);
                        }
                    }
                }
            }
        }

        if (!touchingFrog)
        {
            targetFactor = 0f;
        }

        // カエルに触れている間だけ自動発音を止める
        if (vocalizer != null)
        {
            vocalizer.isPaused = touchingFrog;
        }

        touchPush.Factor = Mathf.Lerp(touchPush.Factor, targetFactor, Time.deltaTime * smoothSpeed);
    }
}