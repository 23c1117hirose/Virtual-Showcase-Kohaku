using UnityEngine;
using UnityEngine.Rendering;

public class FlipCullingCorrector : MonoBehaviour
{
    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        GL.invertCulling = FlipManager.IsFlipEnabled;
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        GL.invertCulling = false;
    }
}