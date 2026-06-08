using System.Collections.Generic;
using UnityEngine;

namespace Gamex.Game
{
    // Tiny one-shot SFX singleton. Loads clips lazily from Resources/Audio/
    // and plays them through a single shared AudioSource so we never spawn
    // GameObjects per click. PlayOneShot lets multiple SFX overlap (e.g.
    // tap + coin chime when the player buys an outfit).
    //
    // Throttle: same clip can't fire more than once every THROTTLE_S seconds,
    // so a fast double-tap on the home buttons doesn't double the click sound.
    public static class Sfx
    {
        const float THROTTLE_S = 0.04f;
        const float DEFAULT_VOLUME = 0.65f;

        // Global mute, mirrored from PlayerState.sfxMuted by GameRunner so the
        // Settings panel can flip it without every Sfx.Play call site needing
        // to check state. Default true so audio is on for the very first frame
        // before the runner finishes loading save data.
        public static bool Enabled = true;

        static AudioSource _src;
        static readonly Dictionary<string, AudioClip> _clipCache = new();
        static readonly Dictionary<string, float>     _lastPlayed = new();

        static AudioSource Source()
        {
            if (_src != null) return _src;
            var go = new GameObject("SfxSource");
            Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.volume = DEFAULT_VOLUME;
            return _src;
        }

        public static void Play(string name, float volume = 1f)
        {
            if (!Enabled) return;
            if (string.IsNullOrEmpty(name)) return;
            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(name, out var t) && now - t < THROTTLE_S) return;
            _lastPlayed[name] = now;

            if (!_clipCache.TryGetValue(name, out var clip))
            {
                clip = Resources.Load<AudioClip>("Audio/" + name);
                _clipCache[name] = clip;   // cache nulls too so a misspelled name
                                            // doesn't re-hit Resources every play.
            }
            if (clip != null) Source().PlayOneShot(clip, volume * DEFAULT_VOLUME);
        }
    }

    // Background music — single looping AudioSource on its own GameObject.
    // PlayLoop(name) is idempotent: re-calling with the same clip is a no-op
    // so we can poke it every Refresh without restarting the track.
    public static class Bgm
    {
        const float DEFAULT_VOLUME = 0.32f;   // sits well behind SFX

        // Mirrored from PlayerState.bgmMuted (see Sfx.Enabled rationale).
        // When flipped to false mid-track, the next PlayLoop call early-exits
        // — but a Stop() is needed to halt audio already playing, so Settings
        // panel calls Bgm.Stop() right after flipping this.
        public static bool Enabled = true;

        static UnityEngine.AudioSource _src;
        static string _current;

        static UnityEngine.AudioSource Source()
        {
            if (_src != null) return _src;
            var go = new UnityEngine.GameObject("BgmSource");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<UnityEngine.AudioSource>();
            _src.playOnAwake = false;
            _src.loop = true;
            _src.volume = DEFAULT_VOLUME;
            return _src;
        }

        public static void PlayLoop(string name)
        {
            if (!Enabled) { Stop(); return; }
            if (string.IsNullOrEmpty(name)) { Stop(); return; }
            if (_current == name && Source().isPlaying) return;
            var clip = UnityEngine.Resources.Load<UnityEngine.AudioClip>("Audio/" + name);
            if (clip == null) return;
            Source().clip = clip;
            Source().Play();
            _current = name;
        }

        public static void Stop()
        {
            if (_src == null) return;
            _src.Stop();
            _current = null;
        }
    }
}
