using UnityEngine;

public class TestLoopPlayer : MonoBehaviour
{
    private AudioSource audioSource;
    public Transform listenerReference; // AudioListenerPivotをドラッグ

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        InvokeRepeating(nameof(PlayLoop), 0f, 2f);
    }

    void PlayLoop()
    {
        if (listenerReference != null)
        {
            float dist = Vector3.Distance(transform.position, listenerReference.position);
            Debug.Log("カエルとリスナーの距離: " + dist);
        }
        audioSource.Play();
    }
}