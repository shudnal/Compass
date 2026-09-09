using BepInEx;
using BepInEx.Configuration;
using ConditionalConfigSync;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace Compass
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInDependency("_shudnal.ConditionalConfigSync", "1.0.5")]
    public class Compass : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.Compass";
        public const string pluginName = "Compass";
        public const string pluginVersion = "1.1.1";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID)
        {
            DisplayName = pluginName,
            CurrentVersion = pluginVersion,
            MinimumRequiredVersion = pluginVersion,
            ModRequired = false
        };

        internal static Compass instance;
        private static FileSystemWatcher imageFileWatcher;
        private static readonly Dictionary<string, Coroutine> pendingImageReloads = new Dictionary<string, Coroutine>(StringComparer.OrdinalIgnoreCase);

        private const float imageReloadDelay = 0.1f;
        private const int imageReloadAttempts = 5;

        public static ConfigEntry<bool> configLocked;
        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> loggingEnabled;

        public static ConfigEntry<OrientationType> orientation;
        public static ConfigEntry<AnchorPositionType> anchorPosition;
        public static ConfigEntry<float> scale;
        public static ConfigEntry<Vector2> offset;
        public static ConfigEntry<bool> showCenter;
        public static ConfigEntry<bool> detailedMode;

        public static ConfigEntry<CompassPinType> showPins;
        public static ConfigEntry<string> pinNamesToIgnore;
        public static ConfigEntry<bool> showOnlyLastDeath;
        public static ConfigEntry<bool> hideChecked;
        public static ConfigEntry<bool> hideShared;
        public static ConfigEntry<bool> alwaysShowPinText;
        public static ConfigEntry<KeyboardShortcut> holdToAlwaysShowPings;
        public static ConfigEntry<KeyboardShortcut> holdToAlwaysShowShouts;
        public static ConfigEntry<KeyboardShortcut> holdToAlwaysShowPlayerPin;
        public static ConfigEntry<KeyboardShortcut> holdToShowText;

        public static ConfigEntry<Vector2> pinsAlpha;
        public static ConfigEntry<Vector2> pinsScale;
        public static ConfigEntry<Vector4> pinsStyleConditions;

        public static ConfigEntry<Color> compassColor;
        public static ConfigEntry<Color> centerColor;
        public static ConfigEntry<Color> pinsColor;
        public static ConfigEntry<float> pinTextSize;
        public static ConfigEntry<Color> pinTextColor;
        public static ConfigEntry<TMPro.FontStyles> pinTextFormat;
        public static ConfigEntry<Vector2> pinNameOffset;
        public static ConfigEntry<Vector2> pinOffset;
        public static ConfigEntry<bool> pinNameScaleDistant;

        public static readonly string configDirectory = Path.Combine(Paths.ConfigPath, pluginID);

        public enum OrientationType
        {
            Camera,
            Player
        }

        public enum AnchorPositionType
        {
            Top,
            Bottom
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
            instance = this;

            ConfigInit();

            _ = configSync.AddLockingConfigEntry(configLocked);

            harmony.PatchAll();

            Game.isModded = true;

            CompassHUD.CheckImageFiles();
            ImageSync.Initialize();

            SetupFileWatcher();
        }

        private void ConfigInit()
        {
            configLocked = serverConfig("General", "Lock Configuration", defaultValue: true, "Configuration is locked and can be changed by server admins only.");
            modEnabled = config("General", "Enabled", defaultValue: true, "Enable the mod.", synchronizedSetting: true);
            loggingEnabled = config("General", "Logging enabled", defaultValue: false, "Enable logging.");

            modEnabled.SettingChanged += (s, e) => CompassHUD.UpdateParentObject();

            orientation = config("Compass", "Orientation based on", defaultValue: OrientationType.Camera, "Orientation type. Camera direction or player eyes direction could be used as a center of a compass.", synchronizedSetting: true);
            anchorPosition = config("Compass", "Anchor position", defaultValue: AnchorPositionType.Top, "Defines whether compass is anchored to top or bottom side of the screen.", synchronizedSetting: true);
            scale = config("Compass", "Scale", defaultValue: 1f, "Scale of whole compass component");
            offset = config("Compass", "Position offset", defaultValue: Vector2.zero, "Offset from selected anchor position. X moves compass left/right. Y moves compass down from top, up from bottom.");
            showCenter = config("Compass", "Show center", defaultValue: true, "Show center marker");
            detailedMode = config("Compass", "Detailed mode", defaultValue: false, "Use the detailed compass, center, and mask image set.", synchronizedSetting: true);

            anchorPosition.SettingChanged += (s, e) => CompassHUD.UpdateImageMode();

            scale.SettingChanged += (s, e) => CompassHUD.UpdateParentObject();
            offset.SettingChanged += (s, e) => CompassHUD.UpdateParentObject();
            showCenter.SettingChanged += (s, e) => CompassHUD.UpdateCenterObject();
            detailedMode.SettingChanged += (s, e) => CompassHUD.UpdateImageMode();

            showPins = config("Pins", "Show pins", defaultValue: CompassPinType.All, "Pin types to show on the compass. Use Configuration Manager for more convenient editing." +
                "\nStatic - fixed locations like Haldor, Hildir, Bog Witch, Sacrificial Stones" +
                "\nCustom - Any custom pin type added by other mods" +
                "\nEventArea - red circle around an event" +
                "\nRandomEvent - red exclamation mark of an event", synchronizedSetting: true);
            pinNamesToIgnore = config("Pins", "Ignore pin names", defaultValue: "Silver&&Obsidian&&Copper&&Tin",
                    new ConfigDescription("&& separated pin names to ignore. Wildcards * and ? supported. Use Configuration Manager for more convenient editing.", null, new CustomConfigs.ConfigurationManagerAttributes { CustomDrawer = CustomConfigs.DrawSeparatedStrings("&&") }), synchronizedSetting: true);
            showOnlyLastDeath = config("Pins", "Show only last death", defaultValue: true, "Death pins except the last one will be hidden.", synchronizedSetting: true);
            hideChecked = config("Pins", "Hide checked pins", defaultValue: false, "Hide pins checked by red cross.", synchronizedSetting: true);
            hideShared = config("Pins", "Hide shared pins", defaultValue: false, "Hide pins shared via Cartography Table.", synchronizedSetting: true);
            holdToAlwaysShowShouts = config("Pins", "Hold to show shouts at any distance", defaultValue: new KeyboardShortcut(KeyCode.LeftAlt), "Hold to show player shouts without distance filter.", synchronizedSetting: true);
            holdToAlwaysShowPlayerPin = config("Pins", "Hold to show players at any distance", defaultValue: new KeyboardShortcut(KeyCode.LeftAlt), "Hold to show players with public positions without distance filter.", synchronizedSetting: true);
            holdToAlwaysShowPings = config("Pins", "Hold to show pings at any distance", defaultValue: new KeyboardShortcut(KeyCode.LeftAlt), "Hold to show player pings without distance filter.", synchronizedSetting: true);
            holdToShowText = config("Pins", "Hold to show pin text", defaultValue: new KeyboardShortcut(KeyCode.LeftControl, KeyCode.LeftAlt), "Hold to show pin text if it is set.", synchronizedSetting: true);
            alwaysShowPinText = config("Pins", "Always show pin text", defaultValue: false, "Always show pin text if it is set.", synchronizedSetting: true);

            showPins.SettingChanged += (s, e) => CompassHUD.UpdatePinsObject();
            pinNamesToIgnore.SettingChanged += (s, e) => UpdatePinFilterNames();
            UpdatePinFilterNames();

            compassColor = config("Style", "Compass color", Color.white - new Color(0f, 0f, 0f, 0.5f), "Compass color");
            centerColor = config("Style", "Center color", Color.yellow - new Color(0f, 0f, 0f, 0.5f), "Center marker color");

            compassColor.SettingChanged += (s, e) => CompassHUD.UpdateCompassObject();
            centerColor.SettingChanged += (s, e) => CompassHUD.UpdateCenterObject();

            pinsColor = config("Pin style", "Color", Color.clear, "Pins color. If not set - default is white");
            pinsAlpha = config("Pin style", "Alpha", new Vector2(1f, 0.33f), "Pins alpha. X for max alpha, Y for min alpha");
            pinsScale = config("Pin style", "Scale", new Vector2(1f, 0.33f), "Pins scale. X for max scale, Y for min scale");
            pinOffset = config("Pin style", "Pin offset", Vector2.zero, "Additional pin offset relative to the compass. Y is flipped automatically for top/bottom anchor modes.");
            pinsStyleConditions = config("Pin style", "Style conditions", new Vector4(1f, 20f, 250f, 550f), "Conditions for alpha and scale application" +
                                                                                                                 "\nX - Minimum distance to show pins" +
                                                                                                                 "\nY - Distance where pins will start to become smaller. Size is at maximum. Alpha is at maximum." +
                                                                                                                 "\nZ - Distance where pins will start to become more transparent. Size is at minimum. Alpha is at maximum." +
                                                                                                                 "\nW - Maximum distance to show pins. Size is at minimum. Alpha is at minimum.", synchronizedSetting: true);
            pinsStyleConditions.SettingChanged += (s, e) => UpdatePinsStyleConditions();
            UpdatePinsStyleConditions();

            pinTextSize = config("Pin text style", "Size", 18f, "Pin text size");
            pinTextColor = config("Pin text style", "Color", Color.white, "Pin text color");
            pinTextFormat = config("Pin text style", "Format", FontStyles.Bold, "Pin text format");
            pinNameOffset = config("Pin text style", "Name offset", Vector2.zero, "Additional name offset relative to the pin icon. Y is flipped automatically for top/bottom anchor modes.");
            pinNameScaleDistant = config("Pin text style", "Scale distant pin text", true, "Scale pin text size a bit when it becomes too distant");

            pinTextSize.SettingChanged += (s, e) => CompassHUD.UpdatePinTextStyle();
            pinTextColor.SettingChanged += (s, e) => CompassHUD.UpdatePinTextStyle();
            pinTextFormat.SettingChanged += (s, e) => CompassHUD.UpdatePinTextStyle();

            ImageSync.Register((group, name, defaultValue, description) =>
                serverConfig(group, name, defaultValue, description));
        }

        private void OnDestroy()
        {
            imageFileWatcher?.Dispose();
            imageFileWatcher = null;

            foreach (Coroutine coroutine in pendingImageReloads.Values)
                if (coroutine != null)
                    StopCoroutine(coroutine);

            pendingImageReloads.Clear();

            Config.Save();
            instance = null;
            harmony?.UnpatchSelf();
        }

#pragma warning disable IDE1006 // Naming Styles
        ConfigEntry<T> config<T>(string group, string name, T defaultValue, ConfigDescription description, bool synchronizedSetting = false)
        {
            return configSync.AddConfigEntry(
                Config,
                group,
                name,
                defaultValue,
                description,
                syncMode: ConfigSyncMode.Conditional,
                serverControlledByDefault: synchronizedSetting).SourceConfig;
        }

        ConfigEntry<T> serverConfig<T>(string group, string name, T defaultValue, ConfigDescription description)
        {
            return configSync.AddConfigEntry(
                Config,
                group,
                name,
                defaultValue,
                description,
                syncMode: ConfigSyncMode.AlwaysServerControlled,
                serverControlledByDefault: true).SourceConfig;
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = false) => config(group, name, defaultValue, new ConfigDescription(description), synchronizedSetting);

        ConfigEntry<T> serverConfig<T>(string group, string name, T defaultValue, string description) => serverConfig(group, name, defaultValue, new ConfigDescription(description));
#pragma warning restore IDE1006 // Naming Styles

        public static void LogInfo(object data)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogInfo(data);
        }

        public static void LogWarning(object data) => instance.Logger.LogWarning(data);

        private static void UpdatePinFilterNames()
        {
            CompassHUD.filteredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CompassHUD.filteredWildcardPatterns.Clear();

            IEnumerable<string> filters = pinNamesToIgnore.Value
                .Split(new[] { "&&" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value));

            foreach (string filter in filters)
            {
                if (filter.IndexOf('*') >= 0 || filter.IndexOf('?') >= 0)
                    CompassHUD.filteredWildcardPatterns.Add(new WildcardPattern(filter));
                else
                    CompassHUD.filteredNames.Add(filter);
            }
        }

        private static void UpdatePinsStyleConditions()
        {
            Vector4 source = pinsStyleConditions.Value;
            float minimumDistance = Mathf.Max(0f, Mathf.Floor(source.x));
            float scaleStartDistance = Mathf.Max(minimumDistance, Mathf.Floor(source.y));
            float alphaStartDistance = Mathf.Max(scaleStartDistance + 1f, Mathf.Floor(source.z));
            float maximumDistance = Mathf.Max(alphaStartDistance + 1f, Mathf.Floor(source.w));

            CompassHUD.effectivePinsStyleConditions = new Vector4(
                minimumDistance,
                scaleStartDistance,
                alphaStartDistance,
                maximumDistance);

            if (source != CompassHUD.effectivePinsStyleConditions)
                LogWarning($"Invalid Pin style conditions {source}. Runtime values were normalized to {CompassHUD.effectivePinsStyleConditions}.");
        }

        public static void SetupFileWatcher()
        {
            imageFileWatcher?.Dispose();
            imageFileWatcher = new FileSystemWatcher(configDirectory, ImageFileInfo.filter)
            {
                IncludeSubdirectories = false,
                SynchronizingObject = ThreadingHelper.SynchronizingObject
            };
            imageFileWatcher.Changed += OnTextureFileChange;
            imageFileWatcher.Created += OnTextureFileChange;
            imageFileWatcher.Renamed += OnTextureFileChange;
            imageFileWatcher.Deleted += OnTextureFileChange;
            imageFileWatcher.EnableRaisingEvents = true;
        }

        private static void OnTextureFileChange(object sender, FileSystemEventArgs eargs)
        {
            QueueImageFileRefresh(eargs.Name);

            if (eargs is RenamedEventArgs renamedEvent)
                QueueImageFileRefresh(renamedEvent.OldName);
        }

        private static void QueueImageFileRefresh(string filename)
        {
            if (instance == null || string.IsNullOrEmpty(filename))
                return;

            string imageID = Path.GetFileNameWithoutExtension(filename);
            if (!ImageFileInfo.images.ContainsKey(imageID))
                return;

            if (pendingImageReloads.TryGetValue(imageID, out Coroutine pendingReload) && pendingReload != null)
                instance.StopCoroutine(pendingReload);

            pendingImageReloads[imageID] = instance.StartCoroutine(RefreshImageFile(imageID));
        }

        private static IEnumerator RefreshImageFile(string imageID)
        {
            yield return new WaitForSecondsRealtime(imageReloadDelay);

            ImageFileInfo imageInfo = ImageFileInfo.GetImageInfo(imageID);
            if (ImageSync.IsRemoteOverrideActive(imageID))
            {
                pendingImageReloads.Remove(imageID);
                yield break;
            }

            for (int attempt = 0; attempt < imageReloadAttempts; ++attempt)
            {
                if (!File.Exists(imageInfo.filePath))
                {
                    ImageFileInfo.TryClearFile(imageInfo.fileName);
                    ImageSync.Publish(imageID);
                    pendingImageReloads.Remove(imageID);
                    yield break;
                }

                if (ImageFileInfo.TryLoadFile(imageInfo.fileName))
                {
                    ImageSync.Publish(imageID);
                    pendingImageReloads.Remove(imageID);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(imageReloadDelay);
            }

            pendingImageReloads.Remove(imageID);
            LogWarning($"Failed to reload image {imageInfo.fileName}. The previous valid image remains active.");
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
