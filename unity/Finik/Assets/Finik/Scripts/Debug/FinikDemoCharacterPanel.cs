using System.Collections;
using Finik.Accessories;
using Finik.Core;
using Finik.Navigation;
using Finik.UI;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Finik.DebugTools
{
    [DisallowMultipleComponent]
    public sealed class FinikDemoCharacterPanel : MonoBehaviour
    {
        [SerializeField] FinikCharacterSwitcher switcher;
        [SerializeField] FinikAccessoryRig accessoryRig;
        FinikOnboardingFlow onboarding;
        GameObject demoButtonRoot;
        Button demoButton;
        RectTransform demoButtonRect;
        RectTransform safeArea;
        RectTransform goalCard;
        RectTransform taskCard;
        RectTransform dock;
        Canvas hudCanvas;
        [Tooltip("Drawer state. In demo mode the collapsed «Демо» tab always stays visible.")]
        [SerializeField] bool visible;

        string selectedPetId = "fox";
        int selectedStage = 1;
        int selectedDay = 1;
        Coroutine demoRoutine;
        int demoQuestStep;
        float drawerProgress;
        Rect lastPanelRect;
        static FinikDemoCharacterPanel activeInstance;

        bool CompactPortrait => Screen.height > Screen.width && Screen.width <= 1000;
        float DemoRowHeight => CompactPortrait ? 78f : 78f;
        float DemoButtonHeight => CompactPortrait ? 68f : 68f;
        float DemoAdjustButtonHeight => CompactPortrait ? 64f : 62f;
        float DemoStateLabelHeight => CompactPortrait ? 40f : 38f;

        GUIStyle panelStyle;
        GUIStyle titleStyle;
        GUIStyle currentStyle;
        GUIStyle sectionStyle;
        GUIStyle buttonStyle;
        GUIStyle activeButtonStyle;
        GUIStyle unavailableStyle;
        GUIStyle hintStyle;

        Texture2D panelTexture;
        Texture2D buttonTexture;
        Texture2D activeTexture;
        Texture2D disabledTexture;
        Texture2D shadowTexture;

        void Awake()
        {
            activeInstance = this;
            if (!switcher) switcher = GetComponent<FinikCharacterSwitcher>();
            if (!accessoryRig) accessoryRig = GetComponent<FinikAccessoryRig>();
            onboarding = FindFirstObjectByType<FinikOnboardingFlow>(FindObjectsInactive.Include);
            drawerProgress = visible ? 1f : 0f;
        }

        void Start()
        {
            if (!switcher || !IsDemoProfile(out var profile)) return;
            selectedPetId = profile.petId;
            selectedStage = switcher.ActiveStage > 0 ? switcher.ActiveStage : 1;
            selectedDay = FinikGame.DemoCampaignDay;
            if (!switcher.HasCharacter(selectedPetId, selectedStage))
                SelectFirstAvailableStage(selectedPetId);
            EnableAccessoriesForDemo();
            CreateDemoButton();
        }

        void Update()
        {
            bool canShowDemo = IsDemoProfile(out _) && !(onboarding && onboarding.gameObject.activeInHierarchy);
            // HUD_Home is activated after this component on some scene/device paths. Retry creation
            // until the HUD hierarchy exists instead of losing the button forever after Start().
            if (canShowDemo && !demoButtonRoot) CreateDemoButton();
            if (demoButtonRoot && demoButtonRoot.activeSelf != canShowDemo) demoButtonRoot.SetActive(canShowDemo);
            if (!canShowDemo) return;
            PositionDemoButton();
            drawerProgress = Mathf.MoveTowards(drawerProgress, visible ? 1f : 0f, Time.unscaledDeltaTime * 5f);
#if UNITY_EDITOR
            if (Keyboard.current?.f9Key.wasPressedThisFrame == true) visible = !visible;
#endif
        }

        void OnDestroy()
        {
            if (activeInstance == this) activeInstance = null;
            if (demoButton) demoButton.onClick.RemoveListener(ToggleDrawer);
            if (demoButtonRoot) Destroy(demoButtonRoot);
            DestroyTexture(panelTexture);
            DestroyTexture(buttonTexture);
            DestroyTexture(activeTexture);
            DestroyTexture(disabledTexture);
            DestroyTexture(shadowTexture);

            panelTexture = null;
            buttonTexture = null;
            activeTexture = null;
            disabledTexture = null;
            shadowTexture = null;

            panelStyle = null;
            titleStyle = null;
            currentStyle = null;
            sectionStyle = null;
            buttonStyle = null;
            activeButtonStyle = null;
            unavailableStyle = null;
            hintStyle = null;
        }

        void CreateDemoButton()
        {
            var hudView = FindFirstObjectByType<FinikHudView>(FindObjectsInactive.Include);
            var hud = hudView ? hudView.gameObject : GameObject.Find("HUD_Home");
            safeArea = hud ? hud.transform.Find("SafeArea") as RectTransform : null;
            var settingsBody = safeArea ? safeArea.Find("Settings/Body") as RectTransform : null;
            if (!safeArea) return;
            hudCanvas = hud.GetComponent<Canvas>();
            goalCard = safeArea.Find("GoalCard") as RectTransform;
            taskCard = safeArea.Find("TaskCard") as RectTransform;
            dock = safeArea.Find("Dock") as RectTransform;

            var root = new GameObject("DemoButton", typeof(RectTransform));
            demoButtonRoot = root;
            demoButtonRect = root.GetComponent<RectTransform>();
            demoButtonRect.SetParent(safeArea, false);
            demoButtonRect.anchorMin = demoButtonRect.anchorMax = new Vector2(0f, 0.5f);
            demoButtonRect.pivot = new Vector2(0.5f, 0.5f);
            demoButtonRect.sizeDelta = CompactPortrait ? new Vector2(156f, 96f) : new Vector2(108f, 108f);
            PositionDemoButton();
            root.AddComponent<FinikHudObstacle>();

            GameObject body;
            if (settingsBody)
            {
                body = Instantiate(settingsBody.gameObject, demoButtonRect, false);
                body.name = "Body";
                var icon = body.transform.Find("Icon");
                if (icon) icon.gameObject.SetActive(false);
            }
            else
            {
                body = new GameObject("Body", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Shadow));
                body.transform.SetParent(demoButtonRect, false);
                var image = body.GetComponent<Image>();
                image.color = new Color(0.96f, 0.93f, 1f, 0.98f);
                var shadow = body.GetComponent<Shadow>();
                shadow.effectColor = new Color(0.16f, 0.08f, 0.28f, 0.18f);
                shadow.effectDistance = new Vector2(0f, -5f);
            }

            var bodyRect = body.GetComponent<RectTransform>();
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.pivot = new Vector2(0.5f, 0.5f);
            bodyRect.offsetMin = Vector2.zero;
            bodyRect.offsetMax = Vector2.zero;

            demoButton = body.GetComponent<Button>();
            if (!demoButton) demoButton = body.GetComponentInChildren<Button>(true);
            if (!demoButton) demoButton = body.AddComponent<Button>();
            demoButton.onClick.RemoveAllListeners();
            demoButton.onClick.AddListener(ToggleDrawer);
            var tapGraphic = demoButton.targetGraphic;
            if (tapGraphic && !tapGraphic.GetComponent<FinikTapTarget>())
                tapGraphic.gameObject.AddComponent<FinikTapTarget>();
            root.transform.SetAsLastSibling();
            root.SetActive(true);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(body.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(6f, 8f);
            labelRect.offsetMax = new Vector2(-6f, -8f);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            var sample = safeArea.GetComponentInChildren<TMP_Text>(true);
            if (sample) label.font = sample.font;
            label.text = "\u0414\u0435\u043c\u043e";
            label.fontSize = CompactPortrait ? 34f : 24f;
            label.enableAutoSizing = true;
            label.fontSizeMin = CompactPortrait ? 30f : 24f;
            label.fontSizeMax = CompactPortrait ? 36f : 28f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.28f, 0.13f, 0.62f, 1f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        void PositionDemoButton()
        {
            if (!demoButtonRect || !safeArea) return;

            demoButtonRect.sizeDelta = CompactPortrait ? new Vector2(156f, 96f) : new Vector2(108f, 108f);
            var demoLabel = demoButtonRect.GetComponentInChildren<TMP_Text>(true);
            if (demoLabel)
            {
                demoLabel.fontSizeMin = CompactPortrait ? 30f : 24f;
                demoLabel.fontSizeMax = CompactPortrait ? 36f : 28f;
            }

            bool portrait = safeArea.rect.height > safeArea.rect.width;
            float upper;
            float lower;
            if (portrait && goalCard && taskCard)
            {
                upper = LocalEdgeY(goalCard, top: false);
                lower = LocalEdgeY(taskCard, top: true);
            }
            else if (taskCard && dock)
            {
                upper = LocalEdgeY(taskCard, top: false);
                lower = LocalEdgeY(dock, top: true);
            }
            else
            {
                demoButtonRect.anchoredPosition = new Vector2(CompactPortrait ? 92f : 72f, -112f);
                return;
            }

            float centerY = (upper + lower) * 0.5f;
            float halfHeight = demoButtonRect.sizeDelta.y * 0.5f;
            centerY = Mathf.Clamp(centerY, safeArea.rect.yMin + halfHeight + 12f, safeArea.rect.yMax - halfHeight - 12f);
            demoButtonRect.anchoredPosition = new Vector2(CompactPortrait ? 92f : 72f, centerY);
        }

        float LocalEdgeY(RectTransform target, bool top)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            float a = safeArea.InverseTransformPoint(corners[0]).y;
            float b = safeArea.InverseTransformPoint(corners[1]).y;
            float c = safeArea.InverseTransformPoint(corners[2]).y;
            float d = safeArea.InverseTransformPoint(corners[3]).y;
            return top ? Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d)) : Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d));
        }

        float DemoButtonRightScreen()
        {
            if (!demoButtonRect) return Mathf.Max(138f, Screen.width * 0.07f);
            var corners = new Vector3[4];
            demoButtonRect.GetWorldCorners(corners);
            Camera camera = hudCanvas && hudCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? hudCanvas.worldCamera : null;
            float right = 0f;
            for (int i = 0; i < corners.Length; i++)
                right = Mathf.Max(right, RectTransformUtility.WorldToScreenPoint(camera, corners[i]).x);
            return right + 14f;
        }

        void ToggleDrawer()
        {
            visible = !visible;
            FinikAudioManager.Instance.PlayUiTap();
        }

        /// <summary>Blocks world navigation under the IMGUI demo drawer.</summary>
        public static bool BlocksWorldPointer(Vector2 screenPosition)
        {
            var panel = activeInstance;
            if (!panel || panel.drawerProgress <= 0.01f || !panel.IsDemoVisible()) return false;
            // InputSystem screen coordinates start at bottom-left; IMGUI Rect starts at top-left.
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return panel.lastPanelRect.Contains(guiPoint);
        }

        bool IsDemoVisible() =>
            IsDemoProfile(out _) && !(onboarding && onboarding.gameObject.activeInHierarchy);

        void OnGUI()
        {
            if (!switcher || !IsDemoProfile(out _) || (onboarding && onboarding.gameObject.activeInHierarchy)) return;
            EnsureStyles();
            if (!visible && drawerProgress <= 0.001f) return;

            bool landscape = Screen.width >= Screen.height;
            float openX = DemoButtonRightScreen();
            float panelWidth = CompactPortrait
                ? Mathf.Max(280f, Screen.width - 36f)
                : landscape
                    ? Mathf.Clamp(Screen.width * 0.62f, 980f, 1380f)
                    : Mathf.Clamp(Screen.width - openX - 22f, 760f, 1100f);
            panelWidth = Mathf.Min(panelWidth, Screen.width - 24f);
            // The demo drawer is an operator panel and may cover the game HUD.
            float contentHeight = CompactPortrait ? 720f : 740f;
            float panelHeight = Mathf.Min(contentHeight, Screen.height - 24f);
            float y = (Screen.height - panelHeight) * 0.5f;
            float eased = Mathf.SmoothStep(0f, 1f, drawerProgress);
            float openPanelX = CompactPortrait ? 18f : Mathf.Min(openX, Screen.width - panelWidth - 12f);
            float x = Mathf.Lerp(-panelWidth - 30f, openPanelX, eased);
            lastPanelRect = new Rect(x, y, panelWidth, panelHeight);

            if (shadowTexture)
                GUI.DrawTexture(new Rect(x + 5f, y + 7f, panelWidth, panelHeight), shadowTexture, ScaleMode.StretchToFill);
            GUI.Box(new Rect(x, y, panelWidth, panelHeight), GUIContent.none, panelStyle);

            float padX = CompactPortrait ? 24f : 28f;
            float padY = CompactPortrait ? 20f : 24f;
            float headerHeight = 68f;
            GUI.Label(new Rect(x + padX, y + padY, panelWidth - padX * 2f - 80f, headerHeight), "Демо-режим", titleStyle);
            if (GUI.Button(new Rect(x + panelWidth - padX - 68f, y + padY, 68f, 62f), "×", buttonStyle))
                ToggleDrawer();
            GUILayout.BeginArea(new Rect(x + padX, y + padY + headerHeight, panelWidth - padX * 2f, panelHeight - padY * 2f - headerHeight));

            DrawSelectorRow("\u041f\u0435\u0440\u0441\u043e\u043d\u0430\u0436", () =>
            {
                DrawPetButton("fox", "\u0424\u0438\u043d\u0438\u043a");
                GUILayout.Space(5f);
                DrawPetButton("cat", "\u041a\u043e\u0448\u043a\u0430");
                GUILayout.Space(5f);
                DrawPetButton("raccoon", "\u0420\u0438\u043a");
            });
            GUILayout.Space(8f);

            DrawSelectorRow("\u0421\u0442\u0430\u0434\u0438\u044f", () =>
            {
                DrawStageButton(1);
                GUILayout.Space(5f);
                DrawStageButton(2);
                GUILayout.Space(5f);
                DrawStageButton(3);
            });
            GUILayout.Space(8f);

            DrawSelectorRow("\u0414\u0435\u043d\u044c", () =>
            {
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && demoRoutine == null;
                if (GUILayout.Button("−", buttonStyle, GUILayout.Height(DemoButtonHeight))) ChangeDay(-1);
                GUILayout.Label(selectedDay.ToString(), currentStyle, GUILayout.Width(CompactPortrait ? 70f : 82f), GUILayout.Height(DemoButtonHeight));
                if (GUILayout.Button("+", buttonStyle, GUILayout.Height(DemoButtonHeight))) ChangeDay(1);
                GUI.enabled = enabled;
            });

            var growth = FinikGame.Growth;
            int nextStageLevel = FinikGrowthEngine.NextStageLevel(growth.level);
            string stageProgress = nextStageLevel > 0
                ? $"до St{FinikGrowthEngine.StageForLevel(nextStageLevel)}: {Mathf.Max(0, FinikGrowthEngine.ThresholdForLevel(nextStageLevel) - growth.xp)} XP"
                : "St3 достигнута";
            GUILayout.Label(demoRoutine != null ? $"Квест {demoQuestStep}/3 · {FinikGame.Balance} монет · цель {FinikGame.Savings}"
                    : $"Уровень {growth.level} · опыт {growth.xp} · {stageProgress}",
                currentStyle, GUILayout.Height(CompactPortrait ? 42f : 48f));
            bool dayComplete = FinikGame.DemoDayComplete;
            bool canStart = GUI.enabled;
            GUI.enabled = canStart && demoRoutine == null;
            if (GUILayout.Button(demoRoutine != null ? "Идут задания..." : dayComplete ? "Продолжить: следующий день" : "Начать",
                    activeButtonStyle, GUILayout.Height(CompactPortrait ? 68f : 70f)))
            {
                FinikAudioManager.Instance.PlayUiTap();
                if (dayComplete) ChangeDay(1);
                demoRoutine = StartCoroutine(RunDemoDay());
            }
            GUI.enabled = canStart;

            GUILayout.Space(CompactPortrait ? 12f : 14f);
            GUILayout.Label("\u0421\u043e\u0441\u0442\u043e\u044f\u043d\u0438\u0435", sectionStyle, GUILayout.Height(CompactPortrait ? 38f : 38f));
            var needs = FinikGame.NeedsNow;
            GUILayout.BeginHorizontal();
            DrawCompactAdjuster("\u0421\u044b\u0442\u043e\u0441\u0442\u044c", $"{needs.food:0}%", () => FinikGame.AdjustDemoNeeds(-25, 0), () => FinikGame.AdjustDemoNeeds(25, 0));
            GUILayout.Space(6f);
            DrawCompactAdjuster("\u041d\u0430\u0441\u0442\u0440.", $"{needs.mood:0}%", () => FinikGame.AdjustDemoNeeds(0, -25), () => FinikGame.AdjustDemoNeeds(0, 25));
            GUILayout.Space(6f);
            DrawCompactAdjuster("\u041c\u043e\u043d\u0435\u0442\u044b", FinikGame.Balance.ToString(), () => FinikGame.AdjustDemoCoins(-50), () => FinikGame.AdjustDemoCoins(50));
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        void DrawSelectorRow(string label, System.Action drawButtons)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(DemoRowHeight));
            GUILayout.Label(label, sectionStyle, GUILayout.Width(CompactPortrait ? 132f : 190f), GUILayout.Height(DemoRowHeight));
            drawButtons?.Invoke();
            GUILayout.EndHorizontal();
        }

        void ChangeDay(int offset)
        {
            if (demoRoutine != null) return;
            FinikAudioManager.Instance.PlayUiTap();
            selectedDay = Mathf.Clamp(selectedDay + offset, 1, 100000);
            FinikGame.StartDemoCampaignDay(selectedDay);
        }

        IEnumerator RunDemoDay()
        {
            demoQuestStep = 0;
            while (!FinikGame.DemoDayComplete && demoQuestStep < 3)
            {
                demoQuestStep++;
                if (!FinikGame.CompleteDemoDay(maxQuests: 1)) break;
                selectedStage = FinikGrowthEngine.StageForLevel(FinikGame.Growth.level);
                if (switcher.HasCharacter(selectedPetId, selectedStage))
                    switcher.Show(selectedPetId, selectedStage);
                if (!FinikGame.DemoDayComplete)
                    yield return new WaitForSecondsRealtime(1.2f);
            }
            yield return null;
            demoRoutine = null;
        }

        void DrawCompactAdjuster(string label, string value, System.Action minus, System.Action plus)
        {
            GUILayout.BeginVertical();
            GUILayout.Label($"{label} {value}", currentStyle, GUILayout.Height(DemoStateLabelHeight));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("\u2212", buttonStyle, GUILayout.Height(DemoAdjustButtonHeight)))
            {
                FinikAudioManager.Instance.PlayUiTap();
                minus?.Invoke();
            }
            GUILayout.Space(3f);
            if (GUILayout.Button("+", activeButtonStyle, GUILayout.Height(DemoAdjustButtonHeight)))
            {
                FinikAudioManager.Instance.PlayUiTap();
                plus?.Invoke();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        void DrawPetButton(string id, string label)
        {
            var active = selectedPetId == id;
            if (GUILayout.Button(label, active ? activeButtonStyle : buttonStyle, GUILayout.Height(DemoButtonHeight)))
            {
                FinikAudioManager.Instance.PlayUiTap();
                selectedPetId = id;

                if (!switcher.HasCharacter(selectedPetId, selectedStage))
                    SelectFirstAvailableStage(selectedPetId);

                EnableAccessoriesForDemo();
                switcher.Show(selectedPetId, selectedStage);
            }
        }

        void DrawStageButton(int stage)
        {
            var available = switcher.HasCharacter(selectedPetId, stage);
            var active = selectedStage == stage && available;

            var previousEnabled = GUI.enabled;
            GUI.enabled = available;

            var style = !available
                ? unavailableStyle
                : active
                    ? activeButtonStyle
                    : buttonStyle;

            if (GUILayout.Button($"St{stage}", style, GUILayout.Height(DemoButtonHeight)) && available)
            {
                FinikAudioManager.Instance.PlayUiTap();
                selectedStage = stage;
                EnableAccessoriesForDemo();
                switcher.Show(selectedPetId, selectedStage);
            }

            GUI.enabled = previousEnabled;
        }

        static bool IsDemoProfile(out FinikProfile profile) =>
            FinikProfileStore.TryLoad(out profile) && profile.demo;

        void EnableAccessoriesForDemo()
        {
            if (!accessoryRig) return;
            accessoryRig.SetAccessoriesEnabled(true);
        }

        void SelectFirstAvailableStage(string petId)
        {
            for (var stage = 1; stage <= 3; stage++)
            {
                if (!switcher.HasCharacter(petId, stage)) continue;
                selectedStage = stage;
                return;
            }
        }

        string CurrentLabel()
        {
            var pet = selectedPetId switch
            {
                "fox" => "\u0424\u0438\u043d\u0438\u043a",
                "cat" => "\u041a\u043e\u0448\u043a\u0430",
                "raccoon" => "\u0420\u0438\u043a",
                _ => selectedPetId
            };

            return $"\u0421\u0435\u0439\u0447\u0430\u0441: {pet}  \u00b7  St{selectedStage}  \u00b7  \u0414\u0435\u043d\u044c {selectedDay}";
        }

        void EnsureStyles()
        {
            if (panelStyle != null &&
                panelTexture && buttonTexture && activeTexture &&
                disabledTexture && shadowTexture)
            {
                ApplyStyleMetrics();
                return;
            }

            DestroyTexture(panelTexture);
            DestroyTexture(buttonTexture);
            DestroyTexture(activeTexture);
            DestroyTexture(disabledTexture);
            DestroyTexture(shadowTexture);

            // Small rounded textures keep the quest screen's blue/green palette crisp in IMGUI.
            panelTexture = MakeRoundedTexture(
                new Color(.88f, .96f, 1f, .995f),
                new Color(.18f, .65f, .94f, 1f), 40, 40, 11, 2);
            buttonTexture = MakeRoundedTexture(
                new Color(.18f, .66f, .96f, 1f),
                new Color(.07f, .38f, .86f, 1f), 32, 32, 8, 2);
            activeTexture = MakeRoundedTexture(
                new Color(.27f, .83f, .18f, 1f),
                new Color(.06f, .55f, .12f, 1f), 32, 32, 8, 2);
            disabledTexture = MakeRoundedTexture(
                new Color(.86f, .89f, .94f, 1f),
                new Color(.67f, .72f, .80f, 1f), 32, 32, 8, 2);
            shadowTexture = MakeTexture(new Color(.05f, .19f, .42f, .18f));

            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = panelTexture;
            panelStyle.padding = new RectOffset(0, 0, 0, 0);
            panelStyle.border = new RectOffset(11, 11, 11, 11);

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            titleStyle.normal.textColor = new Color(.12f, .20f, .38f);

            currentStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            currentStyle.normal.textColor = new Color(.26f, .20f, .56f);

            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            sectionStyle.normal.textColor = new Color(.13f, .23f, .46f);

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(7, 7, 4, 4),
                border = new RectOffset(8, 8, 8, 8)
            };
            buttonStyle.normal.background = buttonTexture;
            buttonStyle.hover.background = buttonTexture;
            buttonStyle.active.background = activeTexture;
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.hover.textColor = Color.white;
            buttonStyle.active.textColor = Color.white;

            activeButtonStyle = new GUIStyle(buttonStyle);
            activeButtonStyle.normal.background = activeTexture;
            activeButtonStyle.hover.background = activeTexture;
            activeButtonStyle.active.background = activeTexture;
            activeButtonStyle.normal.textColor = Color.white;
            activeButtonStyle.hover.textColor = Color.white;
            activeButtonStyle.active.textColor = Color.white;

            unavailableStyle = new GUIStyle(buttonStyle);
            unavailableStyle.normal.background = disabledTexture;
            unavailableStyle.hover.background = disabledTexture;
            unavailableStyle.active.background = disabledTexture;
            unavailableStyle.normal.textColor = new Color(.42f, .47f, .55f);

            hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true
            };
            hintStyle.normal.textColor = new Color(.26f, .34f, .50f);

            activeButtonStyle.normal.textColor = Color.white;
            activeButtonStyle.hover.textColor = Color.white;
            activeButtonStyle.active.textColor = Color.white;
            ApplyStyleMetrics();
        }

        void ApplyStyleMetrics()
        {
            bool compact = CompactPortrait;
            titleStyle.fontSize = compact ? 32 : 36;
            currentStyle.fontSize = compact ? 24 : 26;
            sectionStyle.fontSize = compact ? 24 : 27;
            buttonStyle.fontSize = compact ? 25 : 29;
            activeButtonStyle.fontSize = buttonStyle.fontSize;
            unavailableStyle.fontSize = buttonStyle.fontSize;
            hintStyle.fontSize = compact ? 21 : 24;

            var pad = compact ? new RectOffset(12, 12, 7, 7) : new RectOffset(14, 14, 9, 9);
            buttonStyle.padding = pad;
            activeButtonStyle.padding = pad;
            unavailableStyle.padding = pad;
        }

        static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };

            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply();
            return texture;
        }

        static Texture2D MakeRoundedTexture(Color fill, Color border, int width, int height, int radius, int borderWidth)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[width * height];
            float outerRadius = Mathf.Max(1f, radius);
            float innerRadius = Mathf.Max(0f, outerRadius - borderWidth);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float dx = Mathf.Max(Mathf.Max(outerRadius - x - .5f, x + .5f - (width - outerRadius)), 0f);
                float dy = Mathf.Max(Mathf.Max(outerRadius - y - .5f, y + .5f - (height - outerRadius)), 0f);
                float outerDistance = Mathf.Sqrt(dx * dx + dy * dy);
                if (outerDistance > outerRadius)
                {
                    pixels[y * width + x] = Color.clear;
                    continue;
                }

                float ix = Mathf.Max(Mathf.Max(innerRadius - x - .5f, x + .5f - (width - innerRadius)), 0f);
                float iy = Mathf.Max(Mathf.Max(innerRadius - y - .5f, y + .5f - (height - innerRadius)), 0f);
                float innerDistance = Mathf.Sqrt(ix * ix + iy * iy);
                bool edge = x < borderWidth || y < borderWidth ||
                            x >= width - borderWidth || y >= height - borderWidth ||
                            innerDistance > innerRadius;
                pixels[y * width + x] = edge ? border : fill;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static void DestroyTexture(Texture2D texture)
        {
            if (!texture) return;

            if (Application.isPlaying)
                Destroy(texture);
            else
                DestroyImmediate(texture);
        }
    }
}
