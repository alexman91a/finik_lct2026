using TMPro;
using UnityEngine;

namespace Finik.UI.Onboarding
{
    /// <summary>Reveals a TMP text character by character, like Finik is talking. Tap to finish instantly.</summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class FinikTypewriter : MonoBehaviour
    {
        [SerializeField] float charactersPerSecond = 45f;
        [SerializeField] float punctuationPause = 0.12f;

        TMP_Text text;
        float visible;
        float pauseUntil;
        int total;

        public bool IsDone => !text || text.maxVisibleCharacters >= total;

        void Awake() => text = GetComponent<TMP_Text>();

        public void Play(string value)
        {
            if (!text) Awake();
            text.text = value;
            text.ForceMeshUpdate();
            total = text.textInfo.characterCount;
            visible = 0f;
            pauseUntil = 0f;
            text.maxVisibleCharacters = 0;
        }

        public void Complete()
        {
            if (!text) return;
            visible = total;
            text.maxVisibleCharacters = total;
        }

        void Update()
        {
            if (!text || text.maxVisibleCharacters >= total) return;
            if (Time.unscaledTime < pauseUntil) return;
            visible += Time.unscaledDeltaTime * charactersPerSecond;
            int shown = Mathf.Min(total, Mathf.FloorToInt(visible));
            if (shown == text.maxVisibleCharacters) return;
            text.maxVisibleCharacters = shown;
            char last = shown > 0 ? text.textInfo.characterInfo[shown - 1].character : ' ';
            if (last is '.' or ',' or '!' or '?' or '—') pauseUntil = Time.unscaledTime + punctuationPause;
        }
    }
}
