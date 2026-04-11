using System;
using System.Collections.Generic;
using UnityEngine;

public class SoundManager : MonoBehaviour
{
    [Serializable]
    private class AudioEntry
    {
        public string id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    public static SoundManager Instance { get; private set; }

    [Header("Sources")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;

    [Header("Library")]
    [SerializeField] private List<AudioEntry> bgmLibrary = new List<AudioEntry>();
    [SerializeField] private List<AudioEntry> sfxLibrary = new List<AudioEntry>();

    [Header("Mixer")]
    [SerializeField, Range(0f, 1f)] private float masterBgmVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float masterSfxVolume = 1f;

    private readonly Dictionary<string, AudioEntry> bgmMap = new Dictionary<string, AudioEntry>();
    private readonly Dictionary<string, AudioEntry> sfxMap = new Dictionary<string, AudioEntry>();

    public string CurrentBgmId { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        CacheLibrary();
        EnsureSources();
        ApplySourceDefaults();
    }

    void OnValidate()
    {
        masterBgmVolume = Mathf.Clamp01(masterBgmVolume);
        masterSfxVolume = Mathf.Clamp01(masterSfxVolume);

        if (!Application.isPlaying)
            return;

        CacheLibrary();
        ApplySourceDefaults();
    }

    public void PlayBgm(string id, bool loop = true)
    {
        if (bgmSource == null || !TryGetEntry(bgmMap, id, out AudioEntry entry))
            return;

        if (CurrentBgmId == id && bgmSource.isPlaying)
            return;

        CurrentBgmId = id;
        bgmSource.clip = entry.clip;
        bgmSource.loop = loop;
        bgmSource.volume = entry.volume * masterBgmVolume;
        bgmSource.Play();
    }

    public void StopBgm()
    {
        CurrentBgmId = null;

        if (bgmSource != null)
            bgmSource.Stop();
    }

    public void PlaySfx(string id)
    {
        if (sfxSource == null || !TryGetEntry(sfxMap, id, out AudioEntry entry))
            return;

        sfxSource.PlayOneShot(entry.clip, entry.volume * masterSfxVolume);
    }

    public void SetBgmVolume(float volume)
    {
        masterBgmVolume = Mathf.Clamp01(volume);

        if (bgmSource != null && bgmSource.clip != null && TryGetEntry(bgmMap, CurrentBgmId, out AudioEntry entry))
            bgmSource.volume = entry.volume * masterBgmVolume;
    }

    public void SetSfxVolume(float volume)
    {
        masterSfxVolume = Mathf.Clamp01(volume);
    }

    void EnsureSources()
    {
        if (bgmSource == null)
            bgmSource = CreateChildSource("BgmSource");

        if (sfxSource == null)
            sfxSource = CreateChildSource("SfxSource");
    }

    AudioSource CreateChildSource(string sourceName)
    {
        Transform child = transform.Find(sourceName);
        GameObject go = child != null ? child.gameObject : new GameObject(sourceName);
        go.transform.SetParent(transform, false);

        AudioSource source = go.GetComponent<AudioSource>();
        if (source == null)
            source = go.AddComponent<AudioSource>();

        return source;
    }

    void ApplySourceDefaults()
    {
        if (bgmSource != null)
        {
            bgmSource.playOnAwake = false;
            bgmSource.loop = true;
            bgmSource.spatialBlend = 0f;
        }

        if (sfxSource != null)
        {
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
        }
    }

    void CacheLibrary()
    {
        RebuildMap(bgmLibrary, bgmMap);
        RebuildMap(sfxLibrary, sfxMap);
    }

    static void RebuildMap(List<AudioEntry> source, Dictionary<string, AudioEntry> target)
    {
        target.Clear();

        for (int i = 0; i < source.Count; i++)
        {
            AudioEntry entry = source[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.id) || entry.clip == null)
                continue;

            target[entry.id] = entry;
        }
    }

    static bool TryGetEntry(Dictionary<string, AudioEntry> map, string id, out AudioEntry entry)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            entry = null;
            return false;
        }

        return map.TryGetValue(id, out entry);
    }
}
