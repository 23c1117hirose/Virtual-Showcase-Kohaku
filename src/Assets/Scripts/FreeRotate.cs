using UnityEngine;

public class FreeRotate : MonoBehaviour
{
    public float rotateSpeed = 0.3f;   // 回転の感度
    public bool invertY = false;       // 上下方向を反転したい場合はtrueに

    private Vector3 lastMousePosition;
    private bool isDragging = false;

    void Update()
    {
        // 右クリックを押した瞬間
        if (Input.GetMouseButtonDown(1))
        {
            isDragging = true;
            lastMousePosition = Input.mousePosition;
        }

        // 右クリックを離した瞬間
        if (Input.GetMouseButtonUp(1))
        {
            isDragging = false;
        }

        if (isDragging)
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;

            // 横方向のドラッグ → Y軸周りに回転(左右に回す)
            transform.Rotate(Vector3.up, -delta.x * rotateSpeed, Space.World);

            // 縦方向のドラッグ → X軸周りに回転(上下に傾ける)
            float yDir = invertY ? 1f : -1f;
            transform.Rotate(Vector3.right, delta.y * rotateSpeed * yDir, Space.Self);

            lastMousePosition = Input.mousePosition;
        }
    }
}