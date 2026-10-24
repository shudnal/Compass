using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Compass
{
    internal static class PlayerIconColors
    {
        private sealed class ColoredIcon
        {
            internal Sprite sprite;
            internal Texture2D texture;
        }

        private static readonly Dictionary<int, ColoredIcon> icons = new Dictionary<int, ColoredIcon>();
        private static readonly Dictionary<Minimap.PinData, Sprite> playerPins = new Dictionary<Minimap.PinData, Sprite>();
        private static readonly HashSet<int> wantedColors = new HashSet<int>();
        private static readonly HashSet<int> warnedSources = new HashSet<int>();
        private static readonly List<int> removedColors = new List<int>();
        private static Sprite sourceSprite;
        private static Color32[] sourcePixels;
        private static byte[] redAmounts;
        private static int maximumRed;
        private static int sourceWidth;
        private static int sourceHeight;
        private static bool sourceFailed;
        private static bool paletteDirty = true;
        private static bool assignmentsDirty = true;

        internal static void InvalidatePalette() => paletteDirty = true;

        internal static void InvalidateAssignments() => assignmentsDirty = true;

        internal static bool TryGetIcon(Minimap.PinData pin, out Sprite sprite)
        {
            sprite = null;
            return PlayerColorSync.Enabled && pin != null && playerPins.TryGetValue(pin, out sprite) && sprite;
        }

        internal static void Update()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            if (!PlayerColorSync.Enabled)
            {
                ClearIcons();
                assignmentsDirty = true;
                return;
            }

            Minimap minimap = Minimap.instance;
            if (!minimap)
                return;

            Sprite currentSource = GetSourceSprite(minimap);
            if (currentSource != sourceSprite)
            {
                Reset();
                sourceSprite = currentSource;
            }

            if (!sourceSprite || sourceFailed || (!paletteDirty && !assignmentsDirty))
                return;

            // Configuration and assignment updates can arrive together during a server sync.
            paletteDirty = false;
            assignmentsDirty = false;
            try
            {
                if (sourcePixels == null)
                    ReadSourcePixels();

                UpdateIconCache();
                RefreshPlayerPins(minimap);
                // Apply the new sprites before rendering, after the regular HUD update has already run.
                CompassHUD.UpdatePins();
            }
            catch (Exception exception)
            {
                sourceFailed = true;
                ClearIcons();
                if (warnedSources.Add(sourceSprite.GetInstanceID()))
                    Compass.LogWarning($"Could not create colored player icons from '{sourceSprite.name}': {exception.Message}");
            }
        }

        internal static void Reset()
        {
            ClearIcons();
            sourceSprite = null;
            sourcePixels = null;
            redAmounts = null;
            maximumRed = 0;
            sourceWidth = 0;
            sourceHeight = 0;
            sourceFailed = false;
            paletteDirty = true;
            assignmentsDirty = true;
        }

        private static Sprite GetSourceSprite(Minimap minimap)
        {
            if (minimap.m_publicPosition && minimap.m_publicPosition.graphic is Image image && image.sprite)
                return image.sprite;

            if (minimap.m_icons != null)
                foreach (Minimap.SpriteData icon in minimap.m_icons)
                    if (icon.m_name == Minimap.PinType.Player && icon.m_icon)
                        return icon.m_icon;

            return null;
        }

        private static void UpdateIconCache()
        {
            wantedColors.Clear();
            IReadOnlyList<Color32> palette = PlayerColorSync.Palette;
            for (int i = 0; i < palette.Count; i++)
                wantedColors.Add(Pack(palette[i]));

            // A color assignment can arrive before its matching palette update.
            foreach (Color32 color in PlayerColorSync.AssignedColors)
                wantedColors.Add(Pack(color));

            removedColors.Clear();
            foreach (KeyValuePair<int, ColoredIcon> entry in icons)
                if (!wantedColors.Contains(entry.Key))
                    removedColors.Add(entry.Key);

            foreach (int key in removedColors)
            {
                DestroyIcon(icons[key]);
                icons.Remove(key);
            }

            foreach (int key in wantedColors)
                if (!icons.ContainsKey(key))
                    icons.Add(key, CreateIcon(new Color32((byte)(key >> 16), (byte)(key >> 8), (byte)key, 255)));
        }

        private static int Pack(Color32 color) => (color.r << 16) | (color.g << 8) | color.b;

        private static void RefreshPlayerPins(Minimap minimap)
        {
            playerPins.Clear();
            if (!PlayerColorSync.Enabled || sourceFailed || icons.Count == 0)
                return;

            // These lists have the same order after the game's UpdatePlayerPins call.
            int count = Math.Min(minimap.m_playerPins.Count, minimap.m_tempPlayerInfo.Count);
            for (int i = 0; i < count; i++)
            {
                Minimap.PinData pin = minimap.m_playerPins[i];
                if (pin != null && PlayerColorSync.TryGetColor(minimap.m_tempPlayerInfo[i], out Color32 color) &&
                    icons.TryGetValue(Pack(color), out ColoredIcon icon) && icon.sprite)
                    playerPins[pin] = icon.sprite;
            }
        }

        private static void ClearIcons()
        {
            playerPins.Clear();
            foreach (ColoredIcon icon in icons.Values)
                DestroyIcon(icon);

            icons.Clear();
            wantedColors.Clear();
            removedColors.Clear();
        }

        private static void DestroyIcon(ColoredIcon icon)
        {
            // Pooled compass Images can outlive both the assignment and the Minimap.
            foreach (CompassHUD.PinElement pin in CompassHUD.pinsList)
                if (pin.image && pin.image.sprite == icon.sprite)
                    pin.image.sprite = sourceSprite ? sourceSprite : null;

            if (icon.sprite)
                UnityEngine.Object.Destroy(icon.sprite);
            if (icon.texture)
                UnityEngine.Object.Destroy(icon.texture);
        }

        private static ColoredIcon CreateIcon(Color32 color)
        {
            Color32[] pixels = (Color32[])sourcePixels.Clone();
            for (int i = 0; i < pixels.Length; i++)
            {
                int red = redAmounts[i];
                if (red == 0)
                    continue;

                Color32 pixel = pixels[i];
                float strength = red / (float)maximumRed;
                pixels[i] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(pixel.r - red + color.r * strength), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(pixel.g + color.g * strength), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(pixel.b + color.b * strength), 0, 255),
                    pixel.a);
            }

            Texture2D texture = null;
            Sprite sprite = null;
            try
            {
                texture = new Texture2D(sourceWidth, sourceHeight, TextureFormat.RGBA32, false)
                {
                    name = $"Compass player icon {Pack(color):X6}",
                    filterMode = sourceSprite.texture.filterMode,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                Vector2 pivot = sourceSprite.pivot;
                sprite = Sprite.Create(texture, new Rect(0, 0, sourceWidth, sourceHeight),
                    new Vector2(pivot.x / sourceWidth, pivot.y / sourceHeight), sourceSprite.pixelsPerUnit,
                    0, SpriteMeshType.FullRect, sourceSprite.border);
                if (!sprite)
                    throw new InvalidOperationException("Unity could not create the player icon sprite.");

                sprite.name = texture.name;
                return new ColoredIcon { sprite = sprite, texture = texture };
            }
            catch
            {
                if (sprite)
                    UnityEngine.Object.Destroy(sprite);
                if (texture)
                    UnityEngine.Object.Destroy(texture);
                throw;
            }
        }

        private static void ReadSourcePixels()
        {
            Texture2D atlas = sourceSprite.texture;
            Vector2[] vertices = sourceSprite.vertices;
            Vector2[] uvs = sourceSprite.uv;
            ushort[] triangles = sourceSprite.triangles;
            sourceWidth = Mathf.RoundToInt(sourceSprite.rect.width);
            sourceHeight = Mathf.RoundToInt(sourceSprite.rect.height);
            if (!atlas || sourceWidth <= 0 || sourceHeight <= 0 || vertices.Length == 0 ||
                uvs.Length != vertices.Length || triangles.Length == 0 || triangles.Length % 3 != 0)
                throw new InvalidOperationException("The player icon has no usable sprite geometry.");

            Vector2 uvMin = uvs[0];
            Vector2 uvMax = uvs[0];
            for (int i = 1; i < uvs.Length; i++)
            {
                uvMin = Vector2.Min(uvMin, uvs[i]);
                uvMax = Vector2.Max(uvMax, uvs[i]);
            }

            int left = Mathf.Clamp(Mathf.FloorToInt(uvMin.x * atlas.width), 0, atlas.width - 1);
            int bottom = Mathf.Clamp(Mathf.FloorToInt(uvMin.y * atlas.height), 0, atlas.height - 1);
            int right = Mathf.Clamp(Mathf.CeilToInt(uvMax.x * atlas.width), left + 1, atlas.width);
            int top = Mathf.Clamp(Mathf.CeilToInt(uvMax.y * atlas.height), bottom + 1, atlas.height);
            int width = right - left;
            int height = top - bottom;
            Color32[] croppedPixels = ReadAtlasRegion(atlas, left, bottom, width, height);

            // UVs preserve atlas rotation. Local vertices preserve trimmed transparent borders.
            // This also works for tightly packed sprites, whose textureRect is unavailable.
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = vertices[i] * sourceSprite.pixelsPerUnit + sourceSprite.pivot;

            sourcePixels = new Color32[sourceWidth * sourceHeight];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                CopyTriangle(vertices[a], vertices[b], vertices[c], uvs[a], uvs[b], uvs[c],
                    atlas.width, atlas.height, croppedPixels, left, bottom, width, height);
            }

            redAmounts = new byte[sourcePixels.Length];
            maximumRed = 0;
            for (int i = 0; i < sourcePixels.Length; i++)
            {
                Color32 pixel = sourcePixels[i];
                if (pixel.a == 0)
                    continue;

                int red = pixel.r - Math.Max(pixel.g, pixel.b);
                if (red <= 0)
                    continue;

                redAmounts[i] = (byte)red;
                maximumRed = Math.Max(maximumRed, red);
            }

            if (maximumRed == 0)
                throw new InvalidOperationException("The player icon has no red pixels to recolor.");
        }

        private static Color32[] ReadAtlasRegion(Texture2D atlas, int left, int bottom, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture temporary = null;
            Texture2D readable = null;
            try
            {
                temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(atlas, temporary,
                    new Vector2(width / (float)atlas.width, height / (float)atlas.height),
                    new Vector2(left / (float)atlas.width, bottom / (float)atlas.height));
                RenderTexture.active = temporary;
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgbWrite;
                if (temporary)
                    RenderTexture.ReleaseTemporary(temporary);
                if (readable)
                    UnityEngine.Object.Destroy(readable);
            }
        }

        private static void CopyTriangle(Vector2 a, Vector2 b, Vector2 c, Vector2 uvA, Vector2 uvB, Vector2 uvC,
            int atlasWidth, int atlasHeight, Color32[] croppedPixels, int left, int bottom, int width, int height)
        {
            float denominator = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(denominator) < 0.00001f)
                return;

            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, sourceWidth - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, sourceWidth);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, sourceHeight - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, sourceHeight);
            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    float px = x + 0.5f - c.x;
                    float py = y + 0.5f - c.y;
                    float weightA = ((b.y - c.y) * px + (c.x - b.x) * py) / denominator;
                    float weightB = ((c.y - a.y) * px + (a.x - c.x) * py) / denominator;
                    float weightC = 1f - weightA - weightB;
                    if (weightA < -0.0001f || weightB < -0.0001f || weightC < -0.0001f)
                        continue;

                    Vector2 uv = uvA * weightA + uvB * weightB + uvC * weightC;
                    int sourceX = Mathf.Clamp(Mathf.FloorToInt(uv.x * atlasWidth) - left, 0, width - 1);
                    int sourceY = Mathf.Clamp(Mathf.FloorToInt(uv.y * atlasHeight) - bottom, 0, height - 1);
                    sourcePixels[y * sourceWidth + x] = croppedPixels[sourceY * width + sourceX];
                }
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Awake))]
        private static class Minimap_Awake_PlayerIconColors
        {
            private static void Postfix() => Reset();
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePlayerPins))]
        private static class Minimap_UpdatePlayerPins_PlayerIconColors
        {
            private static void Postfix(Minimap __instance) => RefreshPlayerPins(__instance);
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnDestroy))]
        private static class Minimap_OnDestroy_PlayerIconColors
        {
            private static void Prefix() => Reset();
        }
    }
}
