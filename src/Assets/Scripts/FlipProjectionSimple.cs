using UnityEngine;

[RequireComponent(typeof(Camera))]
public class FlipProjectionSimple : MonoBehaviour
{
    void Start()
    {
        Camera cam = GetComponent<Camera>();
        Matrix4x4 flip = Matrix4x4.Scale(new Vector3(1, -1, 1));
        cam.projectionMatrix = flip * cam.projectionMatrix;
    }
}