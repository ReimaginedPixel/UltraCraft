using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Killcraft
{
    // The mode menu (the toggle key, F9): ULTRA-CRAFT's logo over three cards, Hybrid, Minecraft and
    // ULTRAKILL. Pick one with the mouse, 1-3, or the arrows and Enter; the toggle key or Esc closes
    // it. While it's open the game stands still (ULTRAKILL's time and Minecraft's server) and the
    // cursor is free, through ULTRAKILL's own game states, as its weapon wheel and shops do.
    internal static class ModeMenu
    {
        public const int Hybrid = 0, Minecraft = 1, Ultrakill = 2;
        private const string StateKey = "ultracraft-mode-menu";

        private static readonly string[] titles = { "HYBRID", "MINECRAFT", "ULTRAKILL" };
        private static readonly Color[] accents =
        {
            new Color(1f, 0.76f, 0.1f),
            new Color(0.37f, 0.83f, 0.37f),
            new Color(1f, 0.18f, 0.18f),
        };

        private static GameObject root;
        private static RectTransform panel;
        private static readonly Card[] cards = new Card[3];
        private static int current, highlight;
        private static float openedAt;

        private sealed class Card
        {
            public RectTransform Rect;
            public Image Border, Fill;
            public Text Badge;
            public float Scale = 1f;
        }

        public static bool IsOpen => root != null && root.activeSelf;

        private static string Description(int mode, string buildKey) => mode switch
        {
            Hybrid => $"Be V1: ULTRAKILL's guns, movement, health and style. Press {buildKey} to pull out Minecraft's hotbar and build, place TNT, swing swords.",
            Minecraft => "Be your Minecraft player: its health, hunger, armour, hotbar, swords, bows, elytra and blocks, in ULTRAKILL's levels.",
            _ => "Plain ULTRAKILL. Minecraft's player waits in the background until you come back.",
        };

        public static void Open(int currentMode, string buildKey)
        {
            Ensure(buildKey);
            current = highlight = currentMode;
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].Badge.text = i == current ? "CURRENT" : "";
                cards[i].Scale = 1f;
            }
            openedAt = Time.unscaledTime;
            root.SetActive(true);
            if (GameStateManager.Instance != null && !GameStateManager.Instance.IsStateActive(StateKey))
            {
                GameStateManager.Instance.RegisterState(new GameState(StateKey, root)
                {
                    cursorLock = LockMode.Unlock,
                    cameraInputLock = LockMode.Lock,
                    playerInputLock = LockMode.Lock,
                    priority = 3,
                });
            }
            if (MonoSingleton.GetInstance(typeof(TimeController)) is TimeController time)
            {
                time.timeScaleModifier = 0f;
                Time.timeScale = 0f;
            }
        }

        public static void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            root.SetActive(false);
            if (GameStateManager.Instance != null && GameStateManager.Instance.IsStateActive(StateKey))
            {
                GameStateManager.Instance.PopState(StateKey);
            }
            if (MonoSingleton.GetInstance(typeof(TimeController)) is TimeController time)
            {
                time.timeScaleModifier = 1f;
                time.RestoreTime();
            }
        }

        // Once a frame while open: the mode picked (Hybrid, Minecraft, Ultrakill), or -1. toggle: the
        // toggle key was pressed (it closes the menu).
        public static int Frame(bool toggle)
        {
            if (!IsOpen)
            {
                return -1;
            }
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            // (Not the press that opened it.)
            bool fresh = Time.unscaledTime - openedAt > 0.1f;
            if (fresh && (toggle || (kb != null && kb.escapeKey.wasPressedThisFrame)))
            {
                Close();
                return -1;
            }
            int picked = -1;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) picked = Hybrid;
                if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) picked = Minecraft;
                if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) picked = Ultrakill;
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) highlight = (highlight + 2) % 3;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) highlight = (highlight + 1) % 3;
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) picked = highlight;
            }
            if (mouse != null)
            {
                Vector2 at = mouse.position.ReadValue();
                for (int i = 0; i < cards.Length; i++)
                {
                    if (RectTransformUtility.RectangleContainsScreenPoint(cards[i].Rect, at, null))
                    {
                        if (mouse.delta.ReadValue() != Vector2.zero)
                        {
                            highlight = i;
                        }
                        if (fresh && mouse.leftButton.wasPressedThisFrame)
                        {
                            picked = i;
                        }
                    }
                }
            }
            Animate();
            if (picked >= 0)
            {
                Close();
            }
            return picked;
        }

        private static void Animate()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.15f);
            float pop = 0.92f + 0.08f * (1f - (1f - t) * (1f - t));
            panel.localScale = new Vector3(pop, pop, 1f);
            float k = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            for (int i = 0; i < cards.Length; i++)
            {
                Card c = cards[i];
                bool on = i == highlight;
                c.Scale = Mathf.Lerp(c.Scale, on ? 1.06f : 1f, k);
                c.Rect.localScale = new Vector3(c.Scale, c.Scale, 1f);
                c.Border.color = on ? accents[i] : accents[i] * new Color(0.45f, 0.45f, 0.45f, 1f);
                c.Fill.color = on ? new Color(0.13f, 0.11f, 0.14f, 0.97f) : new Color(0.07f, 0.06f, 0.08f, 0.95f);
            }
        }

        // ---- built once ----------------------------------------------------------------------------

        private static void Ensure(string buildKey)
        {
            if (root != null)
            {
                return;
            }
            Font font = UiFont();
            root = new GameObject("UltraCraft mode menu");
            Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32010;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            Image backdrop = NewImage(root.transform, "backdrop", new Color(0f, 0f, 0f, 0.78f));
            Stretch(backdrop.rectTransform);

            panel = new GameObject("panel", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(root.transform, false);
            panel.sizeDelta = new Vector2(1400, 860);

            Texture2D logo = LoadLogo();
            if (logo != null)
            {
                var img = new GameObject("logo", typeof(RectTransform)).AddComponent<RawImage>();
                img.transform.SetParent(panel, false);
                img.texture = logo;
                img.raycastTarget = false;
                float w = 900f;
                img.rectTransform.sizeDelta = new Vector2(w, w * logo.height / logo.width);
                img.rectTransform.anchoredPosition = new Vector2(0, 300);
            }
            else
            {
                Label(panel, "ULTRA-CRAFT", font, 96, accents[Hybrid], new Vector2(0, 300), new Vector2(1200, 140), TextAnchor.MiddleCenter);
            }
            Label(panel, "CHOOSE YOUR MODE", font, 34, new Color(0.85f, 0.85f, 0.85f), new Vector2(0, 185), new Vector2(1200, 50), TextAnchor.MiddleCenter);

            for (int i = 0; i < 3; i++)
            {
                cards[i] = MakeCard(panel, i, font, buildKey);
            }

            Label(panel, "CLICK OR PRESS 1  2  3     ← →  + ENTER     " + Plugin.ToggleKey.Value.ToUpperInvariant() + " / ESC  CLOSE",
                font, 24, new Color(0.6f, 0.6f, 0.6f), new Vector2(0, -330), new Vector2(1300, 40), TextAnchor.MiddleCenter);
            root.SetActive(false);
        }

        private static Card MakeCard(RectTransform parent, int i, Font font, string buildKey)
        {
            var card = new Card();
            card.Rect = new GameObject(titles[i], typeof(RectTransform)).GetComponent<RectTransform>();
            card.Rect.SetParent(parent, false);
            card.Rect.sizeDelta = new Vector2(400, 340);
            card.Rect.anchoredPosition = new Vector2((i - 1) * 440, -55);

            card.Border = NewImage(card.Rect, "border", accents[i]);
            Stretch(card.Border.rectTransform);
            card.Fill = NewImage(card.Rect, "fill", new Color(0.07f, 0.06f, 0.08f, 0.95f));
            Stretch(card.Fill.rectTransform, 4f);
            Image bar = NewImage(card.Rect, "bar", accents[i]);
            RectTransform br = bar.rectTransform;
            br.anchorMin = new Vector2(0, 1);
            br.anchorMax = new Vector2(1, 1);
            br.pivot = new Vector2(0.5f, 1);
            br.sizeDelta = new Vector2(0, 12);
            br.anchoredPosition = Vector2.zero;

            Label(card.Rect, (i + 1).ToString(), font, 30, new Color(1f, 1f, 1f, 0.55f), new Vector2(-165, 130), new Vector2(50, 50), TextAnchor.MiddleCenter);
            Label(card.Rect, titles[i], font, 54, accents[i], new Vector2(0, 85), new Vector2(380, 70), TextAnchor.MiddleCenter);
            Text desc = Label(card.Rect, Description(i, buildKey), font, 23, new Color(0.9f, 0.9f, 0.9f), new Vector2(0, -38), new Vector2(350, 180), TextAnchor.UpperCenter);
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            desc.lineSpacing = 1.15f;
            card.Badge = Label(card.Rect, "", font, 26, accents[i], new Vector2(0, -140), new Vector2(380, 40), TextAnchor.MiddleCenter);
            return card;
        }

        private static Image NewImage(Transform parent, string name, Color c)
        {
            var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            img.transform.SetParent(parent, false);
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private static void Stretch(RectTransform r, float inset = 0f)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset);
            r.offsetMax = new Vector2(-inset, -inset);
        }

        private static Text Label(Transform parent, string text, Font font, int size, Color c, Vector2 at, Vector2 box, TextAnchor anchor)
        {
            var t = new GameObject("text", typeof(RectTransform)).AddComponent<Text>();
            t.transform.SetParent(parent, false);
            t.font = font;
            t.fontSize = size;
            t.color = c;
            t.text = text;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.rectTransform.sizeDelta = box;
            t.rectTransform.anchoredPosition = at;
            var shadow = t.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2, -2);
            return t;
        }

        private static Font font;

        // ULTRAKILL's own pixel font if it's loaded, or Unity's.
        public static Font UiFont()
        {
            if (font != null)
            {
                return font;
            }
            Font fallback = null;
            foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (f == null)
                {
                    continue;
                }
                if (f.name.IndexOf("VCR", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return font = f;
                }
                fallback ??= f;
            }
            Font builtIn = null;
            try
            {
                builtIn = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch
            {
            }
            return font = builtIn != null ? builtIn : fallback;
        }

        private static Texture2D LoadLogo()
        {
            try
            {
                using (Stream s = typeof(ModeMenu).Assembly.GetManifestResourceStream("UltraCraft.logo.png"))
                {
                    if (s == null)
                    {
                        return null;
                    }
                    var bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = s.Read(bytes, read, bytes.Length - read);
                        if (n <= 0)
                        {
                            break;
                        }
                        read += n;
                    }
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    return tex.LoadImage(bytes) ? tex : null;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"mode menu: logo: {e.Message}");
                return null;
            }
        }
    }
}
