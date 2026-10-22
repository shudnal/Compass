using BepInEx.Configuration;
using ConditionalConfigSync;
using HarmonyLib;
using Splatform;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Compass
{
    internal static class PlayerColorSync
    {
        private const string configSection = "Player colors";
        private const int colorCount = 30;

        private static readonly Color32[] defaultColors =
        {
            new Color32(204, 100, 100, 255), // Crimson
            new Color32(132, 180, 212, 255), // Frost blue
            new Color32(220, 204, 100, 255), // Gold
            new Color32(76, 188, 124, 255),  // Emerald
            new Color32(164, 140, 212, 255), // Amethyst
            new Color32(228, 164, 84, 255), // Amber
            new Color32(60, 164, 148, 255),  // Teal
            new Color32(204, 100, 188, 255), // Heather
            new Color32(220, 212, 188, 255), // Ivory
            new Color32(164, 188, 100, 255)  // Moss
        };

        private static readonly ConfigEntry<Color>[] colorEntries = new ConfigEntry<Color>[colorCount];
        private static readonly Color32[] slotColors = new Color32[colorCount];
        private static readonly bool[] activeSlots = new bool[colorCount];
        private static readonly int[] slotUseCounts = new int[colorCount];
        private static readonly List<Color32> palette = new List<Color32>(colorCount);
        private static readonly HashSet<Color32> uniqueColors = new HashSet<Color32>();
        private static readonly Dictionary<string, int> serverSlots = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly List<string> connectedPlayers = new List<string>();
        private static readonly HashSet<string> connectedPlayerSet = new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<string> removedPlayers = new List<string>();
        private static readonly Dictionary<PlatformUserID, Color32> activePlayerColors = new Dictionary<PlatformUserID, Color32>();
        private static readonly List<int> freeSlots = new List<int>(colorCount);
        private static readonly System.Random random = new System.Random();

        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<bool> randomAssignment;
        private static CustomSyncedValue<Dictionary<string, Color>> syncedColors;
        private static bool paletteDirty = true;
        private static int nextSlot;

        internal static bool Enabled => enabled != null && enabled.Value && Compass.modEnabled.Value;
        internal static IReadOnlyList<Color32> Palette => palette;
        internal static IEnumerable<Color32> AssignedColors => activePlayerColors.Values;

        internal static void Register(
            Func<string, string, bool, string, ConfigEntry<bool>> boolConfig,
            Func<string, string, Color, string, ConfigEntry<Color>> colorConfig)
        {
            enabled = boolConfig(configSection, "Enabled", true,
                "Use a different color for each player's compass icon. Install Compass on the server and clients to share the same colors. Map icons are not changed.");
            randomAssignment = boolConfig(configSection, "Random assignment", false,
                "Choose a random unused color when a player connects. When disabled, use palette order. When all colors are in use, reuse them in palette order.");

            enabled.SettingChanged += OnConfigChanged;
            randomAssignment.SettingChanged += OnConfigChanged;

            for (int i = 0; i < colorCount; i++)
            {
                Color defaultColor = i < defaultColors.Length ? (Color)defaultColors[i] : Color.clear;
                colorEntries[i] = colorConfig(configSection, $"Color {i + 1:00}", defaultColor,
                    "Player icon color. Set alpha to 0 to leave this slot unused. Other alpha values keep the original icon transparency. Duplicate RGB colors use one slot.");
                colorEntries[i].SettingChanged += OnConfigChanged;
            }

            syncedColors = new CustomSyncedValue<Dictionary<string, Color>>(
                Compass.configSync, "Player colors", new Dictionary<string, Color>(StringComparer.Ordinal));
            syncedColors.ValueChanged += OnColorsChanged;
            Compass.configSync.SourceOfTruthChanged += OnSourceOfTruthChanged;
            Compass.configSync.ServerConnectionReset += ResetSession;
            RefreshPalette();
        }

        private static void OnConfigChanged(object sender, EventArgs e) => paletteDirty = true;

        internal static void Update()
        {
            if (!paletteDirty)
                return;

            RefreshPalette();
            ReconcilePlayers(ZNet.instance);
        }

        private static void RefreshPalette()
        {
            if (!paletteDirty)
                return;

            paletteDirty = false;
            palette.Clear();
            uniqueColors.Clear();
            Array.Clear(activeSlots, 0, activeSlots.Length);

            for (int i = 0; i < colorEntries.Length; i++)
            {
                Color color = colorEntries[i].Value;
                if (color.a <= 0f)
                    continue;

                if (!IsFinite(color.r) || !IsFinite(color.g) || !IsFinite(color.b) || !IsFinite(color.a))
                {
                    Compass.LogWarning($"Player color {i + 1:00} contains an invalid color value. This slot is not used.");
                    continue;
                }

                Color32 normalized = color;
                normalized.a = 255;
                slotColors[i] = normalized;
                if (!uniqueColors.Add(normalized))
                    continue;

                activeSlots[i] = true;
                palette.Add(normalized);
            }

            PlayerIconColors.InvalidatePalette();
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void ReconcilePlayers(ZNet network)
        {
            // Optional-server fallback also sets IsSourceOfTruth on clients. Only a real server assigns colors.
            if (!network || network.HaveStopped || !network.IsServer() || !Compass.configSync.IsSourceOfTruth || syncedColors == null)
                return;

            RefreshPalette();
            connectedPlayers.Clear();
            connectedPlayerSet.Clear();
            foreach (ZNet.PlayerInfo player in network.GetPlayerList())
            {
                if (!player.m_userInfo.m_id.IsValid)
                    continue;

                string playerID = player.m_userInfo.m_id.ToString();
                if (connectedPlayerSet.Add(playerID))
                    connectedPlayers.Add(playerID);
            }

            removedPlayers.Clear();
            foreach (KeyValuePair<string, int> assignment in serverSlots)
                if (!connectedPlayerSet.Contains(assignment.Key) || !activeSlots[assignment.Value])
                    removedPlayers.Add(assignment.Key);

            foreach (string playerID in removedPlayers)
                serverSlots.Remove(playerID);

            Array.Clear(slotUseCounts, 0, slotUseCounts.Length);
            foreach (int slot in serverSlots.Values)
                slotUseCounts[slot]++;

            // GetPlayerList preserves connection order and places a local host before remote players.
            foreach (string playerID in connectedPlayers)
            {
                if (serverSlots.ContainsKey(playerID))
                    continue;

                int slot = SelectSlot();
                if (slot < 0)
                    break;

                serverSlots.Add(playerID, slot);
                slotUseCounts[slot]++;
                nextSlot = (slot + 1) % colorCount;
            }

            Dictionary<string, Color> snapshot = new Dictionary<string, Color>(StringComparer.Ordinal);
            if (enabled.Value)
                foreach (KeyValuePair<string, int> assignment in serverSlots)
                    snapshot.Add(assignment.Key, slotColors[assignment.Value]);

            // Dictionary equality is otherwise reference equality. Do not resend an unchanged player list.
            if (!ColorsEqual(syncedColors.Value, snapshot))
                syncedColors.AssignLocalValueIfChanged(snapshot);
        }

        private static int SelectSlot()
        {
            freeSlots.Clear();
            int nextActiveSlot = -1;
            for (int offset = 0; offset < colorCount; offset++)
            {
                int slot = (nextSlot + offset) % colorCount;
                if (!activeSlots[slot])
                    continue;

                if (nextActiveSlot < 0)
                    nextActiveSlot = slot;
                if (slotUseCounts[slot] == 0)
                    freeSlots.Add(slot);
            }

            if (freeSlots.Count == 0)
                return nextActiveSlot;

            return freeSlots[randomAssignment.Value ? random.Next(freeSlots.Count) : 0];
        }

        private static bool ColorsEqual(Dictionary<string, Color> left, Dictionary<string, Color> right)
        {
            if (left == null || left.Count != right.Count)
                return false;

            foreach (KeyValuePair<string, Color> assignment in right)
                if (!left.TryGetValue(assignment.Key, out Color color) || !color.Equals(assignment.Value))
                    return false;

            return true;
        }

        private static void OnColorsChanged()
        {
            activePlayerColors.Clear();
            if (syncedColors.Value != null)
                foreach (KeyValuePair<string, Color> assignment in syncedColors.Value)
                {
                    PlatformUserID playerID;
                    try
                    {
                        playerID = new PlatformUserID(assignment.Key);
                    }
                    catch (Exception exception)
                    {
                        Compass.LogWarning($"Received an invalid player color identifier: {exception.Message}");
                        continue;
                    }
                    Color color = assignment.Value;
                    if (!playerID.IsValid || color.a <= 0f || !IsFinite(color.r) || !IsFinite(color.g) || !IsFinite(color.b) || !IsFinite(color.a))
                        continue;

                    Color32 normalized = color;
                    normalized.a = 255;
                    activePlayerColors[playerID] = normalized;
                }

            PlayerIconColors.InvalidateAssignments();
        }

        internal static bool TryGetColor(ZNet.PlayerInfo player, out Color32 color) =>
            activePlayerColors.TryGetValue(player.m_userInfo.m_id, out color);

        private static void OnSourceOfTruthChanged(bool isSourceOfTruth)
        {
            if (!isSourceOfTruth)
            {
                // This updates the local fallback after CCS has handed ownership to the server.
                serverSlots.Clear();
                nextSlot = 0;
                syncedColors.AssignLocalValueIfChanged(new Dictionary<string, Color>(StringComparer.Ordinal));
            }
        }

        private static void ResetSession()
        {
            serverSlots.Clear();
            connectedPlayers.Clear();
            connectedPlayerSet.Clear();
            removedPlayers.Clear();
            activePlayerColors.Clear();
            nextSlot = 0;
            paletteDirty = true;
            syncedColors?.AssignLocalValueIfChanged(new Dictionary<string, Color>(StringComparer.Ordinal));
            PlayerIconColors.Reset();
        }

        internal static void Shutdown()
        {
            if (syncedColors != null)
                syncedColors.ValueChanged -= OnColorsChanged;
            Compass.configSync.SourceOfTruthChanged -= OnSourceOfTruthChanged;
            Compass.configSync.ServerConnectionReset -= ResetSession;
            enabled.SettingChanged -= OnConfigChanged;
            randomAssignment.SettingChanged -= OnConfigChanged;
            foreach (ConfigEntry<Color> entry in colorEntries)
                entry.SettingChanged -= OnConfigChanged;
            ResetSession();
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        private static class ZNet_Awake_PlayerColors
        {
            private static void Prefix() => ResetSession();
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.UpdatePlayerList))]
        private static class ZNet_UpdatePlayerList_PlayerColors
        {
            private static void Postfix(ZNet __instance) => ReconcilePlayers(__instance);
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
        private static class ZNet_OnDestroy_PlayerColors
        {
            private static void Prefix() => ResetSession();
        }
    }
}
