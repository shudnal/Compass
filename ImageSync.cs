using BepInEx.Configuration;
using ConditionalConfigSync;
using System;
using System.Collections.Generic;
using System.IO;

namespace Compass
{
    internal static class ImageSync
    {
        private const string localPayload = "local";
        private const string missingPayload = "missing";
        private const string dataPrefix = "png:";
        private const string configSection = "Server image sync";

        internal static readonly string[] imageIDs =
        {
            CompassHUD.fileNameCompass,
            CompassHUD.fileNameCompassBottom,
            CompassHUD.fileNameCompassDetailed,
            CompassHUD.fileNameCompassDetailedBottom,
            CompassHUD.fileNameCenter,
            CompassHUD.fileNameCenterBottom,
            CompassHUD.fileNameCenterDetailed,
            CompassHUD.fileNameCenterDetailedBottom,
            CompassHUD.fileNameMask,
            CompassHUD.fileNameMaskDetailed,
            CompassHUD.fileNameOverlay,
            CompassHUD.fileNameUnderlay
        };

        private sealed class SyncedImage
        {
            internal readonly string imageID;
            internal readonly ConfigEntry<bool> syncEnabled;
            internal readonly CustomSyncedValue<string> payload;

            internal SyncedImage(string imageID, ConfigEntry<bool> syncEnabled)
            {
                this.imageID = imageID;
                this.syncEnabled = syncEnabled;
                payload = new CustomSyncedValue<string>(Compass.configSync, $"Image {imageID}", localPayload);
                payload.ValueChanged += Apply;
                syncEnabled.SettingChanged += OnSyncSettingChanged;
            }

            private void OnSyncSettingChanged(object sender, EventArgs e)
            {
                if (Compass.configSync.IsSourceOfTruth)
                    Publish(forceNotify: false);
            }

            internal void Initialize()
            {
                if (syncEnabled.Value)
                    Publish(forceNotify: true);
                else
                    payload.AssignLocalValueIfChanged(localPayload);
            }

            internal void StoreLocalFallback()
            {
                payload.AssignLocalValueIfChanged(localPayload);
            }

            internal bool IsRemoteOverrideActive =>
                !Compass.configSync.IsSourceOfTruth && payload.Value != localPayload;

            internal void Publish(bool forceNotify)
            {
                if (!Compass.configSync.IsSourceOfTruth)
                    return;

                string newPayload = BuildPayload();
                if (newPayload == null)
                    return;

                if (forceNotify)
                    payload.AssignLocalValueAndNotify(newPayload);
                else
                    payload.AssignLocalValueIfChanged(newPayload);
            }

            private string BuildPayload()
            {
                if (!syncEnabled.Value)
                    return localPayload;

                ImageFileInfo imageInfo = ImageFileInfo.GetImageInfo(imageID);
                if (!File.Exists(imageInfo.filePath))
                    return missingPayload;

                if (!imageInfo.TryReadValidFileData(out byte[] imageData))
                {
                    Compass.LogWarning($"Failed to synchronize invalid image {imageInfo.fileName}. The previous synchronized image remains active.");
                    return null;
                }

                return dataPrefix + Convert.ToBase64String(imageData);
            }

            private void Apply()
            {
                ImageFileInfo imageInfo = ImageFileInfo.GetImageInfo(imageID);
                string activePayload = payload.Value;
                if (string.IsNullOrEmpty(activePayload))
                {
                    Compass.LogWarning($"Received empty synchronized image payload for {imageInfo.fileName}.");
                    return;
                }

                if (activePayload == localPayload)
                {
                    ApplyLocalFile(imageInfo);
                    return;
                }

                if (activePayload == missingPayload)
                {
                    ClearAndNotify(imageInfo);
                    return;
                }

                if (!activePayload.StartsWith(dataPrefix, StringComparison.Ordinal))
                {
                    Compass.LogWarning($"Received unsupported synchronized image payload for {imageInfo.fileName}.");
                    return;
                }

                try
                {
                    byte[] imageData = Convert.FromBase64String(activePayload.Substring(dataPrefix.Length));
                    if (!imageInfo.Load(imageData))
                        Compass.LogWarning($"Failed to apply synchronized image {imageInfo.fileName}. The previous valid image remains active.");
                }
                catch (FormatException)
                {
                    Compass.LogWarning($"Received malformed synchronized image data for {imageInfo.fileName}.");
                }
            }
        }

        private static readonly Dictionary<string, SyncedImage> syncedImages =
            new Dictionary<string, SyncedImage>(StringComparer.OrdinalIgnoreCase);

        internal static void Register(Func<string, string, bool, string, ConfigEntry<bool>> serverConfig)
        {
            foreach (string imageID in imageIDs)
            {
                string filename = Path.ChangeExtension(imageID, ImageFileInfo.ext);
                ConfigEntry<bool> syncEnabled = serverConfig(
                    configSection,
                    $"Sync {filename}",
                    false,
                    $"Synchronize {filename} from the server. When disabled, every client uses its local file.");

                syncedImages[imageID] = new SyncedImage(imageID, syncEnabled);
            }

            Compass.configSync.SourceOfTruthChanged += OnSourceOfTruthChanged;
            Compass.configSync.ServerConnectionReset += OnServerConnectionReset;
        }

        internal static void Initialize()
        {
            foreach (SyncedImage syncedImage in syncedImages.Values)
                syncedImage.Initialize();
        }

        internal static bool IsRemoteOverrideActive(string imageID)
        {
            return syncedImages.TryGetValue(imageID, out SyncedImage syncedImage)
                && syncedImage.IsRemoteOverrideActive;
        }

        internal static void Publish(string imageID)
        {
            if (syncedImages.TryGetValue(imageID, out SyncedImage syncedImage))
                syncedImage.Publish(forceNotify: false);
        }

        private static void OnSourceOfTruthChanged(bool isSourceOfTruth)
        {
            if (isSourceOfTruth)
                return;

            foreach (SyncedImage syncedImage in syncedImages.Values)
                syncedImage.StoreLocalFallback();
        }

        private static void OnServerConnectionReset()
        {
            foreach (SyncedImage syncedImage in syncedImages.Values)
                syncedImage.Publish(forceNotify: true);
        }

        private static void ApplyLocalFile(ImageFileInfo imageInfo)
        {
            if (imageInfo.Load())
                return;

            if (!File.Exists(imageInfo.filePath))
                ClearAndNotify(imageInfo);
        }

        private static void ClearAndNotify(ImageFileInfo imageInfo)
        {
            imageInfo.Clear();
            imageInfo.textureChanged?.Invoke();
        }
    }
}
