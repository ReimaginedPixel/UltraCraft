using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Killcraft
{
    // Minecraft's death screen, drawn by ULTRA-CRAFT: in Minecraft mode, dying is Minecraft's "You
    // Died!" with a Respawn button (SkyCraft respawns Minecraft's player at once, so Minecraft never
    // shows its own), over the stopped game. Respawn restarts ULTRAKILL from its last checkpoint (Host).
    internal static class DeathScreen
    {
        private const string StateKey = "ultracraft-death-screen";

        private static GameObject root;
        private static Text score;
        private static Image button, buttonEdge;
        private static float shownAt;

        public static bool IsOpen => root != null && root.activeSelf;

        public static void Show(int stylePoints)
        {
            Ensure();
            score.text = $"Score: <color=#FFFF55>{stylePoints}</color>";
            shownAt = Time.unscaledTime;
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

        public static void Hide()
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

        // Once a frame while shown: true when Respawn is clicked (or Enter / Space pressed). As in
        // Minecraft, the button only works after a second.
        public static bool Frame()
        {
            if (!IsOpen)
            {
                return false;
            }
            bool ready = Time.unscaledTime - shownAt > 1f;
            Mouse mouse = Mouse.current;
            Keyboard kb = Keyboard.current;
            bool over = mouse != null && RectTransformUtility.RectangleContainsScreenPoint(button.rectTransform, mouse.position.ReadValue(), null);
            buttonEdge.color = !ready ? new Color(0.2f, 0.2f, 0.2f) : over ? Color.white : Color.black;
            button.color = !ready ? new Color(0.25f, 0.25f, 0.25f) : over ? new Color(0.5f, 0.5f, 0.65f) : new Color(0.43f, 0.43f, 0.43f);
            return ready && ((over && mouse.leftButton.wasPressedThisFrame)
                || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)));
        }

        private static void Ensure()
        {
            if (root != null)
            {
                return;
            }
            Font font = ModeMenu.UiFont();
            root = new GameObject("UltraCraft death screen");
            Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32005;  // (over Minecraft's HUD, under the mode menu)
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            // Minecraft's red fade: 0x60500000 at the top to 0xA0803030 at the bottom.
            var fade = new GameObject("fade", typeof(RectTransform)).AddComponent<RawImage>();
            fade.transform.SetParent(root.transform, false);
            fade.raycastTarget = false;
            var gradient = new Texture2D(1, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            gradient.SetPixels(new[] { new Color(0.5f, 0.19f, 0.19f, 0.63f), new Color(0.31f, 0f, 0f, 0.38f) });  // (bottom row first)
            gradient.Apply(false, true);
            fade.texture = gradient;
            RectTransform fr = fade.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = Vector2.zero;
            fr.offsetMax = Vector2.zero;

            Label("You Died!", font, 96, Color.white, new Vector2(0, 300), new Vector2(1400, 130));
            score = Label("", font, 40, Color.white, new Vector2(0, 150), new Vector2(1200, 60));
            score.supportRichText = true;

            buttonEdge = new GameObject("Respawn", typeof(RectTransform)).AddComponent<Image>();
            buttonEdge.transform.SetParent(root.transform, false);
            buttonEdge.raycastTarget = false;
            buttonEdge.rectTransform.sizeDelta = new Vector2(808, 88);
            buttonEdge.rectTransform.anchoredPosition = new Vector2(0, -20);
            button = new GameObject("face", typeof(RectTransform)).AddComponent<Image>();
            button.transform.SetParent(buttonEdge.transform, false);
            button.raycastTarget = false;
            button.rectTransform.anchorMin = Vector2.zero;
            button.rectTransform.anchorMax = Vector2.one;
            button.rectTransform.offsetMin = new Vector2(4, 4);
            button.rectTransform.offsetMax = new Vector2(-4, -4);
            Text t = Label("Respawn", font, 40, Color.white, Vector2.zero, new Vector2(800, 80));
            t.transform.SetParent(buttonEdge.transform, false);
            Label("back to ULTRAKILL's last checkpoint", font, 26, new Color(0.85f, 0.85f, 0.85f), new Vector2(0, -110), new Vector2(1200, 40));
            root.SetActive(false);
        }

        private static Text Label(string text, Font font, int size, Color c, Vector2 at, Vector2 box)
        {
            var t = new GameObject("text", typeof(RectTransform)).AddComponent<Text>();
            t.transform.SetParent(root.transform, false);
            t.font = font;
            t.fontSize = size;
            t.color = c;
            t.text = text;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.rectTransform.sizeDelta = box;
            t.rectTransform.anchoredPosition = at;
            var shadow = t.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            shadow.effectDistance = new Vector2(4, -4);
            return t;
        }
    }
}
