using System;
using System.Collections;
using Finik.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Quests
{
    /// <summary>
    /// «Что ты покупаешь на самом деле?» (web: MysteryOdds): a grid of closed boxes; «Показать шансы»
    /// opens them one by one (only a few hold the rare prize) and the question above them turns into
    /// the explanation of what the price really buys. The decision buttons belong to the screen's footer.
    /// </summary>
    public sealed class FinikOddsView : MonoBehaviour
    {
        [SerializeField] TMP_Text kicker;
        [SerializeField] TMP_Text prompt;
        [SerializeField] GameObject[] cells = Array.Empty<GameObject>();
        [SerializeField] Image[] faces = Array.Empty<Image>();
        [SerializeField] TMP_Text[] marks = Array.Empty<TMP_Text>();
        [SerializeField] Image[] prizes = Array.Empty<Image>();
        [SerializeField] Sprite rareSprite;
        [SerializeField] Sprite plainSprite;
        [SerializeField] Color closedColor = new(0.93f, 0.95f, 1f, 1f);
        [SerializeField] Color rareColor = new(1f, 0.9f, 0.55f, 1f);
        [SerializeField] Color plainColor = Color.white;
        [SerializeField, Min(0f)] float revealStep = 0.12f;

        FinikQuestMechanic mechanic;
        Coroutine revealing;

        public bool Revealed { get; private set; }

        public void Configure(TMP_Text kickerText, TMP_Text promptText, GameObject[] boxes, Image[] boxFaces, TMP_Text[] questionMarks, Image[] prizeImages,
            Sprite rare, Sprite plain)
        {
            kicker = kickerText;
            prompt = promptText;
            cells = boxes;
            faces = boxFaces;
            marks = questionMarks;
            prizes = prizeImages;
            rareSprite = rare;
            plainSprite = plain;
        }

        public void Show(FinikQuestMechanic settings)
        {
            mechanic = settings;
            Revealed = false;
            if (revealing != null) StopCoroutine(revealing);
            revealing = null;
            if (kicker) kicker.text = settings.Kicker;
            if (prompt) prompt.text = FinikTypography.Fix(settings.Prompt);
            for (int i = 0; i < cells.Length; i++)
            {
                if (!cells[i]) continue;
                cells[i].SetActive(i < settings.Cells);
                SetCell(i, open: false);
            }
        }

        /// <summary>Opens the boxes; <paramref name="instant"/> skips the one-by-one animation.</summary>
        public void Reveal(bool instant = false)
        {
            if (mechanic == null || Revealed) return;
            Revealed = true;
            if (revealing != null) StopCoroutine(revealing);
            revealing = null;
            if (instant || !isActiveAndEnabled)
            {
                for (int i = 0; i < mechanic.Cells && i < cells.Length; i++) SetCell(i, open: true);
                ShowSummary();
                return;
            }
            revealing = StartCoroutine(RevealRoutine());
        }

        IEnumerator RevealRoutine()
        {
            for (int i = 0; i < mechanic.Cells && i < cells.Length; i++)
            {
                SetCell(i, open: true);
                StartCoroutine(Pop(cells[i].transform));
                yield return new WaitForSecondsRealtime(revealStep);
            }
            ShowSummary();
            revealing = null;
        }

        void ShowSummary()
        {
            if (prompt) prompt.text = FinikTypography.Fix(mechanic.Summary);
        }

        static IEnumerator Pop(Transform target)
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.35f)
            {
                float s = Mathf.LerpUnclamped(0.7f, 1f, FinikUiMotion.EaseOutBack(t, 2.4f));
                target.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            target.localScale = Vector3.one;
        }

        /// <summary>The rare prizes sit in the first boxes, as in the web example (crown first).</summary>
        void SetCell(int index, bool open)
        {
            bool rare = mechanic != null && index < mechanic.Rare;
            if (index < faces.Length && faces[index]) faces[index].color = !open ? closedColor : rare ? rareColor : plainColor;
            if (index < marks.Length && marks[index]) marks[index].gameObject.SetActive(!open);
            if (index < prizes.Length && prizes[index])
            {
                prizes[index].gameObject.SetActive(open);
                prizes[index].sprite = rare ? rareSprite : plainSprite;
            }
            if (!open && index < cells.Length && cells[index]) cells[index].transform.localScale = Vector3.one;
        }
    }
}
