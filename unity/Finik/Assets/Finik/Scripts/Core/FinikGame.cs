using System;
using System.Collections.Generic;
using UnityEngine;

namespace Finik.Core
{
    /// <summary>Everything the game persists besides the profile. Stored as JSON in PlayerPrefs.</summary>
    [Serializable]
    public sealed class FinikGameState
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public int weekNumber = 1;
        public FinikWallet wallet = new();
        public FinikPetCare care = new();
        public FinikGrowth growth = new();
        public FinikQuestState quests = new();
        public FinikShopState shop = new();
        /// <summary>Today's plan for «Нужно» / «Хочу» / «Коплю» and what actually happened.</summary>
        public FinikBudgetState budget = new();
        /// <summary>Which level of each free mood game has been cleared.</summary>
        public FinikMiniGameState miniGames = new();
        public string lastDecisionId;
        public string previousDecisionId;
        /// <summary>What the piggy bank is for (FinikGoalCatalog id); empty in old saves = the default goal.</summary>
        public string goalId;
        /// <summary>Goals already bought with saved coins. Each room reward is one-time and persistent.</summary>
        public List<string> purchasedGoalIds = new();
        /// <summary>Persisted visual stage, raised by budget growth points (<see cref="FinikDayReviewEngine"/>). Never goes down.</summary>
        public int petStage = 1;
        public int demoDay;

        public string PeriodId => $"week-{weekNumber}";
    }

    public enum FinikCareFailure
    {
        None,
        Cooldown,
        InsufficientFunds,
        NoChange,
        UnknownPlan,
        NoJourney
    }

    public readonly struct FinikCareResult
    {
        public readonly FinikCareFailure failure;
        public readonly string message;
        public readonly int retryAfterSeconds;

        public FinikCareResult(FinikCareFailure failure, string message, int retryAfterSeconds = 0)
        {
            this.failure = failure;
            this.message = message;
            this.retryAfterSeconds = retryAfterSeconds;
        }

        public bool Ok => failure == FinikCareFailure.None;
    }

    public enum FinikQuestFailure
    {
        None,
        NoJourney,
        UnknownTask,
        UnknownChoice,
        AlreadyDone,
        NotReady
    }

    /// <summary>What resolving or claiming a quest did: coins and XP paid, and whether the weekly bonus unlocked.</summary>
    public readonly struct FinikQuestOutcome
    {
        public readonly FinikQuestFailure failure;
        public readonly FinikQuestTask task;
        public readonly FinikQuestChoice choice;
        public readonly int coins;
        public readonly int xp;
        public readonly bool weeklyUnlocked;

        public FinikQuestOutcome(FinikQuestFailure failure, FinikQuestTask task = null, FinikQuestChoice choice = null, int coins = 0, int xp = 0, bool weeklyUnlocked = false)
        {
            this.failure = failure;
            this.task = task;
            this.choice = choice;
            this.coins = coins;
            this.xp = xp;
            this.weeklyUnlocked = weeklyUnlocked;
        }

        public bool Ok => failure == FinikQuestFailure.None;
    }

    public enum FinikMiniGameFailure
    {
        None,
        NoJourney,
        UnknownGame,
        UnknownLevel
    }

    /// <summary>What clearing a level of a mood game gave: mood actually gained, XP, and whether the next level opened.</summary>
    public readonly struct FinikMiniGameReward
    {
        public readonly FinikMiniGameFailure failure;
        public readonly FinikMiniGameLevel level;
        public readonly int mood;
        public readonly int xp;
        /// <summary>True when this level had never been cleared before.</summary>
        public readonly bool firstClear;
        public readonly bool unlockedNext;

        public FinikMiniGameReward(FinikMiniGameFailure failure, FinikMiniGameLevel level = null, int mood = 0, int xp = 0,
            bool firstClear = false, bool unlockedNext = false)
        {
            this.failure = failure;
            this.level = level;
            this.mood = mood;
            this.xp = xp;
            this.firstClear = firstClear;
            this.unlockedNext = unlockedNext;
        }

        public bool Ok => failure == FinikMiniGameFailure.None;
    }

    public readonly struct FinikShortage
    {
        public readonly int price;
        public readonly int available;
        public readonly int shortfall;

        public FinikShortage(int price, int available)
        {
            this.price = Math.Max(0, price);
            this.available = Math.Max(0, available);
            shortfall = Math.Max(0, this.price - this.available);
        }

        public string Title => shortfall > 0 ? $"Не хватает {shortfall} монет" : "Монет хватает";
        public string Message => shortfall > 0
            ? "Ничего страшного: можно выбрать дешевле, отложить покупку или выполнить финансовое задание."
            : "Этот вариант помещается в доступный бюджет.";
    }

    /// <summary>
    /// The single source of truth for game data (wallet, pet needs, growth), replacing the web app's
    /// zustand stores. Mutations go through the methods here, persist immediately and raise
    /// <see cref="Changed"/>. Need decay is advanced by the active-game timer, never while the app is closed.
    /// </summary>
    public static class FinikGame
    {
        const string Key = "finik.game.v1";

        static FinikGameState state;
        static int transactionCounter;

        static List<FinikQuestTask> questTasks;
        static string questTasksDay;

        public static event Action Changed;
        /// <summary>
        /// Pet reaction for the 3D Finik: type and intensity, same ids as the web app ("fed", "task-completed"
        /// with "big" for the weekly bonus, …). Intensity is null for the normal variant.
        /// </summary>
        public static event Action<string, string> ReactionRequested;

        public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static bool HasJourney => Load() != null;

        public static FinikGameState State => Load();

        public static FinikNeeds NeedsNow
        {
            get
            {
                var s = Load();
                return s == null
                    ? new FinikNeeds(FinikPetCareEngine.InitialNeed, FinikPetCareEngine.InitialNeed)
                    : FinikPetCareEngine.NeedsAt(s.care, NowMs);
            }
        }

        public static FinikGrowthProgress Growth => FinikGrowthEngine.Progress(Load()?.growth.xp ?? 0);
        public static int CharacterStage => Math.Clamp(Load()?.petStage ?? 1, 1, 3);

        public static int Balance => Load()?.wallet.balance ?? 0;
        public static int Savings => Load()?.wallet.savingsBalance ?? 0;

        public static bool HasSelectedGoal => !string.IsNullOrEmpty(Load()?.goalId);
        public static FinikGoal Goal => FinikGoalCatalog.Resolve(Load()?.goalId);
        public static FinikGoalProgress GoalProgress => new(Goal, Savings);
        public static bool GoalPurchased(string goalId) =>
            !string.IsNullOrEmpty(goalId) && (Load()?.purchasedGoalIds?.Contains(goalId) ?? false);
        public static bool CurrentGoalPurchased => HasSelectedGoal && GoalPurchased(Goal.Id);
        public static bool CanPurchaseCurrentGoal => HasSelectedGoal && GoalProgress.Reached && !CurrentGoalPurchased;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>New journey after onboarding (web: gameStore.startProfile): fresh state plus the first week's income.</summary>
        public static void StartJourney()
        {
            long now = NowMs;
            var local = DateTime.Now;
            state = new FinikGameState { care = FinikPetCareEngine.Initial(now) };
            state.quests.campaignStartDayKey = FinikQuestEngine.DayKey(local);
            EnsureDailyLoginIncome(state, local, now);
            Commit();
        }

        /// <summary>Fresh demo profile for the five-day quest campaign. It uses the same 30-coin economy as a real player.</summary>
        public static void StartDemo()
        {
            long now = NowMs;
            var local = DateTime.Now;
            state = new FinikGameState { care = FinikPetCareEngine.Initial(now), demoDay = 1 };
            state.quests.campaignStartDayKey = FinikQuestEngine.DayKey(local);
            EnsureDailyLoginIncome(state, local, now);
            Commit();
        }

        /// <summary>Current virtual day of the demo campaign.</summary>
        public static int DemoCampaignDay =>
            Load() == null ? 1 : Math.Max(1, state.demoDay > 0 ? state.demoDay : FinikQuestEngine.CampaignDay(state.quests, DateTime.Now));

        /// <summary>Switches the virtual demo date without discarding growth, wallet or quest rewards.</summary>
        public static void StartDemoCampaignDay(int campaignDay)
        {
            if (Load() == null) StartDemo();
            state.demoDay = Math.Clamp(campaignDay, 1, 100000);
            if (string.IsNullOrEmpty(state.quests.campaignStartDayKey))
                state.quests.campaignStartDayKey = FinikQuestEngine.DayKey(DateTime.Now);
            if (state.demoDay > 1 && string.IsNullOrEmpty(state.goalId))
                state.goalId = FinikGoalCatalog.DefaultGoalId;
            EnsureDailyLoginIncome(state, DateTime.Now.AddDays(state.demoDay - 1), NowMs);
            questTasks = null;
            questTasksDay = null;
            Commit();
        }

        /// <summary>Plays the successful route with the same wallet and savings effects as manual answers.</summary>
        public static bool CompleteDemoDay(int maxQuests = int.MaxValue)
        {
            if (Load() == null || state.demoDay <= 0 || maxQuests <= 0) return false;
            if (string.IsNullOrEmpty(state.goalId)) state.goalId = FinikGoalCatalog.DefaultGoalId;
            var tasks = QuestTasks();
            bool changed = false;
            int completed = 0;
            foreach (var task in tasks)
            {
                if (task.IsWeekly) continue;
                if (completed >= maxQuests) break;
                var progress = state.quests.Find(task.Id);
                if (progress == null || progress.status == FinikQuestStatus.Claimed) continue;
                if (progress.status == FinikQuestStatus.Completed)
                {
                    if (!ClaimQuest(task.Id).Ok) return changed;
                    changed = true;
                    completed++;
                    continue;
                }
                if (!StartQuest(task.Id)) return changed;
                for (int i = progress.questionIndex; i < task.Quest.Questions.Length; i++)
                {
                    var choice = Array.Find(task.Quest.Questions[i].Choices, c => c.Tag == FinikQuestTag.Good);
                    if (choice == null || !ApplyQuestChoiceEffect(task.Id, i, choice)) return changed;
                    if (i + 1 < task.Quest.Questions.Length)
                    {
                        if (!AdvanceQuestQuestion(task.Id, i + 1, choice.Id)) return changed;
                    }
                    else if (!ResolveQuest(task.Id, choice.Id).Ok) return changed;
                }
                changed = true;
                completed++;
            }
            foreach (var task in tasks)
            {
                if (!task.IsWeekly) continue;
                var progress = state.quests.Find(task.Id);
                if (progress == null || progress.status != FinikQuestStatus.Completed) continue;
                if (ClaimQuest(task.Id).Ok) changed = true;
            }
            return changed;
        }

        public static bool DemoDayComplete
        {
            get
            {
                if (Load() == null || state.demoDay <= 0) return false;
                foreach (var task in QuestTasks())
                    if (!task.IsWeekly && state.quests.Find(task.Id)?.status != FinikQuestStatus.Claimed) return false;
                return true;
            }
        }

        /// <summary>Direct demo-only care adjustment, clamped to the normal 0..100 range.</summary>
        public static void AdjustDemoNeeds(float foodDelta, float moodDelta)
        {
            if (Load() == null) return;
            long now = NowMs;
            FinikPetCareEngine.Settle(state.care, now);
            state.care.needs = state.care.needs.Plus(new FinikNeeds(foodDelta, moodDelta));
            state.care.updatedAtMs = now;
            Commit();
        }

        /// <summary>Applies the real active-play duration accumulated by the runtime timer.</summary>
        public static void AdvanceActiveNeedDecay(float seconds)
        {
            if (Load() == null || seconds <= 0f) return;
            long now = NowMs;
            state.care.needs = FinikNeedDecay.Apply(state.care.needs, seconds / 3600d);
            state.care.decayedAtMs = now;
            state.care.updatedAtMs = now;
            Commit();
        }

        /// <summary>Adds or removes demo coins while preserving the wallet transaction log.</summary>
        public static void AdjustDemoCoins(int delta)
        {
            if (Load() == null || delta == 0) return;
            var kind = delta > 0 ? FinikTransactionKind.Earn : FinikTransactionKind.Spend;
            int amount = delta > 0 ? delta : Math.Min(state.wallet.balance, -delta);
            if (amount <= 0) return;
            var transaction = FinikWalletEngine.Make(NextTransactionId(), kind, amount,
                FinikTransactionSource.Demo, "Демо: ручная корректировка баланса", state.PeriodId, NowMs);
            if (FinikWalletEngine.Apply(state.wallet, transaction) == FinikWalletError.None) Commit();
        }

#if UNITY_EDITOR
        public static void StartCampaignDayForTesting(int campaignDay) => StartDemoCampaignDay(campaignDay);
#endif

        /// <summary>For players whose profile predates the game state: start a journey silently.</summary>
        public static void EnsureJourney()
        {
            if (Load() == null)
            {
                StartJourney();
                return;
            }

            if (string.IsNullOrEmpty(state.quests.campaignStartDayKey))
                state.quests.campaignStartDayKey = FinikQuestEngine.DayKey(DateTime.Now);
            if (EnsureDailyLoginIncome(state, DateTime.Now, NowMs)) Commit();
        }

        public static void Clear()
        {
            state = null;
            questTasks = null;
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Drops the in-memory copy so the next access re-reads PlayerPrefs (editor menus, domain reload off).</summary>
        public static void Reload()
        {
            state = null;
            questTasks = null;
            Load();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ wallet

        public static bool Credit(int amount, string source, string reason)
        {
            if (Load() == null) return false;
            var error = FinikWalletEngine.Apply(state.wallet, FinikWalletEngine.Make(NextTransactionId(), FinikTransactionKind.Earn, amount, source, reason, state.PeriodId, NowMs));
            if (error != FinikWalletError.None) return false;
            Commit();
            return true;
        }

        public static FinikShortage ShortageFor(int price) => new(price, Balance);

        // ------------------------------------------------------------------ daily budget

        /// <summary>Game day the current plan/facts belong to. Demo mode follows its virtual campaign day.</summary>
        public static string BudgetDayKey => BudgetDayKeyFor(Load());

        static string BudgetDayKeyFor(FinikGameState s)
        {
            if (s?.demoDay > 0 && DateTime.TryParse(s.quests?.campaignStartDayKey, out var start))
                return FinikQuestEngine.DayKey(start.Date.AddDays(s.demoDay - 1));
            return FinikQuestEngine.DayKey(DateTime.Now);
        }

        /// <summary>Today's budget state, moved to the current day first.</summary>
        static FinikBudgetState Budget()
        {
            if (Load() == null) return null;
            state.budget ??= new FinikBudgetState();
            if (FinikBudgetEngine.Sync(state.budget, BudgetDayKey))
            {
                ApplyGrowthStage(state);
                Commit();
            }
            return state.budget;
        }

        public static bool HasBudgetPlan => FinikBudgetEngine.HasPlan(Budget(), BudgetDayKey);

        /// <summary>The split the budget screen offers before the child changes anything.</summary>
        public static FinikBudgetPlan SuggestedBudget() => FinikBudgetEngine.Suggest(BudgetDayKey, Balance);

        public static FinikBudgetPlan CurrentBudgetPlan() => HasBudgetPlan ? state.budget.plan : null;

        /// <summary>Coins of today's plan this envelope has not spent yet.</summary>
        public static int BudgetLeft(FinikBudgetBucket bucket) => FinikBudgetEngine.Left(Budget(), BudgetDayKey, bucket);

        /// <summary>Plan and fact for today, for the budget screen and the adult section.</summary>
        public static FinikBudgetSummary BudgetSummary() => FinikBudgetEngine.Summarize(Budget(), BudgetDayKey);

        /// <summary>Most recent plan/fact the child can review: today if planned, otherwise the previous finished day.</summary>
        public static FinikBudgetSummary LatestBudgetSummary() => FinikBudgetEngine.LatestSummary(Budget(), BudgetDayKey);

        public static string LatestBudgetDayKey => FinikBudgetEngine.LatestDayKey(Budget(), BudgetDayKey);

        /// <summary>
        /// Locks today's plan. Confirming moves no coins: it only records what the child decided, so
        /// the wallet stays the single place money lives.
        /// </summary>
        public static FinikBudgetFailure ConfirmBudget(int needs, int wants, int savings)
        {
            var budget = Budget();
            if (budget == null) return FinikBudgetFailure.NoJourney;
            string day = BudgetDayKey;
            if (FinikBudgetEngine.HasPlan(budget, day)) return FinikBudgetFailure.AlreadyConfirmed;

            int income = Balance;
            if (income <= 0) return FinikBudgetFailure.NoCoins;
            if (needs < 0 || wants < 0 || savings < 0) return FinikBudgetFailure.NotAllCoinsPlanned;
            if (needs + wants + savings != income) return FinikBudgetFailure.NotAllCoinsPlanned;

            budget.plan = new FinikBudgetPlan
            {
                dayKey = day,
                income = income,
                needs = needs,
                wants = wants,
                savings = savings,
                confirmedAtMs = NowMs
            };
            budget.actuals ??= new FinikBudgetActuals { dayKey = day };
            Commit();
            return FinikBudgetFailure.None;
        }

        /// <summary>True once today's plan has been scored by «Завершить день».</summary>
        public static bool IsBudgetDayClosed => FinikDayReviewEngine.Find(Budget(), BudgetDayKey) != null;

        /// <summary>Today's review, or null while the day is open.</summary>
        public static FinikDayReview TodayReview() => FinikDayReviewEngine.Find(Budget(), BudgetDayKey);

        public static int GrowthPoints => Budget()?.growthPoints ?? 0;

        /// <summary>Why the pet is at its current stage, for «Мой прогресс».</summary>
        public static string GrowthReason(string petName) => FinikDayReviewEngine.Reason(Budget(), CharacterStage, petName);

        /// <summary>
        /// «Завершить день»: scores today's plan against today's facts and turns the result into growth
        /// points. Null without a confirmed plan. Closing twice returns the same review.
        /// </summary>
        public static FinikDayReview CloseBudgetDay()
        {
            var budget = Budget();
            if (budget == null) return null;
            bool wasClosed = FinikDayReviewEngine.Find(budget, BudgetDayKey) != null;
            var review = FinikDayReviewEngine.Close(budget, BudgetDayKey, manual: true);
            if (review == null || wasClosed) return review;
            ApplyGrowthStage(state);
            Commit();
            return review;
        }

        static void ApplyGrowthStage(FinikGameState s)
        {
            if (s?.budget == null) return;
            s.petStage = Math.Clamp(Math.Max(s.petStage, FinikDayReviewEngine.StageFor(s.budget.growthPoints)), 1, 3);
        }

        /// <summary>Records a real operation against its envelope. Call it after the wallet has been charged.</summary>
        static void RecordBudgetActual(FinikBudgetBucket bucket, int amount)
        {
            if (amount <= 0 || Load() == null) return;
            state.budget ??= new FinikBudgetState();
            FinikBudgetEngine.Sync(state.budget, BudgetDayKey);
            state.budget.actuals ??= new FinikBudgetActuals { dayKey = BudgetDayKey };
            FinikBudgetEngine.Add(state.budget.actuals, bucket, amount);
        }


        public static bool CanAfford(FinikFoodPlan plan) => plan != null && plan.TotalCost <= Balance;

        // ------------------------------------------------------------------ piggy bank

        /// <summary>Switches the piggy bank to another goal; the coins already saved stay in it.</summary>
        public static bool SelectGoal(string goalId)
        {
            if (Load() == null || !FinikGoalCatalog.TryGet(goalId, out var goal)) return false;
            if (GoalPurchased(goal.Id)) return false;
            if (state.goalId == goal.Id) return true;
            state.goalId = goal.Id;
            Commit();
            return true;
        }

        /// <summary>Moves coins from the wallet into the piggy bank, never past the goal's price.</summary>
        public static FinikSavingsFailure SaveToGoal(int amount)
        {
            if (Load() == null) return FinikSavingsFailure.NoJourney;
            if (!HasSelectedGoal) return FinikSavingsFailure.InvalidAmount;
            if (amount <= 0) return FinikSavingsFailure.InvalidAmount;
            var progress = GoalProgress;
            if (CurrentGoalPurchased || progress.Reached) return FinikSavingsFailure.GoalReached;
            if (amount > progress.Left) return FinikSavingsFailure.InvalidAmount;
            var transaction = FinikWalletEngine.Make(NextTransactionId(), FinikTransactionKind.Save, amount,
                FinikTransactionSource.Goal, $"В копилку: {progress.goal.Title}", state.PeriodId, NowMs);
            var error = FinikWalletEngine.Apply(state.wallet, transaction);
            if (error != FinikWalletError.None) return Failure(error);
            RecordBudgetActual(FinikBudgetBucket.Savings, amount);
            Commit();
            // Reaching the amount opens the purchase celebration. The big pet celebration is reserved
            // for the child's explicit "Buy" action, so saving itself stays a smaller positive reaction.
            ReactionRequested?.Invoke("saved", null);
            return FinikSavingsFailure.None;
        }

        /// <summary>
        /// Buys the selected goal with the coins already saved for it. The operation keeps the wallet log
        /// derivable: savings are withdrawn and immediately spent, so spendable balance stays unchanged
        /// while the piggy bank is emptied. The room reward is then permanent.
        /// </summary>
        public static bool PurchaseCurrentGoal()
        {
            if (Load() == null || !CanPurchaseCurrentGoal) return false;
            var progress = GoalProgress;
            string goalId = progress.goal.Id;
            int price = progress.Target;
            long now = NowMs;

            var withdraw = FinikWalletEngine.Make(NextTransactionId(), FinikTransactionKind.Withdraw, price,
                FinikTransactionSource.Goal, $"Goal purchase funding: {goalId}", state.PeriodId, now);
            if (FinikWalletEngine.Apply(state.wallet, withdraw) != FinikWalletError.None) return false;

            var spend = FinikWalletEngine.Make(NextTransactionId(), FinikTransactionKind.Spend, price,
                FinikTransactionSource.Goal, $"Goal purchased: {goalId}", state.PeriodId, now);
            if (FinikWalletEngine.Apply(state.wallet, spend) != FinikWalletError.None)
            {
                state.wallet.transactions.Remove(withdraw);
                state.wallet = FinikWalletEngine.Normalize(state.wallet);
                return false;
            }

            state.purchasedGoalIds ??= new List<string>();
            if (!state.purchasedGoalIds.Contains(goalId)) state.purchasedGoalIds.Add(goalId);
            state.goalId = null;

            Commit();
            ReactionRequested?.Invoke("goal-purchased", "big");
            return true;
        }

        /// <summary>Takes coins back from the piggy bank into the wallet.</summary>
        public static FinikSavingsFailure WithdrawSavings(int amount)
        {
            if (Load() == null) return FinikSavingsFailure.NoJourney;
            if (amount <= 0) return FinikSavingsFailure.InvalidAmount;
            var transaction = FinikWalletEngine.Make(NextTransactionId(), FinikTransactionKind.Withdraw, amount,
                FinikTransactionSource.Goal, $"Из копилки: {Goal.Title}", state.PeriodId, NowMs);
            var error = FinikWalletEngine.Apply(state.wallet, transaction);
            if (error != FinikWalletError.None) return Failure(error);
            Commit();
            return FinikSavingsFailure.None;
        }

        static FinikSavingsFailure Failure(FinikWalletError error) => error switch
        {
            FinikWalletError.InsufficientFunds => FinikSavingsFailure.InsufficientFunds,
            FinikWalletError.InsufficientSavings => FinikSavingsFailure.InsufficientSavings,
            _ => FinikSavingsFailure.InvalidAmount
        };

        // ------------------------------------------------------------------ shopping

        public static bool Owns(string itemId) => Load()?.shop?.Owns(itemId) ?? false;

        /// <summary>
        /// What buying this item would cost and whether the wallet covers it. Nothing is charged; the
        /// shop screen builds both the confirmation and the "not enough" explanation from this.
        /// </summary>
        public static FinikPurchasePreview PreviewPurchase(string itemId)
        {
            if (!FinikShopCatalog.TryGet(itemId, out var item)) return default;
            var preview = new FinikPurchasePreview(item, Balance);
            return item.IsAccessory && Owns(item.Id) ? preview.AsOwned() : preview;
        }

        /// <summary>
        /// Pays for an item and gives Finik what it promised (port of domain/purchaseService.ts).
        /// Callers confirm first: this charges the wallet. The needs gained are measured after
        /// clamping, so the result card never promises more than the pet actually got. XP is granted
        /// once per item: buying water ten times teaches nothing new.
        /// </summary>
        public static FinikPurchaseResult Buy(string itemId)
        {
            if (Load() == null) return new FinikPurchaseResult(FinikPurchaseFailure.NoJourney);
            if (!FinikShopCatalog.TryGet(itemId, out var item)) return new FinikPurchaseResult(FinikPurchaseFailure.UnknownItem);
            // A signpost card opens another screen; there is nothing to charge for it.
            if (item.IsLink) return new FinikPurchaseResult(FinikPurchaseFailure.UnknownItem, item);
            // Accessories are worn, not consumed: the shop offers to put an owned one back on instead.
            if (item.IsAccessory && Owns(item.Id))
                return new FinikPurchaseResult(FinikPurchaseFailure.AlreadyOwned, item, balanceAfter: Balance);

            long now = NowMs;
            var transaction = FinikWalletEngine.Make(NextTransactionId(), FinikTransactionKind.Spend, item.Price,
                FinikTransactionSource.Purchase, $"[{Bucket(item)}] Покупка: {item.Title}", state.PeriodId, now);
            var error = FinikWalletEngine.Apply(state.wallet, transaction);
            if (error != FinikWalletError.None)
                return new FinikPurchaseResult(FinikPurchaseFailure.InsufficientFunds, item,
                    balanceAfter: Balance, deficit: Math.Max(0, item.Price - Balance));

            var before = FinikPetCareEngine.NeedsAt(state.care, now);
            // The bigger promise decides which care action this counts as: food goes on the fridge's
            // clock, everything else on play's, so the room's own actions stay in step with the shop.
            var action = item.Deltas.food >= item.Deltas.mood ? FinikCareAction.Feed : FinikCareAction.Play;
            FinikPetCareEngine.Apply(state.care, action, item.Deltas, now);
            var gained = new FinikNeeds(state.care.needs.food - before.food, state.care.needs.mood - before.mood);

            int xp = 0;
            if (item.Xp > 0 && FinikGrowthEngine.Grant(state.growth, $"growth-shop-{item.Id}", FinikGrowthSource.Care,
                    item.Xp, $"Первая покупка: {item.Title}", now))
                xp = item.Xp;

            state.shop.owned ??= new List<string>();
            if (!state.shop.owned.Contains(item.Id)) state.shop.owned.Add(item.Id);
            RecordBudgetActual(FinikBudgetEngine.BucketFor(item.Category), item.Price);

            Commit();
            ReactionRequested?.Invoke(item.Deltas.food >= item.Deltas.mood ? "fed" : "played", null);
            return new FinikPurchaseResult(FinikPurchaseFailure.None, item, item.Price, xp, gained, Balance);
        }

        /// <summary>Budget envelope the purchase is charged to, kept in the reason for the history screen.</summary>
        static string Bucket(FinikShopItem item) => item.Category == FinikShopCategory.Need ? "needs" : "wants";

        // ------------------------------------------------------------------ pet care

        public static int SecondsUntil(FinikCareAction action) =>
            Load() == null ? 0 : FinikPetCareEngine.SecondsUntil(state.care, action, NowMs);

        public static int TryRaiseMoodFromTap()
        {
            if (Load() == null) return 0;
            long now = NowMs;
            if (state.care.lastTapMoodAtMs > 0 && now - state.care.lastTapMoodAtMs < 180_000) return 0;
            FinikPetCareEngine.Settle(state.care, now);
            if (state.care.needs.mood >= 100f) return 0;
            float before = state.care.needs.mood;
            state.care.needs = state.care.needs.Plus(new FinikNeeds(0f, 5f));
            state.care.lastTapMoodAtMs = now;
            state.care.updatedAtMs = now;
            Commit();
            return Mathf.CeilToInt(state.care.needs.mood - before);
        }

        // ------------------------------------------------------------------ mood games

        /// <summary>Highest level of <paramref name="gameId"/> the player has cleared (0 = none).</summary>
        public static int MiniGameBestLevel(string gameId)
        {
            var s = Load();
            if (s == null) return 0;
            s.miniGames ??= new FinikMiniGameState();
            return s.miniGames.BestLevel(gameId);
        }

        /// <summary>True when that level may be started: the first one always, the rest once the previous is cleared.</summary>
        public static bool MiniGameUnlocked(string gameId, int level) =>
            FinikMiniGameCatalog.Unlocked(level, MiniGameBestLevel(gameId));

        /// <summary>
        /// Pays for clearing a mood game: the level's +3 / +6 / +9 mood, XP the first time ever, and the
        /// level is remembered so the next one opens. The games cost nothing, so nothing is charged and
        /// no cooldown blocks them — but the gain is measured after clamping, so a pet already at 100
        /// is told it gained nothing rather than promised mood it did not get.
        /// </summary>
        public static FinikMiniGameReward CompleteMiniGameLevel(string gameId, int level)
        {
            if (Load() == null) return new FinikMiniGameReward(FinikMiniGameFailure.NoJourney);
            if (!FinikMiniGameCatalog.TryGet(gameId, out var game)) return new FinikMiniGameReward(FinikMiniGameFailure.UnknownGame);
            var definition = FinikMiniGameCatalog.Level(game, level);
            if (definition == null) return new FinikMiniGameReward(FinikMiniGameFailure.UnknownLevel);

            long now = NowMs;
            state.miniGames ??= new FinikMiniGameState();
            var progress = state.miniGames.Ensure(gameId);
            bool firstClear = progress.bestLevel < level;

            FinikPetCareEngine.Settle(state.care, now);
            float before = state.care.needs.mood;
            FinikPetCareEngine.Apply(state.care, FinikCareAction.Play, new FinikNeeds(0f, definition.Mood), now);
            int mood = (int)Math.Round(state.care.needs.mood - before);

            int xp = 0;
            if (definition.Xp > 0 && FinikGrowthEngine.Grant(state.growth, $"growth-game-{gameId}-{level}", FinikGrowthSource.Care,
                    definition.Xp, $"Игра «{game.Title}»: {definition.Title}", now))
                xp = definition.Xp;

            progress.bestLevel = Math.Max(progress.bestLevel, level);
            progress.plays++;
            progress.lastPlayedAtMs = now;

            Commit();
            ReactionRequested?.Invoke("played", definition.IsBig ? "big" : null);
            return new FinikMiniGameReward(FinikMiniGameFailure.None, definition, mood, xp, firstClear,
                FinikMiniGameCatalog.Unlocked(level + 1, progress.bestLevel) && game.HasLevel(level + 1));
        }

        /// <summary>
        /// Port of petCareStore.performFoodPlan. The web app charges the week's budget envelopes once a
        /// plan is confirmed; budget planning is not in Unity yet, so food is paid from the wallet (the
        /// web app's path before the plan is confirmed).
        /// </summary>
        public static FinikCareResult PerformFoodPlan(string planId)
        {
            if (Load() == null) return new FinikCareResult(FinikCareFailure.NoJourney, "Сначала пройди знакомство с питомцем.");
            if (!FinikFoodCatalog.TryGetPlan(planId, out var plan))
                return new FinikCareResult(FinikCareFailure.UnknownPlan, $"Неизвестный вариант еды: {planId}");

            long now = NowMs;
            int retry = FinikPetCareEngine.SecondsUntil(state.care, FinikCareAction.Feed, now);
            if (retry > 0)
                return new FinikCareResult(FinikCareFailure.Cooldown, $"Питомец только что поел. Следующий выбор будет доступен через {retry} сек.", retry);

            if (plan.TotalCost > 0)
            {
                var transaction = FinikWalletEngine.Make(NextTransactionId(), FinikTransactionKind.Spend, plan.TotalCost,
                    FinikTransactionSource.PetCare, $"Еда: {plan.Title}", state.PeriodId, now);
                if (FinikWalletEngine.Apply(state.wallet, transaction) != FinikWalletError.None)
                    return new FinikCareResult(FinikCareFailure.InsufficientFunds, $"Не хватает монет. Этот вариант стоит {plan.TotalCost} монет.");
                // Each charge lands in the envelope the food plan names, so plan and fact stay honest.
                foreach (var charge in plan.Charges) RecordBudgetActual(charge.bucket, charge.amount);
            }

            if (plan.Xp > 0)
                FinikGrowthEngine.Grant(state.growth, $"growth-food-{UtcDate(now)}", FinikGrowthSource.Care, plan.Xp, $"Разумный выбор еды: {plan.Title}", now);

            FinikPetCareEngine.Apply(state.care, FinikCareAction.Feed, plan.Deltas, now);
            state.previousDecisionId = state.lastDecisionId;
            state.lastDecisionId = plan.Id;
            Commit();
            ReactionRequested?.Invoke("fed", null);
            return new FinikCareResult(FinikCareFailure.None, plan.Result);
        }

        // ------------------------------------------------------------------ quests

        /// <summary>
        /// Today's quest board: the daily quests, then the weekly streak. Moves the progress to the current
        /// day and week first (a new day brings new quests), so read it whenever the board is shown.
        /// </summary>
        public static IReadOnlyList<FinikQuestTask> QuestTasks()
        {
            var now = Load()?.demoDay > 0 ? DateTime.Now.AddDays(state.demoDay - 1) : DateTime.Now;
            string day = FinikQuestEngine.DayKey(now);
            if (Load() == null) return Array.Empty<FinikQuestTask>();
            if (string.IsNullOrEmpty(state.quests.campaignStartDayKey))
                state.quests.campaignStartDayKey = day;

            int campaignDay = FinikQuestEngine.CampaignDay(state.quests, now);
            if (questTasks == null || questTasksDay != day)
            {
                questTasks = FinikQuestEngine.BuildTasks(now, campaignDay);
                questTasksDay = day;
            }
            if (FinikQuestEngine.Sync(state.quests, questTasks, now, NowMs)) Commit();
            return questTasks;
        }

        public static FinikQuestProgress QuestProgress(string taskId) => Load()?.quests.Find(taskId);

        public static bool IsQuestUnlocked(string taskId) =>
            FinikQuestEngine.IsUnlocked(Load()?.quests, QuestTasks(), taskId);

        public static bool AllDailyQuestsAnswered =>
            FinikQuestEngine.AllDailyAnswered(Load()?.quests, QuestTasks());

        /// <summary>Daily quests claimed this week, towards the weekly streak.</summary>
        public static int WeeklyQuestsClaimed => Load()?.quests.weeklyClaimedDaily ?? 0;

        /// <summary>Things waiting on the quest board: daily quests not played yet plus rewards to collect.</summary>
        public static int OpenQuestCount()
        {
            if (Load() == null) return 0;
            int count = 0;
            foreach (var task in QuestTasks())
            {
                var status = state.quests.Find(task.Id)?.status ?? FinikQuestStatus.Available;
                if (task.IsWeekly ? status == FinikQuestStatus.Completed : status != FinikQuestStatus.Claimed) count++;
            }
            return count;
        }

        /// <summary>Marks a daily quest as being played (web: taskStore.startTask). True when it can be played.</summary>
        public static bool StartQuest(string taskId)
        {
            if (!IsQuestUnlocked(taskId)) return false;
            if (!TryQuest(taskId, out var task, out var progress) || task.IsWeekly) return false;
            if (progress.status == FinikQuestStatus.Active) return true;
            if (progress.status != FinikQuestStatus.Available) return false;
            progress.status = FinikQuestStatus.Active;
            progress.questionIndex = 0;
            progress.goodAnswers = 0;
            progress.riskAnswers = 0;
            progress.rewardEligible = false;
            Commit();
            return true;
        }

        /// <summary>Applies the money consequence of one question. Ids are deterministic, so retrying after a crash cannot pay/spend twice.</summary>
        public static bool ApplyQuestChoiceEffect(string taskId, int questionIndex, FinikQuestChoice choice)
        {
            if (!IsQuestUnlocked(taskId)) return false;
            if (Load() == null || choice == null) return false;
            if (!TryQuest(taskId, out var task, out _) || task.IsWeekly || task.Quest == null) return false;

            int autoSave = FinikQuestEngine.AutoSaveAfterQuestion(task.Quest, questionIndex);
            int totalSave = choice.Save + autoSave;
            if (totalSave > 0 && (!HasSelectedGoal || totalSave > GoalProgress.Left)) return false;
            if (choice.Withdraw > Savings) return false;
            if (Balance + choice.Withdraw < choice.Spend + totalSave) return false;

            string prefix = $"quest-effect-{taskId}-q{questionIndex + 1}";
            long now = NowMs;
            bool changed = false;

            bool Apply(FinikTransactionKind kind, int amount, string suffix, string reason)
            {
                if (amount <= 0) return true;
                var tx = FinikWalletEngine.Make($"{prefix}-{suffix}", kind, amount, FinikTransactionSource.Task,
                    reason, state.PeriodId, now);
                var error = FinikWalletEngine.Apply(state.wallet, tx);
                if (error == FinikWalletError.DuplicateId) return true;
                if (error != FinikWalletError.None) return false;
                changed = true;
                return true;
            }

            if (!Apply(FinikTransactionKind.Withdraw, choice.Withdraw, "withdraw", $"Задание: вернуть из копилки")) return false;
            if (!Apply(FinikTransactionKind.Save, choice.Save, "save", $"Задание: отложить на цель")) return false;
            if (!Apply(FinikTransactionKind.Spend, choice.Spend, "spend", $"Задание: покупка")) return false;

            if (autoSave > 0 &&
                !Apply(FinikTransactionKind.Save, autoSave, "auto-save", $"Задание: первые накопления")) return false;

            if (changed) Commit();
            return true;
        }

        /// <summary>Persists progress and correctness between the three questions of one scenario.</summary>
        public static bool AdvanceQuestQuestion(string taskId, int nextQuestionIndex, string choiceId)
        {
            if (!IsQuestUnlocked(taskId)) return false;
            if (!TryQuest(taskId, out var task, out var progress) || task.IsWeekly || task.Quest == null) return false;
            if (progress.status != FinikQuestStatus.Active) return false;
            var choice = task.Quest.Choice(choiceId);
            if (choice == null) return false;
            if (choice.Tag == FinikQuestTag.Good) progress.goodAnswers++;
            else if (choice.Tag == FinikQuestTag.Risk) progress.riskAnswers++;
            int last = Math.Max(0, task.Quest.Questions.Length - 1);
            progress.questionIndex = Math.Clamp(nextQuestionIndex, 0, last);
            progress.choiceId = choiceId;
            Commit();
            return true;
        }

        /// <summary>
        /// Completes a daily scenario. Day-one rewards require successful answers;
        /// later daily rewards are paid regardless of answer quality.
        /// </summary>
        public static FinikQuestOutcome ResolveQuest(string taskId, string choiceId)
        {
            if (Load() == null) return new FinikQuestOutcome(FinikQuestFailure.NoJourney);
            if (!IsQuestUnlocked(taskId)) return new FinikQuestOutcome(FinikQuestFailure.NotReady);
            if (!TryQuest(taskId, out var task, out var progress) || task.IsWeekly) return new FinikQuestOutcome(FinikQuestFailure.UnknownTask);
            var choice = task.Quest.Choice(choiceId);
            if (choice == null) return new FinikQuestOutcome(FinikQuestFailure.UnknownChoice, task);
            if (progress.status == FinikQuestStatus.Completed || progress.status == FinikQuestStatus.Claimed)
                return new FinikQuestOutcome(FinikQuestFailure.AlreadyDone, task, task.Quest.Choice(progress.choiceId));

            long now = NowMs;
            if (choice.Tag == FinikQuestTag.Good) progress.goodAnswers++;
            else if (choice.Tag == FinikQuestTag.Risk) progress.riskAnswers++;
            progress.rewardEligible = progress.riskAnswers == 0 &&
                                      progress.goodAnswers >= (task.Quest?.Questions.Length ?? 1);
            progress.status = FinikQuestStatus.Completed;
            progress.choiceId = choice.Id;
            progress.questionIndex = task.Quest?.Questions.Length ?? progress.questionIndex;
            progress.completedAtMs = now;
            var paid = Claim(task, progress, now);
            Commit();
            ReactionRequested?.Invoke("task-completed", null);
            if (paid.weeklyUnlocked) ReactionRequested?.Invoke("task-completed", "big");
            return new FinikQuestOutcome(FinikQuestFailure.None, task, choice, paid.coins, paid.xp, paid.weeklyUnlocked);
        }

        /// <summary>Collects a completed task's reward, e.g. the weekly streak bonus (web: taskStore.claimTask).</summary>
        public static FinikQuestOutcome ClaimQuest(string taskId)
        {
            if (Load() == null) return new FinikQuestOutcome(FinikQuestFailure.NoJourney);
            if (!TryQuest(taskId, out var task, out var progress)) return new FinikQuestOutcome(FinikQuestFailure.UnknownTask);
            if (progress.status == FinikQuestStatus.Claimed) return new FinikQuestOutcome(FinikQuestFailure.AlreadyDone, task);
            if (progress.status != FinikQuestStatus.Completed) return new FinikQuestOutcome(FinikQuestFailure.NotReady, task);
            var paid = Claim(task, progress, NowMs);
            Commit();
            if (paid.weeklyUnlocked) ReactionRequested?.Invoke("task-completed", "big");
            return new FinikQuestOutcome(FinikQuestFailure.None, task, task.Quest?.Choice(progress.choiceId), paid.coins, paid.xp, paid.weeklyUnlocked);
        }

        static bool TryQuest(string taskId, out FinikQuestTask task, out FinikQuestProgress progress)
        {
            task = null;
            progress = null;
            if (Load() == null) return false;
            foreach (var candidate in QuestTasks())
                if (candidate.Id == taskId) task = candidate;
            progress = task != null ? state.quests.Find(task.Id) : null;
            return progress != null;
        }

        /// <summary>Pays a completed task once. From day two the 10-coin reward is paid regardless of answer quality.</summary>
        static (int coins, int xp, bool weeklyUnlocked) Claim(FinikQuestTask task, FinikQuestProgress progress, long now)
        {
            int coins = 0, xp = 0;
            bool grantReward = task.IsWeekly || (task.Quest?.Day ?? 0) >= 2 || progress.rewardEligible;
            if (grantReward)
            {
                if (task.Reward > 0)
                {
                    var transaction = FinikWalletEngine.Make(FinikQuestEngine.RewardTransactionId(task.Id), FinikTransactionKind.Earn, task.Reward,
                        FinikTransactionSource.Task, $"Задание: {task.Title}", state.PeriodId, now);
                    // A duplicate id means this task was already paid (e.g. a restored save): never pay twice.
                    if (FinikWalletEngine.Apply(state.wallet, transaction) == FinikWalletError.None) coins = task.Reward;
                }

                int taskXp = FinikGrowthEngine.TaskXp(task.Difficulty, task.IsWeekly);
                if (FinikGrowthEngine.Grant(state.growth, FinikQuestEngine.GrowthEventId(task.Id), FinikGrowthSource.Task, taskXp,
                        task.IsWeekly ? $"Недельное задание: {task.Title}" : $"Задание: {task.Title}", now))
                    xp = taskXp;
            }

            progress.status = FinikQuestStatus.Claimed;
            progress.claimedAtMs = now;
            if (task.IsWeekly) return (coins, xp, false);

            var quests = state.quests;
            quests.weeklyClaimedDaily++;
            bool unlocked = false;
            // The board TryQuest just synced; re-reading QuestTasks() here could roll the day over mid-claim.
            foreach (var other in questTasks)
            {
                if (!other.IsWeekly) continue;
                var weekly = quests.Find(other.Id);
                if (weekly != null && weekly.status == FinikQuestStatus.Available && quests.weeklyClaimedDaily >= other.TargetCount)
                {
                    weekly.completedAtMs = now;
                    if (other.Reward > 0)
                    {
                        weekly.status = FinikQuestStatus.Completed;
                        unlocked = true;
                    }
                    else
                    {
                        weekly.status = FinikQuestStatus.Claimed;
                        weekly.claimedAtMs = now;
                    }
                }
            }

            MarkTasksDoneIfDayComplete();
            return (coins, xp, unlocked);
        }

        /// <summary>Remembers the budget day on which every daily task was claimed: a bonus point in that day's review.</summary>
        static void MarkTasksDoneIfDayComplete()
        {
            if (state == null || questTasks == null) return;
            bool foundDailyTask = false;
            foreach (var task in questTasks)
            {
                if (task.IsWeekly) continue;
                foundDailyTask = true;
                var progress = state.quests.Find(task.Id);
                if (progress == null || progress.status != FinikQuestStatus.Claimed) return;
            }
            if (!foundDailyTask) return;
            state.budget ??= new FinikBudgetState();
            state.budget.tasksDoneDayKey = BudgetDayKeyFor(state);

            string periodKey = state.quests.dayKey;
            if (!string.IsNullOrEmpty(periodKey) && state.quests.lastCompletedPeriodDayKey != periodKey)
            {
                state.quests.completedPeriods = Math.Clamp(state.quests.completedPeriods + 1, 0, 5);
                state.quests.lastCompletedPeriodDayKey = periodKey;
            }
        }

        // ------------------------------------------------------------------ internals

        static bool EnsureDailyLoginIncome(FinikGameState s, DateTime local, long now)
        {
            string day = FinikQuestEngine.DayKey(local);
            string id = $"daily-login-{day}";
            if (s.wallet.Contains(id)) return false;
            var tx = FinikWalletEngine.Make(id, FinikTransactionKind.Earn, FinikWalletEngine.DailyLoginIncome,
                FinikTransactionSource.PeriodIncome, FinikWalletEngine.DailyLoginReason, $"day-{day}", now);
            return FinikWalletEngine.Apply(s.wallet, tx) == FinikWalletError.None;
        }

        static void MigrateCompletedPeriods(FinikQuestState quests)
        {
            if (quests == null)
                return;
            quests.completedPeriods = Math.Clamp(quests.completedPeriods, 0, 5);
            if (quests.completedPeriods > 0 || string.IsNullOrEmpty(quests.dayKey) || quests.progress == null)
                return;

            bool foundDaily = false;
            foreach (var progress in quests.progress)
            {
                if (progress == null || string.IsNullOrEmpty(progress.taskId) || progress.taskId.StartsWith("weekly-", StringComparison.Ordinal))
                    continue;
                foundDaily = true;
                if (progress.status != FinikQuestStatus.Claimed)
                    return;
            }

            if (foundDaily)
            {
                quests.completedPeriods = 1;
                quests.lastCompletedPeriodDayKey = quests.dayKey;
            }
        }

        static string NextTransactionId() => $"wallet-{NowMs}-{state.wallet.transactions.Count + 1}-{++transactionCounter}";

        static string UtcDate(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd");

        static FinikGameState Load()
        {
            if (state != null) return state;
            string json = PlayerPrefs.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var loaded = JsonUtility.FromJson<FinikGameState>(json);
                if (loaded == null) return null;
                loaded.wallet = FinikWalletEngine.Normalize(loaded.wallet);
                loaded.growth = FinikGrowthEngine.Normalize(loaded.growth);
                loaded.quests = FinikQuestEngine.Normalize(loaded.quests);
                MigrateCompletedPeriods(loaded.quests);
                // Saves made before the shop existed have no shop block at all.
                loaded.shop ??= new FinikShopState();
                loaded.shop.owned ??= new List<string>();
                loaded.purchasedGoalIds ??= new List<string>();
                loaded.purchasedGoalIds.RemoveAll(id => string.IsNullOrEmpty(id) || !FinikGoalCatalog.TryGet(id, out _));
                // Saves made before the budget step existed have no budget block at all.
                loaded.budget ??= new FinikBudgetState();
                loaded.budget.plan ??= new FinikBudgetPlan();
                loaded.budget.actuals ??= new FinikBudgetActuals();
                loaded.budget.lastPlan ??= new FinikBudgetPlan();
                loaded.budget.lastActuals ??= new FinikBudgetActuals();
                loaded.budget.reviews ??= new List<FinikDayReview>();
                // Same for saves made before the mood games existed.
                loaded.miniGames ??= new FinikMiniGameState();
                loaded.miniGames.games ??= new List<FinikMiniGameProgress>();
                FinikBudgetEngine.Sync(loaded.budget, BudgetDayKeyFor(loaded));
                if (loaded.care == null || loaded.care.decayedAtMs <= 0) loaded.care = FinikPetCareEngine.Initial(NowMs);
                loaded.weekNumber = Math.Max(1, loaded.weekNumber);
                loaded.petStage = Math.Clamp(loaded.petStage <= 0 ? 1 : loaded.petStage, 1, 3);
                ApplyGrowthStage(loaded);
                state = loaded;
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning($"[FinikGame] Stored game state is corrupt and will be ignored: {e.Message}");
                return null;
            }
            return state;
        }

        static void Commit()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(state));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

#if UNITY_EDITOR
        // Enter Play Mode without a domain reload keeps statics alive; start every session from disk.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            state = null;
            questTasks = null;
            Changed = null;
            ReactionRequested = null;
        }
#endif
    }
}
