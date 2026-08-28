using UnityEngine;

public class FrogVocalizer : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip[] croakClips;
    public float minInterval = 5f;
    public float maxInterval = 15f;

    [HideInInspector]
    public bool isPaused = false; // ← 追加:接触中はtrueにする

    public event System.Action<AudioClip> OnCroak; // ← 追加:鳴いたタイミングを他スクリプトに通知(喉袋の膨らみ用)

    void Start()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        ScheduleNextCroak();
    }

    void ScheduleNextCroak()
    {
        float delay = Random.Range(minInterval, maxInterval);
        Invoke(nameof(Croak), delay);
    }

    void Croak()
    {
        if (!isPaused) // ← 一時停止中でなければ再生
        {
            if (croakClips != null && croakClips.Length > 0)
            {
                audioSource.clip = croakClips[Random.Range(0, croakClips.Length)];
            }
            audioSource.Play();
            OnCroak?.Invoke(audioSource.clip); // ← 追加
        }
        ScheduleNextCroak(); // タイマー自体は止めず、次回のチェックに委ねる
    }
}