using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Finik.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Finik.UI.Games
{
    /// <summary>
    /// «Три в ряд», the view. The rules live in <see cref="FinikMatch3Model"/>; this board turns a
    /// finger on the glass into a move, hands it to the model and plays back what the model says
    /// happened — clears, powers flying, crates cracking, gems falling, the board shuffling.
    ///
    /// A gem is moved by dragging it onto a neighbour (tapping one and then the other still works, for
    /// a child who has not found the drag yet). A swap that makes nothing slides back and costs no
    /// move. A tap on a power sets it off. When the board runs out of moves it shuffles itself for
    /// free — with the pieces visibly flying — and if the child sits still for a few seconds two gems
    /// that would match give a little nudge.
    /// </summary>
    public sealed class FinikMatch3Board : FinikMiniGameBoard,
        IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
    {
        const float GapRatio = 0.10f;
        /// <summary>Gem size as a share of its cell.</summary>
        const float GemScale = 0.9f;
        const float SwapSeconds = 0.14f;
        /// <summary>Travel, in cells, under which a touch is still a tap and not a drag.</summary>
        const float TapSlop = 0.15f;
        /// <summary>How far, in cells, a gem has to be pulled for letting go to count as a swap.</summary>
        const float ReleaseThreshold = 0.35f;
        /// <summary>How far a gem gives towards an edge or a crate it cannot swap with.</summary>
        const float BlockedGive = 0.12f;
        const float HintAfter = 5f;
        const float HintSeconds = 1.3f;

        const string RocketSprite = "game_special_rocket";
        const string PlaneSprite = "game_special_plane";
        const string BombSprite = "game_special_bomb";
        const string DiscoSprite = "game_special_disco";
        const string CrateSprite = "game_crate";
        const string StrongCrateSprite = "game_crate_strong";
        const string IceSprite = "game_ice";
        const string ThickIceSprite = "game_ice_thick";

        /// <summary>Pictures this board draws on top of the gems; the builder wires them into the art table.</summary>
        public static readonly string[] ArtNames =
        {
            RocketSprite, PlaneSprite, BombSprite, DiscoSprite, CrateSprite, StrongCrateSprite, IceSprite, ThickIceSprite,
            "ui_check", "food_combo_check"
        };

        [Tooltip("Ring drawn over the gem the player picked first.")]
        [SerializeField] string selectionSprite = "ui_card_selected";

        /// <summary>Spark colour per gem, in the order of <see cref="FinikMiniGameCatalog.GemSprites"/>.</summary>
        static readonly Color[] GemColors =
        {
            new(1f, 0.33f, 0.33f), new(0.29f, 0.62f, 1f), new(1f, 0.83f, 0.24f),
            new(0.36f, 0.85f, 0.42f), new(0.72f, 0.45f, 1f), new(1f, 0.6f, 0.2f)
        };

        static readonly Color Gold = new(1f, 0.82f, 0.3f);
        static readonly Color Wood = new(0.86f, 0.6f, 0.3f);
        static readonly Color Frost = new(0.72f, 0.92f, 1f);

        static readonly string[] Cheers = { "Отлично!", "Здорово!", "Супер!", "Вот это да!" };

        sealed class Piece
        {
            public int Id;
            public RectTransform Rect;
            public Image Image;
            public int Cell;
            public int Color;
            public FinikMatch3Special Special;
        }

        sealed class Tween
        {
            public float Delay;
            public float Duration;
            public float Age;
            public Action<float> Step;
            public Action Done;
        }

        FinikMatch3Level level;
        FinikMatch3Model model;
        int columns, rows;
        int movesLeft;
        bool busy;

        readonly Dictionary<int, Piece> pieces = new();
        Image[] slots;
        Image[] crates;
        Image[] ice;
        RectTransform slotLayer, iceLayer, pieceLayer, crateLayer, overlay;
        Image selection;
        int picked = -1;

        float cell, step;
        Vector2 origin;

        readonly List<Tween> tweens = new();
        /// <summary>Pieces flying into the goal card. Kept apart from <see cref="tweens"/>: the board does not wait for them.</summary>
        readonly List<Tween> flights = new();
        /// <summary>Per goal: pieces sent flying to the card, and those that got there.</summary>
        int[] launched = Array.Empty<int>();
        int[] landed = Array.Empty<int>();
        readonly List<FinikGoalItem> goalItems = new();

        // Input.
        int pressCell = -1;
        int pressPointer;
        Vector2 pressLocal;
        /// <summary>The finger has moved past the tap slop: letting go is a drop, not a tap.</summary>
        bool dragging;
        /// <summary>The neighbour the gem is being pulled towards, or -1.</summary>
        int dragTarget = -1;
        /// <summary>How far the gem is pulled towards <see cref="dragTarget"/>, in layer units.</summary>
        float dragOffset;

        // Idle hint.
        float idle;
        float hintAge = -1f;
        Piece hintA, hintB;

        // ------------------------------------------------------------------ what the HUD shows

        /// <summary>The board's own goals once it is dealt (they count what is really on it), the level's before.</summary>
        IReadOnlyList<FinikMatch3Goal> Goals => model != null ? model.Goals : level?.Goals ?? Array.Empty<FinikMatch3Goal>();

        /// <summary>
        /// What the goal card shows as collected: the pieces that have landed in it. The model settles
        /// a whole turn, cascades and all, before any of it is shown; counting landings keeps the card
        /// in step with what the child sees, as in Homescapes.
        /// </summary>
        int Collected(int goal) => goal >= 0 && goal < landed.Length ? landed[goal] : 0;

        // The goals are always pictures with a count on the card, never a sentence: a child reads
        // "🟨 12" before they can read "Собери 12 жёлтых кубиков".
        public override string GoalIcon => "icon_games";
        public override string GoalValue => string.Empty;

        public override float GoalProgress
        {
            get
            {
                int need = 0, have = 0;
                for (int i = 0; i < Goals.Count; i++)
                {
                    need += Goals[i].Count;
                    have += Mathf.Min(Goals[i].Count, Collected(i));
                }
                return need == 0 ? 0f : have / (float)need;
            }
        }

        public override IReadOnlyList<FinikGoalItem> GoalItems
        {
            get
            {
                goalItems.Clear();
                if (level == null) return goalItems;
                for (int i = 0; i < Goals.Count; i++)
                    goalItems.Add(new FinikGoalItem(GoalSprite(Goals[i]), Mathf.Max(0, Goals[i].Count - Collected(i))));
                return goalItems;
            }
        }

        static string GoalSprite(FinikMatch3Goal goal) => goal.Kind switch
        {
            FinikMatch3GoalKind.Color => FinikMiniGameCatalog.GemSprites[Mathf.Clamp(goal.Color, 0, FinikMiniGameCatalog.GemSprites.Length - 1)],
            FinikMatch3GoalKind.Crate => CrateSprite,
            FinikMatch3GoalKind.Ice => IceSprite,
            _ => "icon_games"
        };

        public override string CounterIcon => "game_moves";
        public override string CounterValue => model == null ? "…" : movesLeft.ToString();
        public override string CounterLabel => FinikTypography.Plural(movesLeft, "ход", "хода", "ходов");
        public override bool CounterLow => model != null && movesLeft <= 3;
        public override string LossReason => "Закончились ходы";

        protected override bool Accepts(FinikMiniGameLevel candidate) => candidate is FinikMatch3Level;

        protected override void Begin(FinikMiniGameLevel candidate)
        {
            level = (FinikMatch3Level)candidate;
            columns = level.Columns;
            model = null;
            movesLeft = 0;
            picked = -1;
            busy = true;
            StartCoroutine(Setup());
        }

        protected override void Clear()
        {
            level = null;
            model = null;
            tweens.Clear();
            flights.Clear();
            launched = landed = Array.Empty<int>();
            pieces.Clear();
            slots = crates = ice = null;
            slotLayer = iceLayer = pieceLayer = crateLayer = overlay = null;
            selection = null;
            picked = -1;
            pressCell = -1;
            dragTarget = -1;
            dragging = false;
            StopHint();
            busy = false;
            ClearField();
        }

        // ------------------------------------------------------------------ setup

        /// <summary>
        /// Waits for the layout to give the board its size, picks the rows that fit, then lets the
        /// generator build and test the board off the main thread before dealing it.
        /// </summary>
        IEnumerator Setup()
        {
            yield return null;
            // Wait for the area to settle, not just to exist: the dock under the board is measured a
            // frame later, and a board dealt before that gets the smallest number of rows every time.
            var last = Vector2.zero;
            for (int guard = 0; guard < 30; guard++)
            {
                if (!Running) yield break;
                var size = Field.rect.size;
                if (size.x > 1f && size.y > 1f && (size - last).sqrMagnitude < 0.5f) break;
                last = size;
                yield return null;
            }
            if (!Running) yield break;

            int wanted = FitRows();
            FinikMatch3Level.LastRows = wanted;
            FinikMatch3Setup setup = null;
            Task<FinikMatch3Setup> task = null;
            try
            {
                task = level.SetupAsync(wanted);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            while (task != null && !task.IsCompleted) yield return null;
            if (!Running) yield break;
            if (task != null && task.Status == TaskStatus.RanToCompletion) setup = task.Result;
            if (setup == null)
            {
                if (task?.Exception != null) Debug.LogException(task.Exception);
                // Never leave the child in front of an empty board: a plain one with the same goals.
                setup = new FinikMatch3Setup(FinikMatch3Layout.Rectangle(columns, wanted, level.Colors, ToArray(level.Goals)), 25, 0f);
            }

            var layout = setup.Layout;
            columns = layout.Columns;
            rows = layout.Rows;
            model = new FinikMatch3Model(layout, Environment.TickCount ^ (level.Number * 7919));
            launched = new int[layout.Goals.Length];
            landed = new int[layout.Goals.Length];
            model.Deal();
            movesLeft = setup.Moves;

            BuildLayers();
            BuildCells();
            foreach (int index in AllCells())
                if (model.PieceAt(index).Exists) Spawn(model.PieceAt(index), index);
            BuildSelection();
            Relayout();
            RaiseChanged();
            yield return DropIn();
        }

        static FinikMatch3Goal[] ToArray(IReadOnlyList<FinikMatch3Goal> goals)
        {
            var result = new FinikMatch3Goal[goals.Count];
            for (int i = 0; i < result.Length; i++) result[i] = goals[i];
            return result;
        }

        IEnumerable<int> AllCells()
        {
            for (int i = 0; i < columns * rows; i++) yield return i;
        }

        /// <summary>
        /// How many rows fit at the cell size the width allows, between the level's own and a few more.
        /// Settled once per level: re-deciding it on a rotation would rebuild the board mid-game.
        /// </summary>
        int FitRows()
        {
            var size = Field.rect.size;
            if (size.x < 1f || size.y < 1f) return level.Rows;
            float cellFromWidth = size.x / (columns + GapRatio * (columns + 1));
            int fits = Mathf.FloorToInt((size.y - cellFromWidth * GapRatio) / (cellFromWidth * (1f + GapRatio)));
            return Mathf.Clamp(fits, level.Rows, level.MaxRows);
        }

        void BuildLayers()
        {
            slotLayer = NewPiece("Slots");
            iceLayer = NewPiece("Ice");
            pieceLayer = NewPiece("Pieces");
            // New gems drop in from above the grid; the mask keeps them from sliding over the goal card.
            pieceLayer.gameObject.AddComponent<RectMask2D>();
            crateLayer = NewPiece("Crates");
            overlay = NewPiece("Overlay");
        }

        void BuildCells()
        {
            int n = columns * rows;
            slots = new Image[n];
            crates = new Image[n];
            ice = new Image[n];
            for (int i = 0; i < n; i++)
            {
                if (!model.IsOpen(i)) continue;
                int x = i % columns, y = i / columns;
                var slot = NewImage($"Slot{x}_{y}", slotLayer, "game_cell", raycast: true, preserveAspect: false);
                slot.type = Image.Type.Sliced;
                // A checkerboard, as every match-3 has; quiet, so the gems are what the eye finds.
                slot.color = (x + y) % 2 == 0 ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.3f);
                slots[i] = slot;

                if (model.IceAt(i) > 0)
                    ice[i] = NewImage($"Ice{x}_{y}", iceLayer, model.IceAt(i) > 1 ? ThickIceSprite : IceSprite);
                if (model.CrateAt(i) > 0)
                    crates[i] = NewImage($"Crate{x}_{y}", crateLayer, model.CrateAt(i) > 1 ? StrongCrateSprite : CrateSprite, raycast: true);
            }
        }

        Piece Spawn(FinikMatch3Piece data, int at)
        {
            var image = NewImage($"Piece{data.Id}", pieceLayer, SpriteFor(data.Special, data.Color), raycast: true);
            // Blue and violet gems sit on a blue board; a shadow under every piece lifts them off it.
            var shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.04f, 0.06f, 0.18f, 0.45f);
            shadow.effectDistance = new Vector2(0f, -7f);
            var piece = new Piece
            {
                Id = data.Id, Rect = image.rectTransform, Image = image, Cell = at, Color = data.Color, Special = data.Special
            };
            pieces[data.Id] = piece;
            Dress(piece);
            piece.Rect.anchoredPosition = PieceAt(at);
            return piece;
        }

        string SpriteFor(FinikMatch3Special special, int color) => special switch
        {
            FinikMatch3Special.RocketH or FinikMatch3Special.RocketV => Pick(RocketSprite, "booster_hammer"),
            FinikMatch3Special.Plane => Pick(PlaneSprite, "booster_reveal"),
            FinikMatch3Special.Bomb => Pick(BombSprite, "booster_bomb"),
            FinikMatch3Special.Rainbow => Pick(DiscoSprite, "booster_rainbow"),
            _ => FinikMiniGameCatalog.GemSprites[Mathf.Clamp(color, 0, FinikMiniGameCatalog.GemSprites.Length - 1)]
        };

        string Pick(string wanted, string fallback) => art && !art.Has(wanted) ? fallback : wanted;

        /// <summary>Size and turn of a piece: powers a touch bigger than gems, a column rocket stands up.</summary>
        void Dress(Piece piece)
        {
            float size = cell * (piece.Special == FinikMatch3Special.None ? GemScale : 0.98f);
            piece.Rect.sizeDelta = new Vector2(size, size);
            piece.Rect.localRotation = Quaternion.Euler(0f, 0f, piece.Special == FinikMatch3Special.RocketV ? 90f : 0f);
            piece.Rect.localScale = Vector3.one;
        }

        /// <summary>
        /// The opening: the gems rain into the board column by column. Purely for show — the board is
        /// already dealt — but it is what makes a level feel like it begins rather than appears.
        /// </summary>
        IEnumerator DropIn()
        {
            const float fall = 0.34f, stagger = 0.035f;
            float lift = Field.rect.height * 0.7f;
            foreach (var piece in pieces.Values)
            {
                var p = piece;
                int x = p.Cell % columns, y = p.Cell / columns;
                var landing = PieceAt(p.Cell);
                var start = landing + Vector2.up * (lift + y * step);
                p.Rect.anchoredPosition = start;
                Animate(fall, k =>
                {
                    if (p.Rect) p.Rect.anchoredPosition = Vector2.LerpUnclamped(start, PieceAt(p.Cell), FinikUiMotion.EaseOutBack(k, 1.05f));
                }, null, stagger * (x + y));
            }
            yield return Settle();
            Relayout();
            busy = false;
        }

        void BuildSelection()
        {
            selection = NewImage("Selection", overlay, selectionSprite, preserveAspect: false);
            selection.type = Image.Type.Sliced;
            selection.raycastTarget = false;
            selection.gameObject.SetActive(false);
        }

        Vector2 Position(int at) => CellPosition(origin, step, at % Mathf.Max(1, columns), at / Mathf.Max(1, columns));

        protected override void Relayout()
        {
            if (level == null || model == null || slots == null) return;
            Metrics(columns, rows, GapRatio, out cell, out step, out origin);
            var grid = GridSize(columns, rows, cell, step);
            // The plate reaches only as far as the grid's own outer gap: any wider and it ran into the
            // goal row above and off the sides of a phone.
            FitTray(grid, cell * GapRatio * 1.2f, GridCenter(origin, step, columns, rows));
            if (pieceLayer)
            {
                // The mask stops at the plate's top edge, so new gems appear out of it rather than over
                // the goal card; sideways and below there is room for a clearing gem to swell.
                float top = cell * GapRatio * 1.2f, rest = step * 0.5f;
                pieceLayer.sizeDelta = grid + new Vector2(rest * 2f, top + rest);
                pieceLayer.anchoredPosition = GridCenter(origin, step, columns, rows) + new Vector2(0f, (top - rest) / 2f);
            }
            // Pieces sit in their own, masked layer centred on the grid; the others on the field's centre.
            var center = pieceLayer ? pieceLayer.anchoredPosition : Vector2.zero;
            for (int i = 0; i < slots.Length; i++)
            {
                var at = Position(i);
                if (slots[i])
                {
                    slots[i].rectTransform.sizeDelta = new Vector2(step, step);
                    slots[i].rectTransform.anchoredPosition = at;
                }
                if (ice[i])
                {
                    ice[i].rectTransform.sizeDelta = new Vector2(cell * 1.04f, cell * 1.04f);
                    ice[i].rectTransform.anchoredPosition = at;
                }
                if (crates[i])
                {
                    crates[i].rectTransform.sizeDelta = new Vector2(cell * 1.02f, cell * 1.02f);
                    crates[i].rectTransform.anchoredPosition = at;
                }
            }
            foreach (var piece in pieces.Values)
            {
                if (!piece.Rect) continue;
                Dress(piece);
                piece.Rect.anchoredPosition = Position(piece.Cell) - center;
            }
            if (selection) selection.rectTransform.sizeDelta = new Vector2(cell * 1.18f, cell * 1.18f);
            ShowSelection();
        }

        // ------------------------------------------------------------------ tweens

        /// <summary>Runs <paramref name="stepAction"/> with 0..1 over <paramref name="duration"/>, after <paramref name="delay"/>.</summary>
        void Animate(float duration, Action<float> stepAction, Action done = null, float delay = 0f) =>
            tweens.Add(new Tween { Duration = duration, Step = stepAction, Done = done, Delay = delay });

        void After(float delay, Action action) => Animate(0f, null, action, delay);

        IEnumerator Settle()
        {
            while (tweens.Count > 0) yield return null;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Run(tweens, dt);
            Run(flights, dt);
            UpdateHint(dt);
        }

        static void Run(List<Tween> list, float dt)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var tween = list[i];
                tween.Age += dt;
                if (tween.Age < tween.Delay) continue;
                float k = tween.Duration <= 0f ? 1f : Mathf.Clamp01((tween.Age - tween.Delay) / tween.Duration);
                try
                {
                    tween.Step?.Invoke(k);
                    if (k >= 1f) tween.Done?.Invoke();
                }
                catch (Exception error)
                {
                    // One broken animation must not freeze the board behind a "busy" that never clears.
                    Debug.LogException(error);
                    k = 1f;
                }
                if (k < 1f) continue;
                list.RemoveAt(i);
                i--;
            }
        }

        // Pieces sit in their own layer, centred on the grid rather than on the field.
        Vector2 PieceAt(int at) => Position(at) - (pieceLayer ? pieceLayer.anchoredPosition : Vector2.zero);

        // ------------------------------------------------------------------ input

        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        bool ToLocal(PointerEventData eventData, out Vector2 local)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Field, eventData.position, eventData.pressEventCamera, out local))
                return false;
            local -= Field.rect.center;
            return true;
        }

        int CellAt(Vector2 local)
        {
            if (model == null || step <= 0f) return -1;
            int x = Mathf.RoundToInt((local.x - origin.x) / step);
            int y = Mathf.RoundToInt((origin.y - local.y) / step);
            if (x < 0 || y < 0 || x >= columns || y >= rows) return -1;
            int at = x + y * columns;
            return model.IsOpen(at) ? at : -1;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Running || busy || model == null || pressCell >= 0) return;
            if (!ToLocal(eventData, out var local)) return;
            int at = CellAt(local);
            if (at < 0) return;
            StopHint();
            pressCell = at;
            pressPointer = eventData.pointerId;
            pressLocal = local;
            dragging = false;
            dragTarget = -1;
            dragOffset = 0f;
        }

        /// <summary>
        /// While the finger is down nothing is decided: the gem rides along under it, up to a whole
        /// cell, and the neighbour it heads for slides the other way, so the child sees the swap they
        /// are about to make — and can pull back or turn to another neighbour. Only letting go counts.
        /// </summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (pressCell < 0 || eventData.pointerId != pressPointer) return;
            if (busy || !string.IsNullOrEmpty(ArmedBooster) || !model.IsMovable(pressCell)) return;
            if (!ToLocal(eventData, out var local)) return;
            var delta = local - pressLocal;
            if (!dragging && delta.magnitude < step * TapSlop) return;
            dragging = true;
            if (picked >= 0)
            {
                picked = -1;
                ShowSelection();
            }

            bool horizontal = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);
            float along = horizontal ? delta.x : delta.y;
            int x = pressCell % columns, y = pressCell / columns;
            int tx = x + (horizontal ? (int)Mathf.Sign(along) : 0);
            int ty = y - (horizontal ? 0 : (int)Mathf.Sign(along));
            int target = tx >= 0 && ty >= 0 && tx < columns && ty < rows && model.IsMovable(tx + ty * columns) ? tx + ty * columns : -1;

            if (target != dragTarget) ReturnHome(dragTarget);
            dragTarget = target;
            // Towards a neighbour the gem follows for a whole cell; towards an edge or a crate it only gives a little.
            float reach = target >= 0 ? step : step * BlockedGive;
            dragOffset = Mathf.Clamp(along, -reach, reach);
            var offset = horizontal ? new Vector2(dragOffset, 0f) : new Vector2(0f, dragOffset);

            var held = Find(pressCell);
            if (held != null && held.Rect) held.Rect.anchoredPosition = PieceAt(pressCell) + offset;
            var other = Find(target);
            if (other != null && other.Rect) other.Rect.anchoredPosition = PieceAt(target) - offset;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (pressCell < 0 || eventData.pointerId != pressPointer) return;
            int at = pressCell, target = dragTarget;
            bool dropped = dragging;
            float pulled = Mathf.Abs(dragOffset);
            pressCell = -1;
            dragTarget = -1;
            dragging = false;
            dragOffset = 0f;

            if (!dropped)
            {
                Tap(at);
                return;
            }
            if (!busy && target >= 0 && pulled >= step * ReleaseThreshold)
            {
                // Let go far enough: the swap finishes from wherever the two gems are now.
                StartCoroutine(TrySwap(at, target));
                return;
            }
            ReturnHome(at);
            ReturnHome(target);
        }

        /// <summary>Slides the gem on <paramref name="at"/> back into its own cell.</summary>
        void ReturnHome(int at)
        {
            var piece = Find(at);
            if (piece == null || !piece.Rect) return;
            var from = piece.Rect.anchoredPosition;
            Animate(0.1f, k =>
            {
                if (!piece.Rect || busy) return;
                // The finger came back for it mid-way: the finger wins.
                if (pressCell >= 0 && (piece.Cell == pressCell || piece.Cell == dragTarget)) return;
                piece.Rect.anchoredPosition = Vector2.Lerp(from, PieceAt(piece.Cell), Mathf.SmoothStep(0f, 1f, k));
            });
        }

        Piece Find(int at)
        {
            if (model == null || at < 0) return null;
            var data = model.PieceAt(at);
            return data.Exists && pieces.TryGetValue(data.Id, out var piece) ? piece : null;
        }

        void Tap(int at)
        {
            if (!Running || busy || model == null) return;
            if (!string.IsNullOrEmpty(ArmedBooster))
            {
                StartCoroutine(Strike(Blast(at, ArmedBooster), ArmedBooster));
                return;
            }
            if (!model.IsMovable(at)) return;

            bool power = model.PieceAt(at).Special != FinikMatch3Special.None;
            if (picked >= 0 && picked != at && model.Adjacent(picked, at))
            {
                int from = picked;
                picked = -1;
                ShowSelection();
                StartCoroutine(TrySwap(from, at));
                return;
            }
            if (power)
            {
                // A power goes off where it stands: the quickest way to see what it does.
                picked = -1;
                ShowSelection();
                StartCoroutine(FirePower(at));
                return;
            }
            // A second tap on the same gem lets go; a far-away tap is a new pick, not a failed swap.
            picked = picked == at ? -1 : at;
            ShowSelection();
        }

        void ShowSelection()
        {
            if (!selection) return;
            selection.gameObject.SetActive(picked >= 0);
            if (picked >= 0) selection.rectTransform.anchoredPosition = Position(picked);
        }

        // ------------------------------------------------------------------ turns

        IEnumerator TrySwap(int from, int to)
        {
            busy = true;
            var a = Find(from);
            var b = Find(to);
            var turn = model.Swap(from, to);
            if (turn == null)
            {
                // Nothing came of it: they touch and go back, and the child tries again for free.
                yield return Slide(a, PieceAt(to), b, PieceAt(from), SwapSeconds);
                yield return Slide(a, PieceAt(from), b, PieceAt(to), SwapSeconds);
                busy = false;
                yield break;
            }
            if (a != null) a.Cell = to;
            if (b != null) b.Cell = from;
            yield return Slide(a, PieceAt(to), b, PieceAt(from), SwapSeconds);
            movesLeft--;
            RaiseChanged();
            yield return PlayTurn(turn);
            yield return EndTurn();
        }

        IEnumerator FirePower(int at)
        {
            busy = true;
            var turn = model.Tap(at);
            if (turn == null)
            {
                busy = false;
                yield break;
            }
            movesLeft--;
            RaiseChanged();
            yield return PlayTurn(turn);
            yield return EndTurn();
        }

        /// <summary>A won level waits for the last pieces to land in the goals before the result comes up.</summary>
        IEnumerator Landed()
        {
            while (flights.Count > 0) yield return null;
            RaiseChanged();
        }

        IEnumerator EndTurn()
        {
            if (model.Won || movesLeft <= 0) yield return Landed();
            if (!Running) yield break;
            if (model.Won) Finish(true);
            else if (movesLeft <= 0) Finish(false);
            busy = false;
            idle = 0f;
        }

        IEnumerator Slide(Piece a, Vector2 aTo, Piece b, Vector2 bTo, float seconds)
        {
            Vector2 aFrom = a != null && a.Rect ? a.Rect.anchoredPosition : aTo;
            Vector2 bFrom = b != null && b.Rect ? b.Rect.anchoredPosition : bTo;
            Animate(seconds, k =>
            {
                float e = Mathf.SmoothStep(0f, 1f, k);
                if (a != null && a.Rect) a.Rect.anchoredPosition = Vector2.Lerp(aFrom, aTo, e);
                if (b != null && b.Rect) b.Rect.anchoredPosition = Vector2.Lerp(bFrom, bTo, e);
            });
            yield return Settle();
        }

        IEnumerator PlayTurn(FinikMatch3Turn turn)
        {
            foreach (var stepData in turn.Steps)
            {
                switch (stepData)
                {
                    case FinikMatch3Blast blast:
                        PlayBlast(blast);
                        break;
                    case FinikMatch3Fall fall:
                        PlayFall(fall);
                        break;
                    case FinikMatch3Shuffle shuffle:
                        PlayShuffle(shuffle);
                        break;
                }
                yield return Settle();
                RaiseChanged();
            }
        }

        // ------------------------------------------------------------------ super powers

        public override bool UseBooster(string boosterId)
        {
            if (!Running || busy || model == null) return false;
            switch (boosterId)
            {
                case FinikBoosterId.Hammer:
                case FinikBoosterId.Bomb:
                    // These need a piece to aim at: arm them and let the next tap decide.
                    picked = -1;
                    ShowSelection();
                    Arm(ArmedBooster == boosterId ? null : boosterId);
                    return ArmedBooster == boosterId;
                case FinikBoosterId.Mix:
                    StartCoroutine(MixRoutine());
                    return true;
                default:
                    return false;
            }
        }

        IEnumerator MixRoutine()
        {
            busy = true;
            Spend(FinikBoosterId.Mix);
            yield return PlayTurn(model.ShufflePower());
            busy = false;
            idle = 0f;
        }

        /// <summary>Clears the cells a fired power hit, then lets gravity and cascades do the rest.</summary>
        IEnumerator Strike(List<int> cells, string boosterId)
        {
            busy = true;
            Arm(null);
            Spend(boosterId);
            if (fx) fx.Shake(Field, boosterId == FinikBoosterId.Bomb ? 20f : 12f);
            var turn = model.Strike(cells);
            if (turn != null) yield return PlayTurn(turn);
            if (model.Won)
            {
                yield return Landed();
                Finish(true);
            }
            busy = false;
            idle = 0f;
        }

        /// <summary>Which cells a power takes out when it is aimed at <paramref name="at"/>.</summary>
        List<int> Blast(int at, string boosterId)
        {
            var hit = new List<int>();
            int radius = boosterId == FinikBoosterId.Bomb ? 1 : 0;
            int cx = at % columns, cy = at / columns;
            for (int x = cx - radius; x <= cx + radius; x++)
            for (int y = cy - radius; y <= cy + radius; y++)
                if (x >= 0 && y >= 0 && x < columns && y < rows && model.IsOpen(x + y * columns))
                    hit.Add(x + y * columns);
            return hit;
        }

        // ------------------------------------------------------------------ playback: blast

        void PlayBlast(FinikMatch3Blast blast)
        {
            // A chain reaction is the best thing that happens in this game: say so out loud.
            if (blast.Cascade >= 2 && fx)
            {
                fx.Say(Vector2.zero, Cheers[Mathf.Min(blast.Cascade - 2, Cheers.Length - 1)], new Color(1f, 0.72f, 0.16f), cell * 0.7f);
                fx.Shake(Field, Mathf.Min(6f + blast.Cascade * 4f, 20f));
            }
            foreach (var removal in blast.Removed) ScheduleRemoval(removal);
            foreach (var created in blast.Created) ScheduleCreated(created);
            foreach (var change in blast.Transformed) ScheduleTransform(change);
            foreach (var hit in blast.Obstacles) ScheduleObstacle(hit);
            foreach (var effect in blast.Effects) ScheduleEffect(effect);
        }

        void ScheduleRemoval(FinikMatch3Removal removal)
        {
            if (!pieces.TryGetValue(removal.Id, out var piece)) return;
            var rect = piece.Rect;
            // Forgotten only when it actually goes: a disco ball may still turn it into a power first.
            After(removal.Delay, () => pieces.Remove(removal.Id));
            if (!rect) return;

            if (removal.MergeInto >= 0)
            {
                // The gems of a big match run together into the power they make; a copy of each one the
                // goals want still flies off to the card.
                int wanted = removal.Special == FinikMatch3Special.None ? GoalFor(FinikMatch3GoalKind.Color, removal.Color) : -1;
                if (wanted >= 0)
                {
                    Claim(wanted);
                    After(removal.Delay, () =>
                        Launch(SpriteFor(FinikMatch3Special.None, removal.Color), Position(removal.Cell), wanted, cell * GemScale));
                }
                var into = PieceAt(removal.MergeInto);
                After(removal.Delay, () =>
                {
                    if (!rect) return;
                    var from = rect.anchoredPosition;
                    Animate(0.13f, k =>
                    {
                        if (!rect) return;
                        rect.anchoredPosition = Vector2.Lerp(from, into, k * k);
                        float s = Mathf.Lerp(1f, 0.55f, k);
                        rect.localScale = new Vector3(s, s, 1f);
                    }, () => { if (rect) Destroy(rect.gameObject); });
                });
                return;
            }

            bool power = removal.Special != FinikMatch3Special.None;
            int goal = power ? -1 : GoalFor(FinikMatch3GoalKind.Color, removal.Color);
            if (goal >= 0)
            {
                // A gem the goals want does not burst: it takes off and flies into its goal.
                Claim(goal);
                After(removal.Delay, () =>
                {
                    if (!rect) return;
                    var where = Position(removal.Cell);
                    if (fx) fx.Flash(where, cell * 1.3f, new Color(1f, 1f, 1f, 0.6f));
                    Launch(SpriteFor(FinikMatch3Special.None, removal.Color), where, goal, cell * GemScale);
                    Destroy(rect.gameObject);
                });
                return;
            }
            After(removal.Delay, () =>
            {
                if (!rect) return;
                var where = Position(removal.Cell);
                if (fx)
                {
                    var color = power ? Gold : GemColors[Mathf.Clamp(removal.Color, 0, GemColors.Length - 1)];
                    fx.Flash(where, cell * (power ? 2.2f : 1.5f), new Color(1f, 1f, 1f, 0.75f));
                    fx.Burst(where, color, cell, power ? 12 : 7);
                }
                Animate(0.16f, k =>
                {
                    if (!rect) return;
                    // Swell before vanishing: a piece that only shrinks reads as a bug, not a hit.
                    float left = 1f - k;
                    float scale = left > 0.75f ? Mathf.Lerp(1f, 1.25f, (1f - left) / 0.25f) : left / 0.75f * 1.25f;
                    rect.localScale = new Vector3(scale, scale, 1f);
                }, () => { if (rect) Destroy(rect.gameObject); });
            });
        }

        static string PowerName(FinikMatch3Special special) => special switch
        {
            FinikMatch3Special.RocketH or FinikMatch3Special.RocketV => "Ракета!",
            FinikMatch3Special.Plane => "Самолётик!",
            FinikMatch3Special.Bomb => "Бомба!",
            FinikMatch3Special.Rainbow => "Диско-шар!",
            _ => string.Empty
        };

        void ScheduleCreated(FinikMatch3Created created)
        {
            After(created.Delay, () =>
            {
                if (model == null) return;
                var piece = Spawn(new FinikMatch3Piece { Id = created.Id, Color = -1, Special = created.Special }, created.Cell);
                var rect = piece.Rect;
                var where = Position(created.Cell);
                if (fx)
                {
                    fx.Flash(where, cell * 2.4f, new Color(1f, 0.92f, 0.55f, 0.9f));
                    fx.Burst(where, Gold, cell, 10);
                    fx.Say(where + Vector2.up * cell * 0.6f, PowerName(created.Special), Gold, cell * 0.5f);
                }
                rect.localScale = Vector3.zero;
                Animate(0.3f, k =>
                {
                    if (!rect) return;
                    float s = FinikUiMotion.EaseOutBack(k, 2.4f);
                    rect.localScale = new Vector3(s, s, 1f);
                });
            });
        }

        void ScheduleTransform(FinikMatch3Transform change)
        {
            After(change.Delay, () =>
            {
                if (!pieces.TryGetValue(change.Id, out var piece) || !piece.Rect) return;
                piece.Special = change.Special;
                piece.Color = -1;
                if (art) art.Apply(piece.Image, SpriteFor(change.Special, -1));
                Dress(piece);
                var rect = piece.Rect;
                if (fx) fx.Flash(Position(change.Cell), cell * 1.6f, new Color(1f, 0.95f, 0.7f, 0.8f));
                Animate(0.22f, k =>
                {
                    if (!rect) return;
                    float s = 1f + Mathf.Sin(k * Mathf.PI) * 0.3f;
                    rect.localScale = new Vector3(s, s, 1f);
                });
            });
        }

        void ScheduleObstacle(FinikMatch3ObstacleHit hit)
        {
            After(hit.Delay, () =>
            {
                var images = hit.Crate ? crates : ice;
                if (images == null) return;
                var image = images[hit.Cell];
                if (!image) return;
                var where = Position(hit.Cell);
                if (fx) fx.Burst(where, hit.Crate ? Wood : Frost, cell, hit.LayersLeft > 0 ? 6 : 12);
                var rect = image.rectTransform;
                if (hit.LayersLeft > 0)
                {
                    if (art) art.Apply(image, hit.Crate ? CrateSprite : IceSprite);
                    Animate(0.18f, k =>
                    {
                        if (!rect) return;
                        float s = 1f - Mathf.Sin(k * Mathf.PI) * 0.12f;
                        rect.localScale = new Vector3(s, s, 1f);
                    });
                    return;
                }
                images[hit.Cell] = null;
                if (fx) fx.Flash(where, cell * 1.8f, hit.Crate ? new Color(1f, 0.85f, 0.6f, 0.8f) : new Color(0.85f, 0.97f, 1f, 0.85f));
                int goal = GoalFor(hit.Crate ? FinikMatch3GoalKind.Crate : FinikMatch3GoalKind.Ice, -1);
                if (goal >= 0)
                {
                    Claim(goal);
                    Launch(hit.Crate ? CrateSprite : IceSprite, where, goal, cell);
                }
                var start = image.color;
                Animate(0.2f, k =>
                {
                    if (!rect) return;
                    float s = Mathf.Lerp(1f, 1.3f, k);
                    rect.localScale = new Vector3(s, s, 1f);
                    image.color = new Color(start.r, start.g, start.b, start.a * (1f - k));
                }, () => { if (rect) Destroy(rect.gameObject); });
            });
        }

        void ScheduleEffect(FinikMatch3Effect effect)
        {
            switch (effect.Kind)
            {
                case FinikMatch3EffectKind.Rocket:
                    After(effect.Delay, () => FlyRocket(effect));
                    break;
                case FinikMatch3EffectKind.Plane:
                    After(effect.Delay, () => FlyPlane(effect));
                    break;
                case FinikMatch3EffectKind.Beam:
                    After(effect.Delay, () => Beam(effect));
                    break;
                case FinikMatch3EffectKind.Bomb:
                    After(effect.Delay, () =>
                    {
                        if (!fx) return;
                        var where = Position(effect.From);
                        fx.Flash(where, cell * (effect.Radius * 2f + 1.5f), new Color(1f, 0.65f, 0.25f, 0.9f));
                        fx.Flash(where, cell * (effect.Radius * 1.2f + 1f), new Color(1f, 1f, 0.85f, 0.9f));
                        fx.Burst(where, new Color(1f, 0.55f, 0.2f), cell * 1.4f, 16);
                        fx.Shake(Field, 14f + effect.Radius * 4f);
                    });
                    break;
            }
        }

        Image Flyer(string sprite, float size)
        {
            var image = NewImage("Flyer", overlay, sprite);
            image.rectTransform.sizeDelta = new Vector2(size, size);
            return image;
        }

        /// <summary>One half of a rocket's flight: out of its cell and off the end of the row.</summary>
        void FlyRocket(FinikMatch3Effect effect)
        {
            if (!overlay) return;
            var from = Position(effect.From);
            var to = Position(effect.To);
            var direction = (to - from).sqrMagnitude > 1f ? (to - from).normalized : Vector2.right;
            // Past the last cell and out: a rocket that stops at the edge looks like it hit a wall.
            to += direction * step * 0.9f;
            var image = Flyer(Pick(RocketSprite, "booster_hammer"), cell * 0.95f);
            var rect = image.rectTransform;
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            rect.anchoredPosition = from;
            float seconds = Mathf.Max(0.12f, effect.Duration + 0.06f);
            int frame = 0;
            Animate(seconds, k =>
            {
                if (!rect) return;
                var at = Vector2.Lerp(from, to, k);
                rect.anchoredPosition = at;
                if (fx && frame++ % 2 == 0) fx.Burst(at - direction * cell * 0.4f, new Color(1f, 0.7f, 0.25f), cell * 0.35f, 1);
                if (k > 0.8f) image.color = new Color(1f, 1f, 1f, (1f - k) / 0.2f);
            }, () => { if (rect) Destroy(rect.gameObject); });
        }

        /// <summary>A paper plane on a curve to what it goes for, turning along its path.</summary>
        void FlyPlane(FinikMatch3Effect effect)
        {
            if (!overlay) return;
            var from = Position(effect.From);
            var to = Position(effect.To);
            var image = Flyer(Pick(PlaneSprite, "booster_reveal"), cell * 1.05f);
            var rect = image.rectTransform;
            var chord = to - from;
            var side = new Vector2(-chord.y, chord.x).normalized * (step * 1.6f) * (Random.value < 0.5f ? 1f : -1f);
            if (chord.sqrMagnitude < 1f) side = Vector2.up * step * 1.6f;
            Vector2 Point(float k) => Vector2.Lerp(from, to, k) + side * Mathf.Sin(k * Mathf.PI);
            rect.anchoredPosition = from;
            Animate(Mathf.Max(0.2f, effect.Duration), k =>
            {
                if (!rect) return;
                float e = Mathf.SmoothStep(0f, 1f, k);
                var at = Point(e);
                var ahead = Point(Mathf.Min(1f, e + 0.02f)) - at;
                rect.anchoredPosition = at;
                // The art points to the upper right; turn that corner along the flight.
                if (ahead.sqrMagnitude > 0.01f)
                    rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(ahead.y, ahead.x) * Mathf.Rad2Deg - 45f);
                float s = 1f + Mathf.Sin(k * Mathf.PI) * 0.35f;
                rect.localScale = new Vector3(s, s, 1f);
            }, () =>
            {
                if (fx) fx.Flash(to, cell * 1.8f, new Color(0.8f, 0.95f, 1f, 0.9f));
                if (rect) Destroy(rect.gameObject);
            });
        }

        /// <summary>A disco ball's ray of light from the ball to one gem of its colour.</summary>
        void Beam(FinikMatch3Effect effect)
        {
            if (!overlay) return;
            var from = Position(effect.From);
            var to = Position(effect.To);
            var color = effect.Color >= 0 ? GemColors[effect.Color % GemColors.Length] : Gold;
            var image = NewImage("Beam", overlay, null);
            image.sprite = null;
            image.color = new Color(Mathf.Lerp(color.r, 1f, 0.4f), Mathf.Lerp(color.g, 1f, 0.4f), Mathf.Lerp(color.b, 1f, 0.4f), 0.9f);
            var rect = image.rectTransform;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = from;
            var chord = to - from;
            float length = chord.magnitude;
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(chord.y, chord.x) * Mathf.Rad2Deg);
            float thickness = cell * 0.12f;
            float grow = Mathf.Max(0.06f, effect.Duration);
            Animate(grow + 0.22f, k =>
            {
                if (!rect) return;
                float t = k * (grow + 0.22f);
                float reach = Mathf.Clamp01(t / grow);
                rect.sizeDelta = new Vector2(length * reach, thickness);
                float fade = t <= grow ? 1f : 1f - (t - grow) / 0.22f;
                var c = image.color;
                image.color = new Color(c.r, c.g, c.b, 0.9f * fade);
            }, () => { if (rect) Destroy(rect.gameObject); });
            After(grow, () => { if (fx) fx.Flash(to, cell * 1.2f, new Color(1f, 1f, 1f, 0.8f)); });
        }

        // ------------------------------------------------------------------ flying into the goals

        /// <summary>
        /// The goal a cleared piece counts for, if it still wants pieces: a colour goal for its own
        /// colour first, then "any gem". -1 when nothing on the card is waiting for it.
        /// </summary>
        int GoalFor(FinikMatch3GoalKind kind, int color)
        {
            var goals = Goals;
            int any = -1;
            for (int i = 0; i < goals.Count && i < launched.Length; i++)
            {
                if (launched[i] >= goals[i].Count) continue;
                if (kind == FinikMatch3GoalKind.Color)
                {
                    if (goals[i].Kind == FinikMatch3GoalKind.Color && goals[i].Color == color) return i;
                    if (goals[i].Kind == FinikMatch3GoalKind.Gems && any < 0) any = i;
                }
                else if (goals[i].Kind == kind) return i;
            }
            return any;
        }

        /// <summary>Books a piece for its goal at once, so two clears in one blast never overfill it.</summary>
        void Claim(int goal) => launched[goal]++;

        /// <summary>
        /// A copy of the piece hops up out of its cell and flies on a curve into its picture on the
        /// goal card, shrinking to the picture's size; the count drops when it lands, with a little pop.
        /// </summary>
        void Launch(string sprite, Vector2 from, int goal, float size)
        {
            var anchor = GoalAnchor?.Invoke(goal);
            if (!overlay || !anchor || !anchor.gameObject.activeInHierarchy)
            {
                Land(goal, null);
                return;
            }
            var image = NewImage("ToGoal", overlay, sprite);
            var rect = image.rectTransform;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = from;
            // Where the picture is, and how big, in this layer's units.
            Vector2 Target() => anchor ? (Vector2)overlay.InverseTransformPoint(anchor.TransformPoint(anchor.rect.center)) : from;
            float scaleRatio = anchor.lossyScale.x / Mathf.Max(0.0001f, overlay.lossyScale.x);
            float endSize = Mathf.Min(anchor.rect.width, anchor.rect.height) * scaleRatio;
            float side = (Random.value - 0.5f) * step * 2f;
            float delay = Random.Range(0f, 0.12f);
            const float hop = 0.14f, fly = 0.5f;
            var start = from + Vector2.up * step * 0.35f;
            flights.Add(new Tween
            {
                Delay = delay,
                Duration = hop + fly,
                Step = k =>
                {
                    if (!rect) return;
                    float t = k * (hop + fly);
                    if (t < hop)
                    {
                        // A little jump first, then away.
                        float h = t / hop;
                        rect.anchoredPosition = Vector2.Lerp(from, start, Mathf.Sin(h * Mathf.PI * 0.5f));
                        float grow = 1f + 0.2f * h;
                        rect.localScale = new Vector3(grow, grow, 1f);
                        return;
                    }
                    float f = (t - hop) / fly;
                    float e = f * f * (3f - 2f * f);
                    var end = Target();
                    var control = (start + end) / 2f + new Vector2(side, step * 1.2f);
                    rect.anchoredPosition = (1 - e) * (1 - e) * start + 2 * (1 - e) * e * control + e * e * end;
                    float s = Mathf.Lerp(size * 1.2f, endSize, e) / Mathf.Max(1f, size);
                    rect.localScale = new Vector3(s, s, 1f);
                },
                Done = () =>
                {
                    if (rect) Destroy(rect.gameObject);
                    Land(goal, anchor);
                }
            });
        }

        void Land(int goal, RectTransform anchor)
        {
            if (goal < landed.Length) landed[goal] = Mathf.Min(launched[goal], landed[goal] + 1);
            RaiseChanged();
            if (!anchor) return;
            if (fx) fx.Burst((Vector2)fx.transform.InverseTransformPoint(anchor.TransformPoint(anchor.rect.center)), Gold, cell * 0.6f, 4);
            flights.Add(new Tween
            {
                Duration = 0.18f,
                Step = k =>
                {
                    if (!anchor) return;
                    float s = 1f + Mathf.Sin(k * Mathf.PI) * 0.25f;
                    anchor.localScale = new Vector3(s, s, 1f);
                },
                Done = () => { if (anchor) anchor.localScale = Vector3.one; }
            });
        }

        // ------------------------------------------------------------------ playback: gravity

        void PlayFall(FinikMatch3Fall fall)
        {
            foreach (var move in fall.Moves)
            {
                if (move.Path == null || move.Path.Length == 0) continue;
                Piece piece;
                var points = new List<Vector2>();
                if (move.SpawnRank >= 0)
                {
                    // A new gem starts above its column, the later ones higher, so they come in as a stream.
                    piece = Spawn(new FinikMatch3Piece { Id = move.Id, Color = move.Color, Special = move.Special }, move.Path[0]);
                    points.Add(PieceAt(move.Path[0]) + Vector2.up * step * (move.SpawnRank + 1));
                }
                else if (!pieces.TryGetValue(move.Id, out piece) || !piece.Rect) continue;
                else points.Add(piece.Rect.anchoredPosition);
                for (int i = move.SpawnRank >= 0 ? 0 : 1; i < move.Path.Length; i++) points.Add(PieceAt(move.Path[i]));
                piece.Cell = move.Path[move.Path.Length - 1];

                float length = 0f;
                for (int i = 1; i < points.Count; i++) length += Vector2.Distance(points[i - 1], points[i]);
                if (length < 0.5f)
                {
                    piece.Rect.anchoredPosition = points[points.Count - 1];
                    continue;
                }
                var rect = piece.Rect;
                rect.anchoredPosition = points[0];
                float seconds = 0.08f + 0.1f * Mathf.Sqrt(length / Mathf.Max(1f, step));
                Animate(seconds, k =>
                {
                    if (!rect) return;
                    // Falling speeds up, as things do.
                    rect.anchoredPosition = AlongPath(points, length, k * k);
                }, () =>
                {
                    if (!rect) return;
                    Animate(0.12f, k =>
                    {
                        if (!rect) return;
                        // A little squash on landing.
                        float squash = Mathf.Sin(k * Mathf.PI) * 0.1f;
                        rect.localScale = new Vector3(1f + squash, 1f - squash, 1f);
                    }, () => { if (rect) rect.localScale = Vector3.one; });
                });
            }
        }

        static Vector2 AlongPath(List<Vector2> points, float length, float k)
        {
            float target = Mathf.Clamp01(k) * length;
            for (int i = 1; i < points.Count; i++)
            {
                float segment = Vector2.Distance(points[i - 1], points[i]);
                if (target <= segment || i == points.Count - 1)
                    return Vector2.Lerp(points[i - 1], points[i], segment < 0.001f ? 1f : Mathf.Clamp01(target / segment));
                target -= segment;
            }
            return points[points.Count - 1];
        }

        // ------------------------------------------------------------------ playback: shuffle

        /// <summary>
        /// The pieces swirl into a whirlpool in the middle of the board and fly back out to their new
        /// places: every piece is seen travelling, so a shuffle reads as a shuffle and not as a glitch.
        /// </summary>
        void PlayShuffle(FinikMatch3Shuffle shuffle)
        {
            const float seconds = 0.95f;
            var center = GridCenter(origin, step, columns, rows) - (pieceLayer ? pieceLayer.anchoredPosition : Vector2.zero);
            if (fx)
            {
                if (shuffle.Automatic)
                    fx.Say(Vector2.zero, "Ходов нет — перемешиваю!", new Color(0.75f, 0.9f, 1f), cell * 0.45f);
                fx.Flash(Vector2.zero, cell * 4f, new Color(0.75f, 0.85f, 1f, 0.6f));
                fx.Shake(Field, 8f);
            }
            After(seconds * 0.5f, () =>
            {
                if (fx) fx.Burst(Vector2.zero, new Color(0.8f, 0.9f, 1f), cell * 1.6f, 18);
            });

            foreach (var move in shuffle.Pieces)
            {
                if (!pieces.TryGetValue(move.Id, out var piece) || !piece.Rect) continue;
                piece.Cell = move.To;
                if (move.NewColor >= 0) piece.Color = move.NewColor;
                var rect = piece.Rect;
                var image = piece.Image;
                var start = rect.anchoredPosition - center;
                var end = PieceAt(move.To) - center;
                float r0 = start.magnitude, r1 = end.magnitude;
                float a0 = Mathf.Atan2(start.y, start.x), a1 = Mathf.Atan2(end.y, end.x);
                // Everyone turns the same way, once round and to their new spot: one whirlpool, not a swarm.
                float turn = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad + Mathf.PI * 2f;
                float baseTurn = piece.Special == FinikMatch3Special.RocketV ? 90f : 0f;
                bool repaint = move.NewColor >= 0;
                int color = move.NewColor;
                bool repainted = false;
                float delay = Random.Range(0f, 0.1f);
                Animate(seconds, k =>
                {
                    if (!rect) return;
                    float e = k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f;
                    float angle = a0 + turn * e;
                    float radius = Mathf.Lerp(r0, r1, e) * (1f - 0.55f * Mathf.Sin(e * Mathf.PI));
                    rect.anchoredPosition = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    float s = 1f - 0.3f * Mathf.Sin(e * Mathf.PI);
                    rect.localScale = new Vector3(s, s, 1f);
                    rect.localRotation = Quaternion.Euler(0f, 0f, baseTurn + 360f * e);
                    if (repaint && !repainted && e >= 0.5f && art)
                    {
                        repainted = true;
                        art.Apply(image, SpriteFor(FinikMatch3Special.None, color));
                    }
                }, () =>
                {
                    if (!rect) return;
                    rect.localScale = Vector3.one;
                    rect.localRotation = Quaternion.Euler(0f, 0f, baseTurn);
                    rect.anchoredPosition = PieceAt(piece.Cell);
                }, delay);
            }
        }

        // ------------------------------------------------------------------ idle hint

        void StopHint()
        {
            idle = 0f;
            if (hintAge >= 0f)
            {
                foreach (var piece in new[] { hintA, hintB })
                    if (piece != null && piece.Rect && pieces.ContainsKey(piece.Id)) piece.Rect.anchoredPosition = PieceAt(piece.Cell);
            }
            hintAge = -1f;
            hintA = hintB = null;
        }

        /// <summary>After a few quiet seconds two gems that would match lean towards each other, or a power wiggles.</summary>
        void UpdateHint(float dt)
        {
            if (!Running || busy || model == null || pressCell >= 0 || tweens.Count > 0)
            {
                if (hintAge >= 0f) StopHint();
                idle = 0f;
                return;
            }
            if (hintAge < 0f)
            {
                idle += dt;
                if (idle < HintAfter) return;
                if (model.FindSwap(out int a, out int b))
                {
                    hintA = Find(a);
                    hintB = Find(b);
                }
                else
                {
                    for (int i = 0; i < columns * rows && hintA == null; i++)
                        if (model.IsMovable(i) && model.PieceAt(i).Special != FinikMatch3Special.None) hintA = Find(i);
                    hintB = null;
                }
                if (hintA == null) return;
                hintAge = 0f;
            }

            hintAge += dt;
            float wave = Mathf.Sin(hintAge * Mathf.PI * 2f * 1.6f) * Mathf.Sin(Mathf.Clamp01(hintAge / HintSeconds) * Mathf.PI);
            if (hintA != null && hintA.Rect)
            {
                var home = PieceAt(hintA.Cell);
                var toward = hintB != null ? (PieceAt(hintB.Cell) - home).normalized : Vector2.up;
                hintA.Rect.anchoredPosition = home + toward * (wave * step * 0.12f);
                if (hintB != null && hintB.Rect)
                    hintB.Rect.anchoredPosition = PieceAt(hintB.Cell) - toward * (wave * step * 0.12f);
            }
            if (hintAge < HintSeconds) return;
            StopHint();
            // The next nudge comes sooner: the child is clearly looking for a move.
            idle = HintAfter * 0.4f;
        }
    }
}
