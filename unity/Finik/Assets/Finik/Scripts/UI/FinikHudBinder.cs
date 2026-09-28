using Finik.Core;
using Finik.UI.Budget;
using Finik.UI.Food;
using Finik.UI.Onboarding;
using Finik.UI.Quests;
using Finik.UI.Savings;
using Finik.UI.Settings;
using Finik.UI.Shop;
using UnityEngine;

namespace Finik.UI
{
    /// <summary>
    /// Feeds the home HUD from <see cref="FinikGame"/> and routes its buttons. Needs decay continuously,
    /// so the rings are refreshed a few times a second as well as on every data change.
    /// </summary>
    [RequireComponent(typeof(FinikHudView))]
    public sealed class FinikHudBinder : MonoBehaviour
    {
        [SerializeField] FinikSettingsScreen settingsScreen;
        [SerializeField] FinikBudgetScreen budgetScreen;
        [SerializeField] FinikFoodScreen foodScreen;
        [SerializeField] FinikQuestScreen questScreen;
        [SerializeField] FinikSavingsScreen savingsScreen;
        [SerializeField] FinikShopScreen shopScreen;
        [SerializeField] FinikProgressScreen progressScreen;
        [SerializeField, Min(0.1f)] float refreshSeconds = 0.5f;

        FinikHudView view;
        FinikHomeCards cards;
        FinikShopScreen hookedShop;
        float nextRefresh;
        bool applied;

        void Awake()
        {
            view = GetComponent<FinikHudView>();
            cards = GetComponent<FinikHomeCards>();
        }

        /// <summary>
        /// The screen to open, resolved at the moment it is needed.
        ///
        /// A wired field that points at a switched-off instance — or a blind scene-wide search that
        /// happens to return one — opens a screen nobody can see, and the button looks broken. So:
        /// keep what was wired if it is on, otherwise take the one that is, and fall back to any at all.
        /// </summary>
        T Pick<T>(ref T field) where T : Component
        {
            if (field && field.gameObject.activeInHierarchy) return field;
            T asleep = null;
            foreach (var candidate in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.gameObject.activeInHierarchy)
                {
                    field = candidate;
                    return field;
                }
                asleep ??= candidate;
            }
            if (!field) field = asleep;
            return field;
        }

        void OnEnable()
        {
            if (FinikProfileStore.TryLoad(out _)) FinikGame.EnsureJourney();
            view.ActionRequested += OnAction;
            if (cards)
            {
                cards.TaskStarted += OpenQuests;
                cards.GoalClicked += OnGoalClicked;
            }
            // Remembered separately from the field: the field follows whichever version is on, and
            // unsubscribing from a different instance than the one subscribed to leaks the handler.
            hookedShop = Pick(ref shopScreen);
            if (hookedShop)
            {
                hookedShop.EarnRequested += OpenQuests;
                hookedShop.FoodRequested += OpenFood;
            }
            FinikGame.Changed += Refresh;
            FinikProfileStore.Changed += OnProfileChanged;
            Refresh();
        }

        void OnDisable()
        {
            view.ActionRequested -= OnAction;
            if (cards)
            {
                cards.TaskStarted -= OpenQuests;
                cards.GoalClicked -= OnGoalClicked;
            }
            if (hookedShop)
            {
                hookedShop.EarnRequested -= OpenQuests;
                hookedShop.FoodRequested -= OpenFood;
            }
            hookedShop = null;
            FinikGame.Changed -= Refresh;
            FinikProfileStore.Changed -= OnProfileChanged;
        }

        void OnProfileChanged(FinikProfile profile)
        {
            var onboarding = FindFirstObjectByType<FinikOnboardingFlow>(FindObjectsInactive.Include);
            if (onboarding) view.SetAvatar(onboarding.AvatarFor(profile.petId));
            Refresh();
        }

        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            Refresh();
        }

        void Refresh()
        {
            nextRefresh = Time.unscaledTime + refreshSeconds;
            var growth = FinikGame.Growth;
            var needs = FinikGame.NeedsNow;
            view.Apply(new FinikHudState
            {
                playerName = FinikProfileStore.TryLoad(out var profile) ? profile.petName : "FINIK",
                level = growth.level,
                xp = growth.XpInLevel,
                xpToNextLevel = growth.LevelSpan,
                coins = FinikGame.Balance,
                savings = FinikGame.Savings,
                food = needs.food / 100f,
                mood = needs.mood / 100f,
                unreadMail = 0
            }, animate: applied);
            if (cards)
            {
                if (!FinikGame.HasSelectedGoal)
                {
                    cards.SetGoal(new FinikGoalCardData
                    {
                        title = "Выбери цель",
                        icon = null,
                        saved = 0,
                        target = 0
                    }, animate: applied);
                }
                else
                {
                    var goal = FinikGame.GoalProgress;
                    cards.SetGoal(new FinikGoalCardData
                    {
                        title = goal.goal.Title,
                        iconName = goal.goal.Icon,
                        icon = Pick(ref savingsScreen) ? savingsScreen.IconFor(goal.goal) : null,
                        saved = System.Math.Min(goal.saved, goal.Target),
                        target = goal.Target
                    }, animate: applied);
                }

                FinikQuestTask nextTask = null;
                bool bonusClaimed = false;
                foreach (var task in FinikGame.QuestTasks())
                {
                    var status = FinikGame.QuestProgress(task.Id)?.status ?? FinikQuestStatus.Available;
                    if (task.IsWeekly) { bonusClaimed = status == FinikQuestStatus.Claimed; continue; }
                    if (status == FinikQuestStatus.Claimed) continue;
                    nextTask = task;
                    break;
                }

                cards.SetTask(nextTask != null
                    ? new FinikDailyTaskData { title = FinikQuestText.Resolve(nextTask.Title), description = FinikQuestText.Resolve(nextTask.Description), reward = nextTask.Reward }
                    : bonusClaimed
                        ? new FinikDailyTaskData { title = "На сегодня всё!", description = "Приходи завтра за новыми заданиями!", finished = true }
                        : new FinikDailyTaskData { title = "Задания дня пройдены", description = "Приходи завтра за новыми заданиями." });
            }
            applied = true;
        }

        void OnAction(FinikHudAction action, FinikHudTab tab)
        {
            switch (action)
            {
                case FinikHudAction.Food:
                    OpenFood();
                    break;
                case FinikHudAction.AddCoins:
                    // «+» at the coins means "earn": Finik walks to the study desk and the quest board opens there.
                    OpenQuests();
                    break;
                case FinikHudAction.AddSavings:
                    OpenSavings();
                    break;
                case FinikHudAction.Profile:
                    // Tapping the avatar is the child's own progress screen.
                    OpenProgress();
                    break;
                case FinikHudAction.Settings:
                    // The adult section is reached from settings, behind its own hold-and-answer gate.
                    OpenSettings();
                    break;
                case FinikHudAction.Tab when tab == FinikHudTab.Savings:
                    // Like the quests: the piggy bank is a screen over the room, so the dock goes dark.
                    view.SelectTab(FinikHudTab.Home);
                    OpenSavings();
                    break;
                case FinikHudAction.Tab when tab == FinikHudTab.Shopping:
                    // The shop is a room screen too: the tab launches it and then stops being lit.
                    view.SelectTab(FinikHudTab.Home);
                    OpenShop();
                    break;
                case FinikHudAction.Tab when tab == FinikHudTab.Quests:
                    // The quest board is a room screen, not a page of the dock: the tab only launches
                    // it, so nothing stays lit for when the HUD comes back.
                    view.SelectTab(FinikHudTab.Home);
                    OpenQuests();
                    break;
                case FinikHudAction.Tab when tab == FinikHudTab.Budget:
                    // Planning happens at the desk, like the quests: a room screen over the HUD.
                    view.SelectTab(FinikHudTab.Home);
                    OpenBudget();
                    break;
                default:
                    // Not ported from the web app yet (mail, budget…).
                    Debug.Log($"[FinikHud] {action}{(action == FinikHudAction.Tab ? " " + tab : string.Empty)}");
                    break;
            }
        }

        void OnGoalClicked() => OpenSavings();

        void OpenProgress()
        {
            if (Pick(ref progressScreen)) progressScreen.Open();
            else Debug.LogWarning("[FinikHud] Progress screen is missing: run Finik/UI/Rebuild Progress Screen.");
        }

        void OpenSettings()
        {
            if (Pick(ref settingsScreen)) settingsScreen.Open();
            else Debug.LogWarning("[FinikHud] Settings screen is missing: run Finik/UI/Rebuild Settings Screen.");
        }

        void OpenBudget()
        {
            if (Pick(ref budgetScreen)) budgetScreen.Open();
            else Debug.LogWarning("[FinikHud] Budget screen is missing: run Finik/UI/Rebuild Budget Screen.");
        }

        void OpenSavings()
        {
            if (Pick(ref savingsScreen)) savingsScreen.Open();
            else Debug.LogWarning("[FinikHud] Savings screen is missing: run Finik/UI/Rebuild Savings Screen.");
        }

        /// <summary>
        /// Same path as tapping the fridge: Finik walks over and the menu opens there, so the button
        /// and the fridge read as one thing. Straight to the menu if he cannot get there.
        /// </summary>
        void OpenFood()
        {
            if (FinikRoomBubble.WalkTo(FinikFoodScreen.FridgeInteraction)) return;
            if (Pick(ref foodScreen)) foodScreen.Open();
            else Debug.LogWarning("[FinikHud] Food screen is missing: run Finik/UI/Rebuild Food Screen.");
        }

        void OpenShop()
        {
            // Like the fridge for food: Finik walks to the shelf and the shop opens there when it exists.
            if (FinikRoomBubble.WalkTo(FinikShopScreen.ShelfInteraction)) return;
            if (Pick(ref shopScreen)) shopScreen.Open();
            else Debug.LogWarning("[FinikHud] Shop screen is missing: run Finik/UI/Rebuild Shop Screen.");
        }

        void OpenQuests()
        {
            if (!FinikGame.HasSelectedGoal)
            {
                OpenSavings();
                return;
            }
            if (FinikRoomBubble.WalkTo(FinikQuestScreen.DeskInteraction)) return;
            if (Pick(ref questScreen)) questScreen.Open();
            else Debug.LogWarning("[FinikHud] Quest screen is missing: run Finik/UI/Rebuild Quest Screen.");
        }
    }
}
