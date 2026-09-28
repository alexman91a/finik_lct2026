using System;
using Finik.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Games
{
    /// <summary>
    /// One game on the hub: its picture, what it is, and the three difficulty steps with what each is
    /// worth. Each step is in one of three states, and only one of them is loud:
    /// <list type="bullet">
    /// <item>the next step to play is the green button with a soft glow — the one accent of the card;</item>
    /// <item>a cleared step is a light tile with a medal inside it: done, but open for another go;</item>
    /// <item>a shut step is a faded tile with a padlock where the reward would be.</item>
    /// The reward — mood icon and amount — looks the same on every open step.
    /// </list>
    /// </summary>
    public sealed class FinikGameCardView : MonoBehaviour
    {
        [Serializable]
        public struct LevelChip
        {
            public Button button;
            /// <summary>The green button of the step to play next.</summary>
            public Image face;
            /// <summary>The light tile a cleared or a shut step sits on.</summary>
            public Image tile;
            public TMP_Text label;
            /// <summary>Mood icon and "+3" together, hidden while the step is shut.</summary>
            public GameObject rewardGroup;
            /// <summary>The mood icon in front of the amount; a cleared step drops it for the medal's room.</summary>
            public GameObject rewardIcon;
            public TMP_Text reward;
            /// <summary>Medal inside the chip, left of the name, once the step is cleared.</summary>
            public GameObject done;
            /// <summary>Shown instead of the reward while the step is still shut.</summary>
            public GameObject locked;
            /// <summary>Soft light behind the step to play next.</summary>
            public GameObject glow;
        }

        /// <summary>
        /// An endless game's card: no row of steps, just the level the child is on and one green
        /// «Играть». Replaying old levels or staring at a padlock adds nothing to a game without an end.
        /// </summary>
        [Serializable]
        public struct NextLevel
        {
            public Button play;
            /// <summary>"Уровень 14 · лёгкий" on the tile left of the button.</summary>
            public TMP_Text title;
            public TMP_Text reward;
        }

        [SerializeField] string gameId;
        [SerializeField] NextLevel next;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text subtitle;
        [SerializeField] LevelChip[] chips = Array.Empty<LevelChip>();
        [Tooltip("Tint over a shut step's face: the same tile as a cleared one, faded.")]
        [SerializeField] Color lockedTint = Color.white;
        [Tooltip("Text on the green button: white with the outline.")]
        [SerializeField] Color nextLabel = Color.white;
        [SerializeField] Material labelOnButton;
        [Tooltip("Text on a light tile: plain ink.")]
        [SerializeField] Color clearedLabel = new(0.12f, 0.15f, 0.36f, 1f);
        [SerializeField] Color lockedLabel = new(0.36f, 0.41f, 0.62f, 0.8f);
        [SerializeField] Material labelOnTile;
        [Tooltip("Left inset of the step's name, without and with the medal in front of it.")]
        [SerializeField] float labelInset = 22f;
        [SerializeField] float labelInsetWithMedal = 56f;

        /// <summary>Game id and level number (1-based) the child asked for.</summary>
        public event Action<string, int> LevelPicked;

        public string GameId => gameId;

        public void Configure(string id, Image cardIcon, TMP_Text cardTitle, TMP_Text cardSubtitle, LevelChip[] levelChips,
            NextLevel nextLevel = default)
        {
            next = nextLevel;
            gameId = id;
            icon = cardIcon;
            title = cardTitle;
            subtitle = cardSubtitle;
            chips = levelChips;
        }

        /// <summary>
        /// Level number behind each chip. An endless game's card slides along as the player goes, so a
        /// chip is read at the moment it is pressed, not fixed when it was built.
        /// </summary>
        [NonSerialized] int[] numbers;

        void Awake() => Hook();

        void Hook()
        {
            if (next.play)
            {
                next.play.onClick.RemoveAllListeners();
                next.play.onClick.AddListener(() =>
                    LevelPicked?.Invoke(gameId, numbers != null && numbers.Length > 0 ? numbers[0] : 1));
            }
            for (int i = 0; i < chips.Length; i++)
            {
                int index = i;
                var button = chips[i].button;
                if (!button) continue;
                // The builder leaves the chips bare; clearing first keeps a rebuilt card from firing twice.
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                    LevelPicked?.Invoke(gameId, numbers != null && index < numbers.Length ? numbers[index] : index + 1));
            }
        }

        static void Paint(TMP_Text text, Color color, Material material)
        {
            text.color = color;
            if (material && text.fontSharedMaterial != material) text.fontSharedMaterial = material;
        }

        /// <summary>Fills the card in from the catalog and how far the player got.</summary>
        public void Show(FinikMiniGame game, Sprite picture, int bestLevel)
        {
            if (game == null) return;
            gameId = game.Id;
            if (icon)
            {
                if (picture) icon.sprite = picture;
                icon.gameObject.SetActive(icon.sprite);
            }
            if (title) title.text = FinikTypography.Fix(game.Title);
            if (subtitle) subtitle.text = FinikTypography.Fix(game.Subtitle);

            if (game.Endless && next.play)
            {
                numbers = game.HubLevels(bestLevel, 1);
                var level = FinikMiniGameCatalog.Level(game, numbers[0]);
                if (next.title) next.title.text = level.Heading;
                if (next.reward) next.reward.text = $"+{level.Mood}";
                return;
            }
            numbers = game.HubLevels(bestLevel, chips.Length);
            for (int i = 0; i < chips.Length; i++)
            {
                var chip = chips[i];
                var level = FinikMiniGameCatalog.Level(game, numbers[i]);
                bool used = level != null;
                if (chip.button) chip.button.gameObject.SetActive(used);
                if (!used) continue;

                bool cleared = bestLevel >= level.Number;
                bool unlocked = FinikMiniGameCatalog.Unlocked(level.Number, bestLevel);
                if (chip.button) chip.button.interactable = unlocked;
                bool next = unlocked && !cleared;
                if (chip.face) chip.face.gameObject.SetActive(next);
                if (chip.tile)
                {
                    chip.tile.gameObject.SetActive(!next);
                    chip.tile.color = unlocked ? Color.white : lockedTint;
                }
                var color = next ? nextLabel : cleared ? clearedLabel : lockedLabel;
                var material = next ? labelOnButton : labelOnTile;
                if (chip.label)
                {
                    chip.label.text = level.Title;
                    Paint(chip.label, color, material);
                    var min = chip.label.rectTransform.offsetMin;
                    min.x = cleared ? labelInsetWithMedal : labelInset;
                    chip.label.rectTransform.offsetMin = min;
                }
                if (chip.reward)
                {
                    chip.reward.text = $"+{level.Mood}";
                    Paint(chip.reward, color, material);
                }
                if (chip.rewardGroup) chip.rewardGroup.SetActive(unlocked);
                if (chip.rewardIcon) chip.rewardIcon.SetActive(true);
                if (chip.done) chip.done.SetActive(cleared);
                if (chip.locked) chip.locked.SetActive(!unlocked);
                if (chip.glow) chip.glow.SetActive(next);
            }
        }
    }
}
