using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Killcraft
{
    // Minecraft renders its hand, hotbar, hearts and every open screen offscreen at ULTRAKILL's
    // resolution and publishes the pixels (premultiplied RGBA, bottom-up rows) through the overlay
    // triple buffer. They're shown over ULTRAKILL's frame on a top-most canvas, with ULTRAKILL's
    // light on the hand and held item (UkLighting) but not on the HUD.
    internal static class Overlay
    {
        private static GameObject root;
        private static LitImage image;
        private static Texture2D texture;
        private static Color light = Color.white;

        private static void Ensure()
        {
            if (root != null)
            {
                return;
            }
            root = new GameObject("Killcraft overlay");
            Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var child = new GameObject("Minecraft");
            child.transform.SetParent(root.transform, false);
            image = child.AddComponent<LitImage>();
            image.raycastTarget = false;
            RectTransform rt = image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            root.SetActive(false);
        }

        // handLight: what the hand is multiplied by (white: as Minecraft drew it); guiScale: Minecraft's.
        public static void Frame(bool show, Color handLight, int guiScale)
        {
            Ensure();
            if (Link.AcquireOverlayFrame())
            {
                Link.FrontHeader(out int w, out int h, out bool bottomUp);
                if (w > 0 && h > 0 && w <= Proto.MaxOverlayW && h <= Proto.MaxOverlayH)
                {
                    if (texture == null || texture.width != w || texture.height != h)
                    {
                        if (texture != null)
                        {
                            Object.Destroy(texture);
                        }
                        texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                        image.texture = texture;
                    }
                    texture.LoadRawTextureData(Link.FrontPixels, w * h * 4);
                    texture.Apply(false, false);
                    // Unity textures start at the bottom row, so bottom-up pixels need no flip.
                    image.uvRect = bottomUp ? new Rect(0, 0, 1, 1) : new Rect(0, 1, 1, -1);
                }
            }
            bool visible = show && texture != null;
            if (root.activeSelf != visible)
            {
                root.SetActive(visible);
            }
            // Quick to light up (a muzzle flash), a little slower to fade.
            float rate = handLight.grayscale > light.grayscale ? 30f : 12f;
            light = Color.Lerp(light, handLight, 1f - Mathf.Exp(-rate * Time.unscaledDeltaTime));
            if (visible)
            {
                image.SetLight(light, Mathf.Max(guiScale, 1), texture.width, texture.height);
            }
        }
    }

    // The overlay as a grid whose vertex colours carry the light: tinted everywhere but where
    // Minecraft's HUD is (hotbar, hearts, food, XP, item name, crosshair, boss bars, effects), with a
    // short fade at those edges.
    internal sealed class LitImage : RawImage
    {
        private Color light = Color.white;
        private int scale = 2, width = 1, height = 1;
        private static readonly List<float> xs = new List<float>(), ys = new List<float>();
        private static readonly List<Rect> hud = new List<Rect>();

        public void SetLight(Color l, int guiScale, int w, int h)
        {
            bool same = Mathf.Abs(l.r - light.r) < 0.004f && Mathf.Abs(l.g - light.g) < 0.004f && Mathf.Abs(l.b - light.b) < 0.004f;
            if (same && guiScale == scale && w == width && h == height)
            {
                return;
            }
            light = l;
            scale = guiScale;
            width = w;
            height = h;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            Rect uv = uvRect;
            Color32 plain = color;
            bool lit = light.r < 0.996f || light.g < 0.996f || light.b < 0.996f;
            // In Minecraft's pixels, y up from the bottom of the screen (its HUD is laid out from the bottom).
            float w = width, h = height, s = scale, cx = w / 2f, feather = 6f * s;
            xs.Clear();
            ys.Clear();
            xs.Add(0f);
            xs.Add(w);
            ys.Add(0f);
            ys.Add(h);
            hud.Clear();
            if (lit)
            {
                hud.Add(Rect.MinMaxRect(cx - 122f * s, 0f, cx + 122f * s, 24f * s));                    // hotbar, offhand, attack indicator
                hud.Add(Rect.MinMaxRect(cx - 100f * s, 0f, cx + 100f * s, 74f * s));                    // XP, hearts, armour, food, air, item name
                hud.Add(Rect.MinMaxRect(cx - 9f * s, h / 2f - 9f * s, cx + 9f * s, h / 2f + 9f * s));   // crosshair
                hud.Add(Rect.MinMaxRect(0f, h - 48f * s, w, h));                                        // boss bars, effects
                foreach (Rect a in hud)
                {
                    AddEdges(xs, a.xMin, feather, w);
                    AddEdges(xs, a.xMax, feather, w);
                    AddEdges(ys, a.yMin, feather, h);
                    AddEdges(ys, a.yMax, feather, h);
                }
            }
            xs.Sort();
            ys.Sort();
            Dedupe(xs);
            Dedupe(ys);
            int nx = xs.Count, ny = ys.Count;
            for (int j = 0; j < ny; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    float px = xs[i], py = ys[j];
                    float u = px / w, v = py / h;
                    Color32 c = plain;
                    if (lit)
                    {
                        float k = HudWeight(px, py, feather);
                        Color tint = Color.Lerp(light, Color.white, k);
                        c = new Color32((byte)(plain.r * tint.r), (byte)(plain.g * tint.g), (byte)(plain.b * tint.b), plain.a);
                    }
                    vh.AddVert(new Vector3(r.xMin + u * r.width, r.yMin + v * r.height), c, new Vector2(uv.xMin + u * uv.width, uv.yMin + v * uv.height));
                }
            }
            for (int j = 0; j + 1 < ny; j++)
            {
                for (int i = 0; i + 1 < nx; i++)
                {
                    int a = j * nx + i;
                    vh.AddTriangle(a, a + nx, a + nx + 1);
                    vh.AddTriangle(a + nx + 1, a + 1, a);
                }
            }
        }

        // 1 inside the HUD, 0 farther than the fade from it.
        private static float HudWeight(float x, float y, float feather)
        {
            float best = 0f;
            foreach (Rect a in hud)
            {
                float dx = Mathf.Max(a.xMin - x, 0f, x - a.xMax), dy = Mathf.Max(a.yMin - y, 0f, y - a.yMax);
                best = Mathf.Max(best, 1f - Mathf.Clamp01(Mathf.Max(dx, dy) / feather));
            }
            return best;
        }

        private static void AddEdges(List<float> list, float at, float feather, float max)
        {
            list.Add(Mathf.Clamp(at, 0f, max));
            list.Add(Mathf.Clamp(at - feather, 0f, max));
            list.Add(Mathf.Clamp(at + feather, 0f, max));
        }

        private static void Dedupe(List<float> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                if (list[i] - list[i - 1] < 0.5f)
                {
                    list.RemoveAt(i == list.Count - 1 ? i - 1 : i);  // (the screen's edges stay)
                }
            }
        }
    }
}
