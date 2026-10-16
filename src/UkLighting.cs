using System.Collections.Generic;
using UnityEngine;

namespace Killcraft
{
    // ULTRAKILL's lights on what Minecraft lights itself: the hand and held item (drawn by Minecraft
    // into the overlay) and its player, mobs, dropped items and particles (WorldRender). The light at a
    // point is added up the way ULTRAKILL's vertex-lit shader lights V1's arm and the enemies: the
    // ambient colour plus every Unity light that reaches it (Unity's vertex-light falloff, spot lights
    // in their cone), so coloured rooms, lamps, muzzle flashes and explosions show on Minecraft's things.
    internal static class UkLighting
    {
        private struct Source
        {
            public Vector3 Pos, Dir;
            public Color Col;  // colour times intensity
            public float RangeSq, Falloff, SpotCos, SpotScale;
            public int Mask;
            public bool Directional, Spot;
        }

        private static readonly List<Source> sources = new List<Source>();
        private static readonly Dictionary<long, Color> cells = new Dictionary<long, Color>();
        private static Color ambient;
        private static int frame = -1;
        private static int handMask = -1;
        private static float logAt;

        // (Directional lights have no position: about how much of one falls on a surface that isn't
        // facing it squarely, as V1's arm turns about.)
        private const float DirectionalShare = 0.75f;
        // Lights that reach ULTRAKILL's level and enemies (Default, Environment, Limb, BigCorpse, EnemyTrigger).
        public const int WorldMask = (1 << 0) | (1 << 8) | (1 << 10) | (1 << 11) | (1 << 12);
        private const float Cell = 0.75f;

        public static bool On => Plugin.UltrakillLight.Value;

        // Once a frame: the lights there are now (explosions and muzzle flashes come and go).
        public static void Frame()
        {
            if (frame == Time.frameCount)
            {
                return;
            }
            frame = Time.frameCount;
            sources.Clear();
            cells.Clear();
            if (!On)
            {
                return;
            }
            ambient = RenderSettings.ambientLight;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l == null || !l.isActiveAndEnabled || l.intensity <= 0f || l.type == LightType.Area)
                {
                    continue;
                }
                Color c = l.color * l.intensity;
                if (c.maxColorComponent < 0.01f)
                {
                    continue;
                }
                var s = new Source
                {
                    Col = c,
                    Mask = l.cullingMask,
                    Directional = l.type == LightType.Directional,
                    Spot = l.type == LightType.Spot,
                    Pos = l.transform.position,
                    Dir = l.transform.forward,
                };
                float range = Mathf.Max(l.range, 0.01f);
                s.RangeSq = range * range;
                s.Falloff = 25f / s.RangeSq;  // Unity's vertex lights: 1 / (1 + 25 d² / range²)
                if (s.Spot)
                {
                    float a = l.spotAngle * Mathf.Deg2Rad;
                    s.SpotCos = Mathf.Cos(a * 0.5f);
                    s.SpotScale = 1f / Mathf.Max(Mathf.Cos(a * 0.25f) - s.SpotCos, 1e-4f);
                }
                sources.Add(s);
            }
        }

        // The light at a point, from the lights that light any of the layers in mask.
        public static Color At(Vector3 p, int mask)
        {
            Color sum = ambient;
            for (int i = 0; i < sources.Count; i++)
            {
                Source s = sources[i];
                if ((s.Mask & mask) == 0)
                {
                    continue;
                }
                if (s.Directional)
                {
                    sum += s.Col * DirectionalShare;
                    continue;
                }
                Vector3 d = p - s.Pos;
                float dsq = d.sqrMagnitude;
                if (dsq >= s.RangeSq)
                {
                    continue;
                }
                float edge = dsq / s.RangeSq;
                // (Faded out over the last of the range, so lights don't pop at its edge.)
                float atten = (1f - edge * edge) / (1f + dsq * s.Falloff);
                if (s.Spot)
                {
                    float rho = dsq > 1e-6f ? Vector3.Dot(d, s.Dir) / Mathf.Sqrt(dsq) : 1f;
                    atten *= Mathf.Clamp01((rho - s.SpotCos) * s.SpotScale);
                }
                sum += s.Col * atten;
            }
            return sum;
        }

        // The same for Minecraft's things in the level, shared by everything in one small cell (a
        // mob's vertices mostly are), once a frame.
        public static Color AtWorld(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x / Cell), y = Mathf.FloorToInt(p.y / Cell), z = Mathf.FloorToInt(p.z / Cell);
            long key = ((long)x & 0x1FFFFF) | (((long)y & 0x1FFFFF) << 21) | (((long)z & 0x1FFFFF) << 42);
            if (!cells.TryGetValue(key, out Color c))
            {
                c = At(new Vector3((x + 0.5f) * Cell, (y + 0.5f) * Cell, (z + 0.5f) * Cell), WorldMask);
                cells[key] = c;
            }
            return c;
        }

        // What Minecraft's colours are multiplied by under that light: never darker than Darkest, and
        // no brighter than Minecraft draws them (full light). Past full light it's the light's colour
        // that shows (a red lamp in a lit room still turns the hand red, as it does the walls).
        public static Color Shade(Color light)
        {
            float k = Mathf.Max(Plugin.LightStrength.Value, 0f), lo = Mathf.Clamp01(Plugin.LightDarkest.Value);
            Color l = light * k;
            float top = Mathf.Max(1f, Mathf.Max(l.r, Mathf.Max(l.g, l.b)));
            return new Color(
                lo + (1f - lo) * Mathf.Clamp01(l.r / top),
                lo + (1f - lo) * Mathf.Clamp01(l.g / top),
                lo + (1f - lo) * Mathf.Clamp01(l.b / top),
                1f);
        }

        public static Color32 Apply(Color32 c, Color shade) =>
            new Color32((byte)(c.r * shade.r), (byte)(c.g * shade.g), (byte)(c.b * shade.b), c.a);

        // The light on Minecraft's hand: a little in front of the camera and to the right, from the
        // lights that light V1's guns (their layer).
        public static Color Hand(Camera cam)
        {
            if (!On || cam == null)
            {
                return Color.white;
            }
            if (handMask == -1 && MonoSingleton.GetInstance(typeof(GunControl)) is GunControl guns && guns.currentWeapon != null)
            {
                Renderer r = guns.currentWeapon.GetComponentInChildren<Renderer>(true);
                if (r != null)
                {
                    handMask = 1 << r.gameObject.layer;
                    Plugin.Log.LogInfo($"lighting: V1's guns are on layer {r.gameObject.layer}");
                }
            }
            Transform t = cam.transform;
            float u = Coords.U;
            Vector3 at = t.position + t.forward * (0.6f * u) + t.right * (0.35f * u) - t.up * (0.3f * u);
            Color light = At(at, handMask == -1 ? ~0 : handMask);
            if (Plugin.Diagnostics.Value && Time.unscaledTime >= logAt)
            {
                logAt = Time.unscaledTime + 2f;
                Plugin.Log.LogInfo($"lighting: hand light {light} (ambient {ambient}, {sources.Count} lights) -> {Shade(light)}");
            }
            return Shade(light);
        }
    }
}
