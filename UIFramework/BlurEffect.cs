using System;
using UnityEngine;
using UnityEngine.Events;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class BlurEffect : MonoBehaviour
{
    [Serializable]
    public class OnTakeSnapshot : UnityEvent<RenderTexture>
    {
    }

    private const int VerticalBlurPass = 0;
    private const int HorizontalBlurPass = 1;
    private const int BlendPass = 2;

    private static readonly int Size = Shader.PropertyToID("_Size");
    private static readonly int Color = Shader.PropertyToID("_Color");

    public bool enableBlur;
    [Range(0, 20)] public float blurSize;
    public Color blurColor = UnityEngine.Color.white;

    public OnTakeSnapshot OnTakeSnapshotCallback = new OnTakeSnapshot();

    private bool enableSnapshot;
    private RenderTexture snapshot;

    [SerializeField] private Material material;
    [SerializeField, Range(1, 8)] private int sampleQuality = 4;

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        RenderTexture renderTexture = null;
        sampleQuality = Mathf.Max(1, sampleQuality);

        if (material != null && (enableSnapshot || enableBlur))
        {
            material.SetFloat(Size, blurSize);
            material.SetColor(Color, blurColor);
            var width = Screen.width / sampleQuality;
            var height = Screen.height / sampleQuality;
            renderTexture = RenderTexture.GetTemporary(width, height);
            var temporary = RenderTexture.GetTemporary(width, height);

            Graphics.Blit(src, renderTexture, material, VerticalBlurPass);
            Graphics.Blit(renderTexture, temporary, material, HorizontalBlurPass);
            Graphics.Blit(temporary, renderTexture, material, BlendPass);

            temporary.Release();
            RenderTexture.ReleaseTemporary(temporary);
        }

        if (enableSnapshot)
        {
            Graphics.Blit(renderTexture, snapshot);
            if (OnTakeSnapshotCallback != null) OnTakeSnapshotCallback.Invoke(snapshot);
            enableSnapshot = false;
        }

        Graphics.Blit(!enableBlur ? src : renderTexture, dest);

        if (renderTexture != null)
        {
            renderTexture.Release();
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }

    public void EnableSnaleshot()
    {
        if (enableSnapshot) return;

        enableSnapshot = true;
        sampleQuality = Mathf.Max(1, sampleQuality);
        var width = Screen.width / sampleQuality;
        var height = Screen.height / sampleQuality;
        if (snapshot == null || width != snapshot.width || height != snapshot.height)
        {
            if (snapshot != null)
            {
                snapshot.Release();
                RenderTexture.ReleaseTemporary(snapshot);
                snapshot = null;
            }

            snapshot = RenderTexture.GetTemporary(width, height);
        }
    }
}