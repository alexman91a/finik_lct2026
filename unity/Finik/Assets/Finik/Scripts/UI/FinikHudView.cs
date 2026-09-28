using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI
{
    public enum FinikHudAction
    {
        Profile,
        Mail,
        Settings,
        AddCoins,
        AddSavings,
        Inventory,
        Food,
        Tab
    }

    /// <summary>
    /// Presentation of the home HUD. Knows nothing about where data comes from: feed it a
    /// <see cref="FinikHudState"/> and listen to <see cref="ActionRequested"/>.
    /// </summary>
    public sealed class FinikHudView : MonoBehaviour
    {
        [Header("Profile")]
        [SerializeField] TMP_Text playerName;
        [SerializeField] TMP_Text levelNumber;
        [Tooltip("Any meter: a bar, or a ring around the portrait.")]
        [SerializeField] FinikMeter xpBar;
        [SerializeField] TMP_Text xpLabel;
        [SerializeField] Button profileButton;
        [SerializeField] Image avatar;

        [Header("Currencies")]
        [SerializeField] FinikCounterText coins;
        [SerializeField] FinikCounterText savings;
        [SerializeField] Button addCoinsButton;
        [SerializeField] Button addSavingsButton;
        [Tooltip("The pills themselves: tapping a counter does what its «+» does.")]
        [SerializeField] Button coinsButton;
        [SerializeField] Button savingsButton;

        [Header("Needs")]
        [SerializeField] FinikMeter food;
        [SerializeField] FinikMeter mood;

        [Header("Side")]
        [SerializeField] Button mailButton;
        [SerializeField] GameObject mailBadge;
        [SerializeField] TMP_Text mailBadgeCount;
        [SerializeField] Button settingsButton;
        [SerializeField] Button inventoryButton;
        [SerializeField] Button foodButton;

        [Header("Dock")]
        [SerializeField] FinikTabButton[] tabs = Array.Empty<FinikTabButton>();

        FinikHudTab selectedTab;
        bool hasState;
        FinikHudState state;

        /// <summary>Raised for every tap. <c>tab</c> is only meaningful for <see cref="FinikHudAction.Tab"/>.</summary>
        public event Action<FinikHudAction, FinikHudTab> ActionRequested;

        public FinikHudTab SelectedTab => selectedTab;

        public void Bind(
            TMP_Text name, TMP_Text level, FinikMeter xp, TMP_Text xpText, Button profile,
            FinikCounterText coinCounter, FinikCounterText savingsCounter, Button addCoins, Button addSavings,
            FinikMeter foodMeter, FinikMeter moodMeter,
            Button mail, GameObject badge, TMP_Text badgeCount, Button settings, Button inventory, FinikTabButton[] dockTabs)
        {
            playerName = name; levelNumber = level; xpBar = xp; xpLabel = xpText; profileButton = profile;
            coins = coinCounter; savings = savingsCounter; addCoinsButton = addCoins; addSavingsButton = addSavings;
            food = foodMeter; mood = moodMeter;
            mailButton = mail; mailBadge = badge; mailBadgeCount = badgeCount; settingsButton = settings; inventoryButton = inventory;
            tabs = dockTabs;
        }

        void Awake()
        {
            Hook(profileButton, FinikHudAction.Profile);
            Hook(mailButton, FinikHudAction.Mail);
            Hook(settingsButton, FinikHudAction.Settings);
            Hook(inventoryButton, FinikHudAction.Inventory);
            Hook(addCoinsButton, FinikHudAction.AddCoins);
            Hook(addSavingsButton, FinikHudAction.AddSavings);
            // The whole pill is a target: a child aims at the number, not at the small «+».
            Hook(coinsButton, FinikHudAction.AddCoins);
            Hook(savingsButton, FinikHudAction.AddSavings);
            Hook(foodButton, FinikHudAction.Food);
            foreach (var tab in tabs)
            {
                if (!tab || !tab.Button) continue;
                var id = tab.Tab;
                tab.Button.onClick.AddListener(() =>
                {
                    SelectTab(id);
                    ActionRequested?.Invoke(FinikHudAction.Tab, id);
                });
            }
            SelectTab(FinikHudTab.Home, animate: false);
        }

        void Hook(Button button, FinikHudAction action)
        {
            if (button) button.onClick.AddListener(() => ActionRequested?.Invoke(action, selectedTab));
        }

        public void SelectTab(FinikHudTab tab, bool animate = true)
        {
            selectedTab = tab;
            foreach (var t in tabs)
                if (t) t.SetSelected(t.Tab == tab, animate);
        }

        public void Apply(FinikHudState next, bool animate = true)
        {
            animate &= hasState;
            if (playerName) playerName.text = (string.IsNullOrWhiteSpace(next.playerName) ? "FINIK" : next.playerName);
            if (levelNumber) levelNumber.text = (next.level.ToString());
            if (xpBar) xpBar.SetValue(next.XpProgress, animate);
            if (xpLabel) xpLabel.text = ($"{next.xp}/{next.xpToNextLevel}");
            if (coins) coins.SetValue(next.coins, animate);
            if (savings) savings.SetValue(next.savings, animate);
            if (food) food.SetValue(next.food, animate);
            if (mood) mood.SetValue(next.mood, animate);
            if (mailBadge) mailBadge.SetActive(next.unreadMail > 0);
            if (mailBadgeCount) mailBadgeCount.text = (next.unreadMail > 9 ? "9+" : next.unreadMail.ToString());
            state = next;
            hasState = true;
        }

        public FinikHudState State => state;

        public void SetAvatar(Sprite sprite)
        {
            if (avatar && sprite) avatar.sprite = sprite;
        }

        public void BindAvatar(Image image) => avatar = image;

        /// <summary>The pet's avatar as currently shown on the profile card.</summary>
        public Sprite AvatarSprite => avatar ? avatar.sprite : null;

        public void BindFood(Button sideButton) => foodButton = sideButton;
    }
}
