using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Compass
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    public class Compass : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.Compass";
        public const string pluginName = "Compass";
        public const string pluginVersion = "1.0.1";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static Compass instance;

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> loggingEnabled;

        public static ConfigEntry<OrientationType> orientation;
        public static ConfigEntry<float> scale;
        public static ConfigEntry<Vector2> offset;
        public static ConfigEntry<bool> showCenter;

        public static ConfigEntry<CompassPinType> showPins;
        public static ConfigEntry<string> pinNamesToIgnore;
        public static ConfigEntry<bool> showOnlyLastDeath;
        public static ConfigEntry<bool> hideChecked;
        public static ConfigEntry<bool> hideShared;

        public static ConfigEntry<Vector2> pinsAlpha;
        public static ConfigEntry<Vector2> pinsScale;
        public static ConfigEntry<Vector4> pinsStyleConditions;

        public static ConfigEntry<Color> compassColor;
        public static ConfigEntry<Color> centerColor;
        public static ConfigEntry<Color> pinsColor;

        public static readonly string configDirectory = Path.Combine(Paths.ConfigPath, pluginID);

        public enum OrientationType
        {
            Camera,
            Player
        }

        [Flags]
        public enum CompassPinType
        {
            None = 0,
            Icon0 = 1,
            Icon1 = 2,
            Icon2 = 4,
            Icon3 = 8,
            Icon4 = 0x10,
            Death = 0x20,
            Bed = 0x40,
            Shout = 0x80,
            Boss = 0x100,
            Player = 0x200,
            RandomEvent = 0x400,
            Ping = 0x800,
            EventArea = 0x1000,
            HildirQuest = 0x2000,
            Static = 0x4000,
            Custom = 0x8000,
            All = 0xFFFF
        }

        private void Awake()
        {
            harmony.PatchAll();

            instance = this;

            ConfigInit();

            Game.isModded = true;

            CompassHUD.CheckImageFiles();

            SetupFileWatcher();
        }

        private void ConfigInit()
        {
            modEnabled = Config.Bind("General", "Enabled", defaultValue: true, "Enable the mod.");
            loggingEnabled = Config.Bind("General", "Logging enabled", defaultValue: false, "Enable logging.");

            modEnabled.SettingChanged += (s, e) => CompassHUD.UpdateParentObject();

            orientation = Config.Bind("Compass", "Orientation based on", defaultValue: OrientationType.Camera, "Orientation type. Camera direction or player eyes direction could be used as a center of a compass.");
            scale = Config.Bind("Compass", "Scale", defaultValue: 1f, "Scale of whole compass component");
            offset = Config.Bind("Compass", "Position offset", defaultValue: Vector2.zero, "Offset from initial position in the middle top of the screen");
            showCenter = Config.Bind("Compass", "Show center", defaultValue: true, "Show center marker");

            scale.SettingChanged += (s, e) => CompassHUD.UpdateParentObject();
            offset.SettingChanged += (s, e) => CompassHUD.UpdateParentObject();
            showCenter.SettingChanged += (s, e) => CompassHUD.UpdateCenterObject();

            showPins = Config.Bind("Pins", "Show pins", defaultValue: CompassPinType.All, "Pin types to show on the compass. Use Configuration Manager for more convenient editing." +
                "\nStatic - fixed locations like Haldor, Hildir, Sacrificial Stones" +
                "\nCustom - Any custom pin type added by other mods" +
                "\nEventArea - red circle around an event" +
                "\nRandomEvent - red exclamation mark of an event");
            pinNamesToIgnore = Config.Bind("Pins", "Ignore pin names", defaultValue: "Silver&&Obsidian&&Copper&&Tin",
                    new ConfigDescription("&& separated pin names to ignore. Wildcards * and ? supported. Use Configuration Manager for more convenient editing.", null, new CustomConfigs.ConfigurationManagerAttributes { CustomDrawer = CustomConfigs.DrawSeparatedStrings("&&") }));
            showOnlyLastDeath = Config.Bind("Pins", "Show only last death", defaultValue: true, "Death pins except the last one will be hidden.");
            hideChecked = Config.Bind("Pins", "Hide checked pins", defaultValue: false, "Hide pins checked by red cross.");
            hideShared = Config.Bind("Pins", "Hide shared pins", defaultValue: false, "Hide pins shared via Cartography Table.");

            showPins.SettingChanged += (s, e) => CompassHUD.UpdatePinsObject();
            pinNamesToIgnore.SettingChanged += (s, e) => UpdatePinFilterNames();

            compassColor = Config.Bind("Style", "Compass color", Color.white - new Color(0f, 0f, 0f, 0.5f), "Compass color");
            centerColor = Config.Bind("Style", "Center color", Color.yellow - new Color(0f, 0f, 0f, 0.5f), "Center marker color");
            
            compassColor.SettingChanged += (s, e) => CompassHUD.UpdateCompassObject();
            centerColor.SettingChanged += (s, e) => CompassHUD.UpdateCenterObject();

            pinsColor = Config.Bind("Pin style", "Color", Color.clear, "Pins color. If not set - default is white");
            pinsAlpha = Config.Bind("Pin style", "Alpha", new Vector2(1f, 0.33f), "Pins alpha. X for max alpha, Y for min alpha");
            pinsScale = Config.Bind("Pin style", "Scale", new Vector2(1f, 0.33f), "Pins scale. X for max scale, Y for min scale");
            pinsStyleConditions = Config.Bind("Pin style", "Style conditions", new Vector4(1f, 20f, 250f, 350f), "Conditions for alpha and scale application" +
                                                                                                                 "\nX - Minimum distance to show pins" +
                                                                                                                 "\nY - Distance where pins will start to become smaller. Size is at maximum. Alpha is at maximum." +
                                                                                                                 "\nZ - Distance where pins will start to become more transparent. Size is at minimum. Alpha is at maximum." +
                                                                                                                 "\nW - Maximum distance to show pins. Size is at minimum. Alpha is at minimum.");

            pinsStyleConditions.SettingChanged += (s, e) => UpdatePinsStyleConditions();
        }

        private void OnDestroy()
        {
            Config.Save();
            instance = null;
            harmony?.UnpatchSelf();
        }

        public static void LogInfo(object data)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogInfo(data);
        }

        public static void LogWarning(object data) => instance.Logger.LogWarning(data);

        private static void UpdatePinFilterNames()
        {
            CompassHUD.filteredWildcards.Clear();
            CompassHUD.filteredNames = new HashSet<string>(pinNamesToIgnore.Value.Split(new string[] { "&&" }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().ToLower()).Where(p => !string.IsNullOrWhiteSpace(p)));
            CompassHUD.filteredNames.DoIf(str => str.IndexOf('*') != -1 || str.IndexOf('?') != -1, str => CompassHUD.filteredWildcards.Add(str));
            CompassHUD.filteredNames.RemoveWhere(str => CompassHUD.filteredWildcards.Contains(str));
        }

        private static void UpdatePinsStyleConditions()
        {
            pinsStyleConditions.Value = new Vector4(Mathf.FloorToInt(pinsStyleConditions.Value.x),
                                                    Mathf.FloorToInt(pinsStyleConditions.Value.y),
                                                    Mathf.FloorToInt(pinsStyleConditions.Value.z),
                                                    Mathf.FloorToInt(pinsStyleConditions.Value.w));
        }

        public static void SetupFileWatcher()
        {
            FileSystemWatcher fileSystemWatcherPlugin = new FileSystemWatcher(configDirectory, ImageFileInfo.filter);
            fileSystemWatcherPlugin.Changed += new FileSystemEventHandler(OnTextureFileChange);
            fileSystemWatcherPlugin.Created += new FileSystemEventHandler(OnTextureFileChange);
            fileSystemWatcherPlugin.Renamed += new RenamedEventHandler(OnTextureFileChange);
            fileSystemWatcherPlugin.Deleted += new FileSystemEventHandler(OnTextureFileChange);
            fileSystemWatcherPlugin.IncludeSubdirectories = false;
            fileSystemWatcherPlugin.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            fileSystemWatcherPlugin.EnableRaisingEvents = true;
        }
        
        private static void OnTextureFileChange(object sender, FileSystemEventArgs eargs)
        {
            ImageFileInfo.TryLoadFile(eargs.Name);
            if (eargs is RenamedEventArgs)
                ImageFileInfo.TryClearFile((eargs as RenamedEventArgs).OldName);
        }

        internal static bool LoadTextureFromConfigDirectory(string filename, ref Texture2D tex)
        {
            string fileInConfigFolder = Path.Combine(configDirectory, filename);
            if (!File.Exists(fileInConfigFolder))
                return false;

            LogInfo($"Loaded image from config folder: {filename}");
            return tex.LoadImage(File.ReadAllBytes(fileInConfigFolder));
        }

        internal static byte[] GetEmbeddedFileData(string filename)
        {
            Assembly executingAssembly = Assembly.GetExecutingAssembly();

            string name = executingAssembly.GetManifestResourceNames().Single(str => str.EndsWith(filename));

            Stream resourceStream = executingAssembly.GetManifestResourceStream(name);

            byte[] data = new byte[resourceStream.Length];
            resourceStream.Read(data, 0, data.Length);

            return data;
        }
    }
}
