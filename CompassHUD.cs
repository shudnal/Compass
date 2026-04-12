using HarmonyLib;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using TMPro;
using static Compass.Compass;
using Splatform;

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
        private const string objectGlobalMaskName = "GlobalMask";
        private const string objectMaskName = "Mask";
        private const string objectCompassName = "Compass";
        private const string objectCenterName = "Center";
        private const string objectPinsRootName = "Pins";
        private const string objectPinElementName = "PinElement";
        private const string objectPinElementCheckedName = "Checked";
        private const string objectPinElementNameName = "Name";

        private static readonly int layerUI = LayerMask.NameToLayer("UI");

        public static GameObject parentObject;
        public static GameObject compassObject;
        public static GameObject centerObject;
        public static RectTransform pinsRootObject;
        public static RectTransform pinElement;
        public static RectTransform compassTransform;

        public static Mask maskComponent;
        public static Image maskImage;

        public static float compassWidth;

        public static readonly List<Minimap.PinData> tempPins = new List<Minimap.PinData>();
        public static readonly List<PinElement> pinsList = new List<PinElement>();

        public static HashSet<string> filteredNames = new HashSet<string>();
        public static HashSet<string> filteredWildcards = new HashSet<string>();

        private static Transform AnchorTransform => orientation.Value == OrientationType.Camera ? GameCamera.instance?.transform : Player.m_localPlayer?.transform;

        public class PinElement
        {
            public string name;
            public RectTransform rect;
            public Image image;
            public GameObject checkedIcon;
            public TMP_Text text;

            public PinElement()
            {
                rect = UnityEngine.Object.Instantiate(pinElement, pinsRootObject);
                rect.gameObject.SetActive(value: true);
                
                image = rect.GetComponent<Image>();
                checkedIcon = rect.Find(objectPinElementCheckedName)?.gameObject;
                text = rect.Find(objectPinElementNameName)?.GetComponent<TMP_Text>();

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

            rt.localScale = Vector3.one * scale.Value;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);

            Texture2D compass = ImageFileInfo.GetImageInfo(fileNameCompass).texture;
            if (compass)
                rt.anchoredPosition = new Vector2(0f, -compass.height / 2) - offset.Value;
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

            // Global mask object
            GameObject mask2DObject = new GameObject(objectGlobalMaskName, typeof(RectTransform))
            {
                layer = layerUI
            };
            mask2DObject.transform.SetParent(parentObject.transform, false);
            mask2DObject.AddComponent<RectMask2D>();
            mask2DObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1600f, 100f);

            // Mask object
            GameObject maskImageObject = new GameObject(objectMaskName, typeof(RectTransform))
            {
                layer = layerUI
            };
            maskImageObject.transform.SetParent(mask2DObject.transform, false);
            mask.SetGameObject(maskImageObject);
            UpdateMaskObject();
            maskComponent = maskImageObject.AddComponent<Mask>();
            maskComponent.showMaskGraphic = false;
            maskImage = maskImageObject.GetComponent<Image>();

            // Compass object
            compassObject = new GameObject(objectCompassName, typeof(RectTransform))
            {
                layer = layerUI
            };
            compassObject.transform.SetParent(maskImageObject.transform, false);
            compass.SetGameObject(compassObject).UpdateGameObject();
            compass.textureChanged = (Action)Delegate.Combine(new Action(UpdateParentObject), new Action(UpdateCompassObject), new Action(UpdateMaskObject));

            // Center object
            centerObject = new GameObject(objectCenterName, typeof(RectTransform))
            {
                layer = layerUI
            };
            centerObject.transform.SetParent(maskImageObject.transform, false);
            center.SetGameObject(centerObject).UpdateGameObject();

            // Pins root object
            pinsRootObject = new GameObject(objectPinsRootName, typeof(RectTransform))
            {
                layer = layerUI
            }.GetComponent<RectTransform>();
            pinsRootObject.SetParent(maskImageObject.transform, false);

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

            GameObject namePin = UnityEngine.Object.Instantiate(Minimap.instance.m_pinNamePrefab.transform.Find(objectPinElementNameName).gameObject, pinElement);
            namePin.name = objectPinElementNameName;
            namePin.GetComponent<TMP_Text>().fontSizeMax = 24f;
            namePin.SetActive(false);

            UpdateParentObject();

            UpdateCenterObject();

            UpdateCompassObject();

            UpdatePinsObject();
        }

        public static void UpdateCompass()
        {
            if (!modEnabled.Value || !Player.m_localPlayer || !compassObject)
                return;

            float angle = AnchorTransform.eulerAngles.y;

            if (angle > 180)
                angle -= 360;

            angle *= -Mathf.Deg2Rad;

            compassTransform ??= compassObject.GetComponent<RectTransform>();
            compassTransform.localPosition = Vector3.right * (compassWidth / 2) * angle / (2f * Mathf.PI) - new Vector3(compassWidth * 0.125f, 0, 0);

            UpdatePins();
        }

        public static void UpdatePinTextStyle()
        {
            pinsList.Do(pin =>
            {
                if (pin.text == null)
                    return;

                pin.text.enableAutoSizing = false;
                pin.text.fontSize = pinTextSize.Value;
                pin.text.color = pinTextColor.Value;
                pin.text.fontStyle = pinTextFormat.Value;
            });
        }

        public static void DestroyCompass()
        {
            parentObject = null;
            compassObject = null;
            centerObject = null;
            pinsRootObject = null;
            pinElement = null;
            compassTransform = null;

            maskComponent = null;
            maskImage = null;

            pinsList.Do(pin => pin.Destroy());
            pinsList.Clear();

            tempPins.Clear();
        }

        private static void UpdatePinList()
        {
            tempPins.Clear();

            AddPinRange(Minimap.instance.m_pins);

            if (ShowAllPingPins())
                AddPinRange(Minimap.instance.m_pingPins);

            if (ShowAllShoutPins())
                AddPinRange(Minimap.instance.m_shoutPins);

            if (ShowAllPlayerPins())
                AddPinRange(Minimap.instance.m_playerPins);

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

                return Utils.DistanceXZ(AnchorTransform.position, y.m_pos).CompareTo(Utils.DistanceXZ(AnchorTransform.position, x.m_pos));
            }
        }

        public static bool IsShortcutDown(KeyboardShortcut shortcut) => shortcut.MainKey != KeyCode.None && ZInput.GetKey(shortcut.MainKey) && shortcut.Modifiers.All(key => ZInput.GetKey(key));

        private static bool ShowAllPlayerPins() => alwaysShowPinText.Value || IsShortcutDown(holdToAlwaysShowPlayerPin.Value);

        private static bool ShowAllShoutPins() => alwaysShowPinText.Value || IsShortcutDown(holdToAlwaysShowShouts.Value);

        private static bool ShowAllPingPins() => alwaysShowPinText.Value || IsShortcutDown(holdToAlwaysShowPings.Value);

        private static bool ShowPinText() => alwaysShowPinText.Value || IsShortcutDown(holdToShowText.Value);

        private static void AddPinRange(List<Minimap.PinData> pinList) => pinList.Do(AddPin);

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

            if (showOnlyLastDeath.Value && (pinType == CompassPinType.Death) && pin.m_pos != Game.instance.GetPlayerProfile().GetDeathPoint())
                return;

            if (!IsDynamicPinToShow(pinType))
            {
                float distance = Utils.DistanceXZ(AnchorTransform.position, pin.m_pos);
                if (distance < pinsStyleConditions.Value.x || distance > pinsStyleConditions.Value.w)
                    return;

                if (filteredNames.Contains(pin.m_name))
                    return;

                if (filteredWildcards.Any(wildcard => new WildcardPattern(wildcard).IsMatch(pin.m_name)))
                    return;
            }

            if (!tempPins.Contains(pin))
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
            if (!pinsRootObject|| !pinsRootObject.gameObject.activeInHierarchy || AnchorTransform == null)
                return;

            UpdatePinList();

            if (pinsList.Count != tempPins.Count)
            {
                pinsList.Do(pin => pin.Destroy());
                pinsList.Clear();

                for (int i = 0; i < tempPins.Count; i++)
                    new PinElement();
            }

            Rect compassRect = ImageFileInfo.GetImageInfo(fileNameCompass).sprite.rect;
            bool textIsShown = false;

            for (int i = 0; i < tempPins.Count; i++)
            {
                PinElement pinElement = pinsList[i];
                Minimap.PinData pin = tempPins[i];

                pinElement.name = pin.m_name;
                pinElement.image.sprite = pin.m_icon;

                if (pinsColor.Value != Color.clear)
                    pinElement.image.color = pinsColor.Value;

                bool isDynamicPin = IsDynamicPinToShow(pin.m_type);

                float distance = Utils.DistanceXZ(AnchorTransform.position, pin.m_pos);
                if (distance > pinsStyleConditions.Value.y && isDynamicPin)
                    distance = pinsStyleConditions.Value.y;

                float scale = Mathf.Lerp(pinsScale.Value.x, pinsScale.Value.y, (distance - pinsStyleConditions.Value.y) / (pinsStyleConditions.Value.z - pinsStyleConditions.Value.y));
                float alpha = Mathf.Lerp(pinsAlpha.Value.x, pinsAlpha.Value.y, (distance - pinsStyleConditions.Value.z) / (pinsStyleConditions.Value.w - pinsStyleConditions.Value.z));

                pinElement.rect.localScale = Vector3.one * scale;
                pinElement.rect.localPosition = Vector3.right * (compassRect.width / 2) * GetAngle(pin.m_pos) / (2f * Mathf.PI);
                pinElement.image.color = new Color(pinElement.image.color.r, pinElement.image.color.g, pinElement.image.color.b, pin.m_animate ? pinsAlpha.Value.x : alpha);
                pinElement.rect.SetSiblingIndex(i);
                pinElement.checkedIcon?.SetActive(pin.m_checked);
                if (pinElement.text != null)
                {
                    pinElement.text.gameObject.SetActive(!string.IsNullOrWhiteSpace(pin.m_name) && (isDynamicPin || ShowPinText()));
                    if (pinElement.text.isActiveAndEnabled)
                    {
                        pinElement.text.SetText(GetPinText(pin));
                        pinElement.text.enableAutoSizing = false;
                        pinElement.text.fontSize = pinTextSize.Value;
                        pinElement.text.color = pinTextColor.Value;
                        pinElement.text.fontStyle = pinTextFormat.Value;
                        pinElement.text.transform.localScale = Vector3.one / scale;
                        pinElement.text.transform.localPosition = new Vector3(0f, -compassRect.height / 2, 0f);
                        textIsShown = true;
                    }
                }

                if (pin.m_animate && !isDynamicPin)
                    pinElement.rect.localScale *= 0.9f + Mathf.Sin(Time.time * 5f) * 0.2f;
            }

            maskComponent.enabled = !textIsShown;
            maskImage.enabled = !textIsShown;
        }

        public static bool IsDynamicPinToShow(CompassPinType pinType)
        {
            return pinType == CompassPinType.Shout && ShowAllShoutPins() || pinType == CompassPinType.Player && ShowAllPlayerPins() || pinType == CompassPinType.Ping && ShowAllPingPins();
        }

        public static bool IsDynamicPinToShow(Minimap.PinType pinType) => IsDynamicPinToShow(GetPinType(pinType));

        public static float GetAngle(Vector3 position) => GetAtan2(AnchorTransform.InverseTransformPoint(new Vector3(position.x, AnchorTransform.position.y, position.z)));

        public static float GetAtan2(Vector3 vector) => Mathf.Atan2(vector.x, vector.z);

        public static string GetPinText(Minimap.PinData pin)
        {
            return string.IsNullOrEmpty(pin.m_author.m_userID) || pin.m_author == PlatformManager.DistributionPlatform.LocalUser.PlatformUserID
                ? Localization.instance.Localize(pin.m_name)
                : CensorShittyWords.FilterUGC(Localization.instance.Localize(pin.m_name), UGCType.Text, pin.m_author, 0L);
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

        [HarmonyPatch(typeof(Hud), nameof(Hud.OnDestroy))]
        public static class Hud_OnDestroy_Compass
        {
            public static void Postfix() => DestroyCompass();
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetCrouch))]
        public static class Player_SetCrouch_PreventCrouchingOnControlPress
        {
            private static bool IsCtrlDown(KeyboardShortcut shortcut) => (ZInput.GetButton("Crouch") || ZInput.GetButton("JoyCrouch")) && IsShortcutDown(shortcut);

            [HarmonyPriority(Priority.First)]
            public static bool Prefix() => !(IsCtrlDown(holdToAlwaysShowPings.Value) || IsCtrlDown(holdToAlwaysShowPlayerPin.Value) || IsCtrlDown(holdToAlwaysShowShouts.Value) || IsCtrlDown(holdToShowText.Value));
        }
    }
}
