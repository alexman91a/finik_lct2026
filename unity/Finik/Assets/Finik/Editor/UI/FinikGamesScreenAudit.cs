using System.Collections.Generic;
using System.Linq;
using Finik.Core;
using Finik.UI.Games;
using Finik.UI.Onboarding;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Play Mode check of the games screen at the current Game view resolution: the hub at every
    /// stage of progress (nothing cleared through everything cleared), the header of every game and
    /// level, and the result card in all its shapes. Reports text that overflows, gets cut, shrinks
    /// to its minimum size or collides with a neighbour (checks in <see cref="FinikUiAudit"/>).
    ///
    /// The menu items under Finik/UI/Games leave one state on screen for a screenshot.
    /// </summary>
    public static class FinikGamesScreenAudit
    {
        // The tick sits on the corner of its own chip by design.
        // The tick sits on the corner of its own chip, and a power's charges sit on the rim of its ring.
        static readonly (string a, string b)[] IntendedOverlaps =
        {
            ("Levels/Level1/Done", "Levels/Level1/Face"),
            ("/Ring/Badge/Count", "/Ring/Icon")
        };

        static readonly string[] ContentImages = { "Icon", "Done" };
        static readonly string[] Containers = { "Face", "Mood", "Xp", "Changes", "Goal", "Counter", "Reward", "Intro", "Dock" };

        [MenuItem("Finik/UI/Audit Games Screen")]
        static void Menu() => Debug.Log(Run());

        public static string SetResolution(int width, int height) => FinikUiAudit.SetResolution(width, height);

        public static string EnsureProfile() => FinikUiAudit.EnsureProfile();

        public static string Capture(string path) => FinikUiAudit.Capture(path);

        /// <summary>Walks every state of the hub, the play header and the result card. Leaves the hub open.</summary>
        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var screen = FindScreen(out string error);
            if (!screen) return error;
            FinikUiAudit.EnsureProfile();
            if (!screen.IsOpen && !screen.Open()) return "Games screen did not open: finish onboarding first.";

            var audit = new FinikUiAudit(IntendedOverlaps, ContentImages, Containers);
            var hub = Column(screen, "Hub");
            var play = Column(screen, "Play");
            var resultColumn = Column(screen, "Result");
            var result = (FinikGameResultView)Get(screen, "result");

            int[] restore = FinikMiniGameCatalog.Games.Select(g => FinikGame.MiniGameBestLevel(g.Id)).ToArray();

            // The hub from a first visit (everything shut but the first step) to a finished one, and an
            // endless game far along, where its card reads "Уровень 119 · сложный".
            foreach (int best in Enumerable.Range(0, FinikMiniGameCatalog.LevelCount + 1).Concat(new[] { 9, 118 }))
            {
                SetProgress(best);
                Show(screen, "Hub");
                Call(screen, "RenderHub");
                audit.Check(hub, $"хаб / пройдено {best}");
            }

            // The header of every level, with the longest counter lines the boards can produce.
            foreach (var game in FinikMiniGameCatalog.Games)
            foreach (var level in AuditLevels(game))
            {
                Set(screen, "game", game);
                Set(screen, "level", level);
                Show(screen, "Play");
                Call(screen, "RenderPlayHeader");
                Call(screen, "RenderCounters");
                audit.Check(play, $"игра / {game.Id} {level.Number}");
            }

            // Result: cleared with and without a next step, cleared with no mood left to give, and lost.
            foreach (var game in FinikMiniGameCatalog.Games)
            foreach (var level in AuditLevels(game))
            {
                bool hasNext = game.HasLevel(level.Number + 1);
                result.Show(new FinikGameResultView.Outcome(true, game, level, level.Mood, level.Xp, hasNext, false), null);
                audit.Check(resultColumn, $"итог / {game.Id} {level.Number} пройден");
                result.Show(new FinikGameResultView.Outcome(true, game, level, 0, 0, hasNext, true), null);
                audit.Check(resultColumn, $"итог / {game.Id} {level.Number} настроение полное");
                result.Show(new FinikGameResultView.Outcome(false, game, level, 0, 0, false, false), null);
                audit.Check(resultColumn, $"итог / {game.Id} {level.Number} не пройден");
            }

            for (int i = 0; i < restore.Length; i++) SetProgress(FinikMiniGameCatalog.Games[i].Id, restore[i]);
            result.Panel.Hide(instant: true);
            Show(screen, "Hub");
            Call(screen, "RenderHub");
            return audit.Report("Games screen", hub, null);
        }

        // ------------------------------------------------------------------ states for a screenshot

        [MenuItem("Finik/UI/Games/Game View: Portrait 1080x2400")]
        static void Portrait() => Debug.Log(SetResolution(1080, 2400));

        [MenuItem("Finik/UI/Games/Game View: Landscape 2400x1080")]
        static void Landscape() => Debug.Log(SetResolution(2400, 1080));

        [MenuItem("Finik/UI/Games/Show Hub")]
        static void ShowHub() => Debug.Log(Present("hub"));

        [MenuItem("Finik/UI/Games/Play Memory (16 cards)")]
        static void PlayMemory() => Debug.Log(Present("play", FinikMiniGameId.Memory, 3));

        [MenuItem("Finik/UI/Games/Play Match-3 (hard)")]
        static void PlayMatch3() => Debug.Log(Present("play", FinikMiniGameId.Match3, 3));

        [MenuItem("Finik/UI/Games/Play Catch Coins")]
        static void PlayCatch() => Debug.Log(Present("play", FinikMiniGameId.Catch, 1));

        [MenuItem("Finik/UI/Games/Show Result")]
        static void ShowResult() => Debug.Log(Present("result", FinikMiniGameId.Match3, 2));

        /// <summary>Opens one state and leaves it on screen. Stage: hub, play, result.</summary>
        public static string Present(string stage, string gameId = FinikMiniGameId.Memory, int number = 1)
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var screen = FindScreen(out string error);
            if (!screen) return error;
            FinikUiAudit.EnsureProfile();
            if (!screen.IsOpen && !screen.Open()) return "Games screen did not open: finish onboarding first.";
            if (!FinikMiniGameCatalog.TryGet(gameId, out var game)) return $"Unknown game {gameId}";
            var level = FinikMiniGameCatalog.Level(game, number);

            var result = (FinikGameResultView)Get(screen, "result");
            switch (stage)
            {
                case "play":
                    // Every step has to be open, or the screen refuses to start the hard one.
                    SetProgress(Mathf.Max(FinikMiniGameCatalog.LevelCount, number - 1));
                    Call(screen, "StartLevel", gameId, number);
                    break;
                case "result":
                    // The real screen hands the card the game's picture; without it the card keeps
                    // whatever picture the previous result left on it.
                    var art = (FinikGameArt)Get(screen, "art");
                    result.Show(new FinikGameResultView.Outcome(true, game, level, level.Mood, level.Xp,
                        game.HasLevel(number + 1), false), art ? art.Get(game.Icon) : null);
                    break;
                default:
                    Call(screen, "ShowHub");
                    break;
            }
            return $"{stage} {gameId} {number} at {Screen.width}x{Screen.height}";
        }

        // ------------------------------------------------------------------ match-3 autoplay

        /// <summary>
        /// Plays the running «три в ряд» level by itself, through the board's own moves, so every
        /// animation — swaps, powers, combos, falls, shuffles — runs for real. <paramref name="mode"/>:
        /// "moves" plays the best-looking options, "powers" prefers taps and power swaps, "mix" fires
        /// the shuffle power first.
        /// </summary>
        public static string AutoPlay(int moves = 10, string mode = "moves")
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var board = Object.FindAnyObjectByType<FinikMatch3Board>();
            if (!board || !board.Running) return "No match-3 level is running: Present(\"play\", \"match3\", n) first.";
            board.StartCoroutine(AutoPlayRoutine(board, moves, mode));
            return $"autoplay {moves} ({mode})";
        }

        static System.Collections.IEnumerator AutoPlayRoutine(FinikMatch3Board board, int moves, string mode)
        {
            if (mode == "mix")
            {
                while (board.Running && (bool)Get(board, "busy")) yield return null;
                board.UseBooster(FinikBoosterId.Mix);
            }
            for (int i = 0; i < moves && board.Running; i++)
            {
                while (board.Running && (bool)Get(board, "busy")) yield return null;
                if (!board.Running) yield break;
                var model = (FinikMatch3Model)Get(board, "model");
                var options = model.Options();
                if (options.Count == 0) yield break;
                var pick = options[0];
                foreach (var option in options)
                {
                    bool power = option.IsTap || model.PieceAt(option.A).Special != FinikMatch3Special.None ||
                                 model.PieceAt(option.B).Special != FinikMatch3Special.None;
                    if (mode == "powers" && power) { pick = option; break; }
                    if (Random.value < 0.25f) pick = option;
                }
                var routine = pick.IsTap
                    ? (System.Collections.IEnumerator)Invoke(board, "FirePower", pick.A)
                    : (System.Collections.IEnumerator)Invoke(board, "TrySwap", pick.A, pick.B);
                board.StartCoroutine(routine);
                yield return null;
            }
        }

        /// <summary>
        /// Films the running board from inside the player loop: <paramref name="frames"/> screenshots,
        /// <paramref name="interval"/> seconds apart, to <paramref name="prefix"/>N.png. A shuffle lasts
        /// a second — far shorter than one round trip of an editor command.
        /// </summary>
        public static string Film(string prefix, int frames = 6, float interval = 0.15f, float delay = 0f)
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var board = Object.FindAnyObjectByType<FinikMatch3Board>();
            if (!board) return "No match-3 board.";
            board.StartCoroutine(FilmRoutine(prefix, frames, interval, delay));
            return $"filming {frames} frames";
        }

        static System.Collections.IEnumerator FilmRoutine(string prefix, int frames, float interval, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            for (int i = 0; i < frames; i++)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot($"{prefix}{i}.png");
                yield return new WaitForSecondsRealtime(interval);
            }
        }

        static object Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                                                 System.Reflection.BindingFlags.Public)?.Invoke(target, args);

        // ------------------------------------------------------------------ plumbing

        /// <summary>
        /// The fixed steps, plus for an endless game a few generated ones far along: long headings
        /// ("Уровень 118 · сложный") and multi-goal levels.
        /// </summary>
        static IEnumerable<FinikMiniGameLevel> AuditLevels(FinikMiniGame game)
        {
            foreach (var level in game.Levels) yield return level;
            if (!game.Endless) yield break;
            foreach (int number in new[] { 5, 18, 118 }) yield return game.Level(number);
        }

        static FinikGamesScreen FindScreen(out string error)
        {
            var screen = Object.FindAnyObjectByType<FinikGamesScreen>(FindObjectsInactive.Include);
            error = screen ? null : "Screen_Games is missing: run Finik/UI/Rebuild Games Screen.";
            return screen;
        }

        static RectTransform Column(FinikGamesScreen screen, string panelName) =>
            (RectTransform)screen.transform.Find($"SafeArea/{panelName}/Column");

        /// <summary>Brings one of the three panels up instantly and hides the others.</summary>
        static void Show(FinikGamesScreen screen, string panelName)
        {
            foreach (string name in new[] { "Hub", "Play", "Result" })
            {
                var panel = screen.transform.Find($"SafeArea/{name}")?.GetComponent<FinikScreenPanel>();
                if (!panel) continue;
                if (name == panelName) panel.Show(instant: true);
                else panel.Hide(instant: true);
            }
        }

        static void SetProgress(int bestLevel)
        {
            foreach (var game in FinikMiniGameCatalog.Games) SetProgress(game.Id, bestLevel);
        }

        static void SetProgress(string gameId, int bestLevel)
        {
            var state = FinikGame.State;
            if (state == null) return;
            state.miniGames ??= new FinikMiniGameState();
            state.miniGames.Ensure(gameId).bestLevel = bestLevel;
        }

        static object Get(object target, string field) => FinikUiAudit.Get(target, field);
        static void Set(object target, string field, object value) => FinikUiAudit.Set(target, field, value);
        static void Call(object target, string method, params object[] args) => FinikUiAudit.Call(target, method, args);
    }
}
