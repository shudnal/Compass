using HarmonyLib;
using System;
using System.IO;
using UnityEngine;
using static Compass.Compass;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

namespace Compass
{
    internal static class CompassHUD
    {
        public const string fileNameCompass = "compass";
        public const string fileNameCenter = "center";
        public const string fileNameMask = "mask";
        public const string fileNameOverlay = "overlay";
        public const string fileNameUnderlay = "underlay";

        private const string objectRootName = "Compass_Parent";
        private const string objectOverlayName = "Overlay";
        private const string objectUnderlayName = "Underlay";
        private const string objectMaskName = "Mask";
        private const string objectCompassName = "Compass";
        private const string objectCenterName = "Center";
        private const string objectPinsRootName = "Pins";
        private const string objectPinElementName = "PinElement";
        private const string objectPinElementCheckedName = "Checked";

        private static readonly int layerUI = LayerMask.NameToLayer("UI");

        public static GameObject parentObject;
        public static GameObject compassObject;
        public static GameObject centerObject;
        public static RectTransform pinsRootObject;
        public static RectTransform pinElement;

        public static float scaleFactor = 1f;
        public static float compassWidth;

        public static readonly List<Minimap.PinData> tempPins = new List<Minimap.PinData>();
        public static readonly List<PinElement> pinsList = new List<PinElement>();

        public static HashSet<string> filteredNames = new HashSet<string>();
        public static HashSet<string> filteredWildcards = new HashSet<string>();

        public class PinElement
        {
            public string name;
            public RectTransform rect;
            public Image image;
            public GameObject checkedIcon;

            public PinElement()
            {
                rect = UnityEngine.Object.Instantiate(pinElement, pinsRootObject);
                rect.gameObject.SetActive(value: true);
                
                image = rect.GetComponent<Image>();
                checkedIcon = rect.Find(objectPinElementCheckedName)?.gameObject;

                pinsList.Add(this);
            }

            public void Destroy() => UnityEngine.Object.Destroy(rect?.gameObject);
        }

        public static void CheckImageFiles()
        {
            Directory.CreateDirectory(configDirectory);

            CheckFile(fileNameCompass);
            CheckFile(fileNameCenter);
            CheckFile(fileNameMask);

            static void CheckFile(string id)
            {
                ImageFileInfo fileinfo = ImageFileInfo.GetImageInfo(id);
                if (!fileinfo.initialized)
                {
                    File.WriteAllBytes(fileinfo.filePath, GetEmbeddedFileData(fileinfo.fileName));
                    fileinfo.Load();
                }
            }
        }

        public static void UpdateParentObject()
        {
            if (!parentObject)
                return;

            parentObject.SetActive(modEnabled.Value);

            RectTransform rt = parentObject.GetComponent<RectTransform>();

            rt.localScale = Vector3.one * scale.Value / scaleFactor;

            Texture2D compass = ImageFileInfo.GetImageInfo(fileNameCompass).texture;
            if (compass)
                rt.anchoredPosition = new Vector2(0f, (Screen.height / scaleFactor - compass.height * scale.Value / scaleFactor) / 2) - offset.Value;
        }

        public static void UpdateCenterObject()
        {
            if (!centerObject)
                return;

            centerObject.GetComponent<Image>().color = centerColor.Value;
            centerObject.SetActive(showCenter.Value);
        }

        public static void UpdateMaskObject()
        {
            Texture2D compass = ImageFileInfo.GetImageInfo(fileNameCompass).texture;
            if (compass)
                ImageFileInfo.GetImageInfo(fileNameMask).SetSpriteWidth(compass.width / 2).UpdateGameObject();
        }

        public static void UpdateCompassObject()
        {
            if (!compassObject) 
                return;

            Image image = compassObject.GetComponent<Image>();
            image.color = compassColor.Value;
            compassWidth = image.sprite.rect.width;
        }

        public static void UpdatePinsObject()
        {
            if (!pinsRootObject)
                return;

            pinsRootObject.gameObject.SetActive(showPins.Value != CompassPinType.None);

            Texture2D compass = ImageFileInfo.GetImageInfo(fileNameCompass).texture;
            if (compass != null)
                pinsRootObject.sizeDelta = new Vector2(compass.width / 2, compass.height);

            pinElement?.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, pinsRootObject.sizeDelta.y);
            pinElement?.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, pinsRootObject.sizeDelta.y);
        }

        public static void InitializeCompass()
        {
            pinsList.Clear();
            tempPins.Clear();

            ImageFileInfo compass = ImageFileInfo.GetImageInfo(fileNameCompass);
            if (!compass.initialized)
            {
                LogWarning($"Mandatory file {compass.fileName} is not found");
                return;
            }

            ImageFileInfo center = ImageFileInfo.GetImageInfo(fileNameCenter);
            if (!center.initialized)
            {
                LogWarning($"Mandatory file {center.fileName} is not found");
                return;
            }

            ImageFileInfo mask = ImageFileInfo.GetImageInfo(fileNameMask);
            if (!mask.initialized)
            {
                LogWarning($"Mandatory file {mask.fileName} is not found");
                return;
            }

            // Parent object to set visibility
            parentObject = new GameObject(objectRootName, typeof(RectTransform))
            {
                layer = layerUI
            };
            parentObject.transform.SetParent(Hud.instance.m_rootObject.transform);

            // Overlay object
            GameObject overlayObject = new GameObject(objectOverlayName, typeof(RectTransform))
            {
                layer = layerUI
            };
            overlayObject.transform.SetParent(parentObject.transform, false);
            ImageFileInfo.SetGameObject(fileNameOverlay, overlayObject).UpdateGameObject();

            // Underlay object
            GameObject underlayObject = new GameObject(objectUnderlayName, typeof(RectTransform))
            {
                layer = layerUI
            };
            underlayObject.transform.SetParent(parentObject.transform, false);
            ImageFileInfo.SetGameObject(fileNameUnderlay, underlayObject).UpdateGameObject();

            // Mask object
            GameObject maskObject = new GameObject(objectMaskName, typeof(RectTransform))
            {
                layer = layerUI
            };
            maskObject.transform.SetParent(parentObject.transform, false);
            mask.SetGameObject(maskObject);
            UpdateMaskObject();
            maskObject.AddComponent<Mask>().showMaskGraphic = false;

            // Compass object
            compassObject = new GameObject(objectCompassName, typeof(RectTransform))
            {
                layer = layerUI
            };
            compassObject.transform.SetParent(maskObject.transform, false);
            compass.SetGameObject(compassObject).UpdateGameObject();
            compass.textureChanged = (Action)Delegate.Combine(new Action(UpdateParentObject), new Action(UpdateCompassObject), new Action(UpdateMaskObject));

            // Center object
            centerObject = new GameObject(objectCenterName, typeof(RectTransform))
            {
                layer = layerUI
            };
            centerObject.transform.SetParent(maskObject.transform, false);
            center.SetGameObject(centerObject).UpdateGameObject();

            // Pins root object
            pinsRootObject = new GameObject(objectPinsRootName, typeof(RectTransform))
            {
                layer = layerUI
            }.GetComponent<RectTransform>();
            pinsRootObject.transform.SetParent(maskObject.transform, false);

            // Pin element
            pinElement = new GameObject(objectPinElementName, typeof(RectTransform))
            {
                layer = layerUI
            }.GetComponent<RectTransform>();
            pinElement.SetParent(parentObject.transform, false);
            pinElement.gameObject.AddComponent<Image>();
            pinElement.gameObject.SetActive(false);

            GameObject checkedPin = UnityEngine.Object.Instantiate(Minimap.instance.m_pinPrefab.transform.Find(objectPinElementCheckedName).gameObject, pinElement);
            checkedPin.name = objectPinElementCheckedName;
            checkedPin.SetActive(false);

            UpdateParentObject();

            UpdateCenterObject();

            UpdateCompassObject();

            UpdatePinsObject();
        }

        public static void UpdateCompass()
        {
            if (!modEnabled.Value || !Player.m_localPlayer || !compassObject)
                return;

            float angle = orientation.Value == OrientationType.Camera ? GameCamera.instance.transform.eulerAngles.y : Player.m_localPlayer.transform.eulerAngles.y;

            if (angle > 180)
                angle -= 360;

            angle *= -Mathf.Deg2Rad;

            compassObject.GetComponent<RectTransform>().localPosition = Vector3.right * (compassWidth / 2) * angle / (2f * Mathf.PI) - new Vector3(compassWidth * 0.125f, 0, 0);

            UpdatePins();
        }

        private static void UpdatePinList()
        {
            tempPins.Clear();

            if (showOnlyLastDeath.Value)
                AddPin(Minimap.instance.m_deathPin);

            AddPinRange(Minimap.instance.m_pins);

            tempPins.Sort((x, y) => ComparePins(x, y));

            static int ComparePins(Minimap.PinData x, Minimap.PinData y)
            {
                if (y.m_pos == x.m_pos)
                {
                    if (x.m_type == Minimap.PinType.EventArea)
                        return -1;
                    else if (y.m_type == Minimap.PinType.EventArea)
                        return 1;

                    return y.m_type.CompareTo(x.m_type);
                }

                return Utils.DistanceXZ(Player.m_localPlayer.transform.position, y.m_pos).CompareTo(Utils.DistanceXZ(Player.m_localPlayer.transform.position, x.m_pos));
            }
        }

        private static void AddPinRange(IEnumerable<Minimap.PinData> pinList) => pinList.Do(AddPin);

        private static void AddPin(Minimap.PinData pin)
        {
            if (pin == null)
                return;

            if (hideChecked.Value && pin.m_checked)
                return;

            if (hideShared.Value && pin.m_ownerID != 0L)
                return;

            CompassPinType pinType = GetPinType(pin.m_type);
            if (!showPins.Value.HasFlag(pinType))
                return;

            if (showOnlyLastDeath.Value && (pinType == CompassPinType.Death))
                return;

            float distance = Utils.DistanceXZ(Player.m_localPlayer.transform.position, pin.m_pos);
            if (distance < pinsStyleConditions.Value.x || distance > pinsStyleConditions.Value.w)
                return;

            if (filteredNames.Contains(pin.m_name))
                return;

            if (filteredWildcards.Any(wildcard => new WildcardPattern(wildcard).IsMatch(pin.m_name)))
                return;

            tempPins.Add(pin);
        }

        private static CompassPinType GetPinType(Minimap.PinType pinType)
        {
            return pinType switch
            {
                Minimap.PinType.Icon0 => CompassPinType.Icon0,
                Minimap.PinType.Icon1 => CompassPinType.Icon1,
                Minimap.PinType.Icon2 => CompassPinType.Icon2,
                Minimap.PinType.Icon3 => CompassPinType.Icon3,
                Minimap.PinType.Icon4 => CompassPinType.Icon4,
                Minimap.PinType.Death => CompassPinType.Death,
                Minimap.PinType.Bed => CompassPinType.Bed,
                Minimap.PinType.Shout => CompassPinType.Shout,
                Minimap.PinType.Boss => CompassPinType.Boss,
                Minimap.PinType.Player => CompassPinType.Player,
                Minimap.PinType.RandomEvent => CompassPinType.RandomEvent,
                Minimap.PinType.Ping => CompassPinType.Ping,
                Minimap.PinType.EventArea => CompassPinType.EventArea,
                Minimap.PinType.Hildir1 => CompassPinType.HildirQuest,
                Minimap.PinType.Hildir2 => CompassPinType.HildirQuest,
                Minimap.PinType.Hildir3 => CompassPinType.HildirQuest,
                Minimap.PinType.None => CompassPinType.Static,
                _ => CompassPinType.Custom,
            };
        }

        public static void UpdatePins()
        {
            if (!pinsRootObject|| !pinsRootObject.gameObject.activeInHierarchy)
                return;

            UpdatePinList();

            if (pinsList.Count != tempPins.Count)
            {
                pinsList.Do(pin => pin.Destroy());
                pinsList.Clear();

                for (int i = 0; i < tempPins.Count; i++)
                    new PinElement();
            }

            float rectWidth = ImageFileInfo.GetImageInfo(fileNameCompass).sprite.rect.width;

            for (int i = 0; i < tempPins.Count; i++)
            {
                PinElement pinElement = pinsList[i];
                Minimap.PinData pin = tempPins[i];

                pinElement.name = pin.m_name;
                pinElement.image.sprite = pin.m_icon;

                if (pinsColor.Value != Color.clear)
                    pinElement.image.color = pinsColor.Value;

                Vector3 vector = orientation.Value == OrientationType.Camera ? GameCamera.instance.transform.InverseTransformPoint(pin.m_pos) : Player.m_localPlayer.transform.InverseTransformPoint(pin.m_pos);
                float angle = Mathf.Atan2(vector.x, vector.z);

                float distance = Utils.DistanceXZ(Player.m_localPlayer.transform.position, pin.m_pos);

                float scale = Mathf.Lerp(pinsScale.Value.x, pinsScale.Value.y, (distance - pinsStyleConditions.Value.y) / (pinsStyleConditions.Value.z - pinsStyleConditions.Value.y));
                float alpha = Mathf.Lerp(pinsAlpha.Value.x, pinsAlpha.Value.y, (distance - pinsStyleConditions.Value.z) / (pinsStyleConditions.Value.w - pinsStyleConditions.Value.z));

                pinElement.rect.localScale = Vector3.one * scale;
                pinElement.rect.localPosition = Vector3.right * (rectWidth / 2) * angle / (2f * Mathf.PI);
                pinElement.image.color = new Color(pinElement.image.color.r, pinElement.image.color.g, pinElement.image.color.b, pin.m_animate ? pinsAlpha.Value.x : alpha);
                pinElement.rect.SetSiblingIndex(i);
                pinElement.checkedIcon?.SetActive(pin.m_checked);

                if (pin.m_animate)
                    pinElement.rect.localScale *= 0.9f + Mathf.Sin(Time.time * 5f) * 0.2f;
            }
        }

        [HarmonyPatch(typeof(GuiScaler), nameof(GuiScaler.UpdateScale))]
        public static class GuiScaler_UpdateScale_GetCurrentScale
        {
            public static void Postfix(GuiScaler __instance)
            {
                if (__instance.name == "LoadingGUI")
                    if (scaleFactor != (scaleFactor = __instance.m_canvasScaler.scaleFactor))
                        UpdateParentObject();
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        static class Hud_Awake_Initialize
        {
            static void Postfix() => InitializeCompass();
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
        public static class Hud_Update_Compass
        {
            public static void Postfix() => UpdateCompass();
        }
    }
}
