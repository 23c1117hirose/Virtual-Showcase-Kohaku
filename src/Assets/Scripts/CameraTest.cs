using UnityEngine;

public class CameraTest : MonoBehaviour
{
    void Start()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        Debug.Log("検出されたカメラの数: " + devices.Length);
        for (int i = 0; i < devices.Length; i++)
        {
            Debug.Log("カメラ " + i + ": " + devices[i].name);
        }
    }
}