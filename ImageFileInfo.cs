using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using static Compass.Compass;

namespace Compass
{
    public class ImageFileInfo
    {
        public static readonly Dictionary<string, ImageFileInfo> images = new Dictionary<string, ImageFileInfo>(StringComparer.OrdinalIgnoreCase);

        public const string ext = "png";
        public static string filter = GetFilename("*");

        public string fileID;
        public string fileName;
        public string filePath;
        public Texture2D texture;
        public bool initialized = false;

        public GameObject gameObject;
        public Sprite sprite;
        public int spriteWidthOverride;

        public Action textureChanged;

        public ImageFileInfo(string id)
        {
            fileID = id;
            fileName = GetFilename(id);
            filePath = Path.Combine(configDirectory, fileName);
            Load();

            images[fileID] = this;
        }

        public bool Load()
        {
            if (!TryReadFileData(out byte[] imageData))
                return false;

            bool loaded = Load(imageData);
            if (loaded)
                LogInfo($"Loaded image from config folder: {fileName}");

            return loaded;
        }

        public bool Load(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0)
                return false;

            Texture2D loadedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);
            try
            {
                if (!loadedTexture.LoadImage(imageData))
                {
                    UnityEngine.Object.Destroy(loadedTexture);
                    return false;
                }
            }
            catch (Exception)
            {
                UnityEngine.Object.Destroy(loadedTexture);
                return false;
            }

            loadedTexture.wrapMode = TextureWrapMode.Clamp;

            Texture2D previousTexture = texture;
            texture = loadedTexture;
            initialized = true;

            InitSprite();
            UpdateGameObject();
            Update();
            textureChanged?.Invoke();

            if (previousTexture != null)
                UnityEngine.Object.Destroy(previousTexture);

            return true;
        }

        public bool TryReadValidFileData(out byte[] imageData)
        {
            imageData = null;
            if (!TryReadFileData(out byte[] fileData))
                return false;

            Texture2D validationTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            bool valid;
            try
            {
                valid = validationTexture.LoadImage(fileData);
            }
            catch (Exception)
            {
                valid = false;
            }
            finally
            {
                UnityEngine.Object.Destroy(validationTexture);
            }

            if (!valid)
                return false;

            imageData = fileData;
            return true;
        }

        private bool TryReadFileData(out byte[] imageData)
        {
            imageData = null;
            if (!File.Exists(filePath))
                return false;

            try
            {
                imageData = File.ReadAllBytes(filePath);
                return imageData.Length > 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Clear()
        {
            if (gameObject)
            {
                Image image = gameObject.GetComponent<Image>();
                if (image && image.sprite == sprite)
                    image.sprite = null;
            }

            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }

            if (sprite != null)
            {
                UnityEngine.Object.Destroy(sprite);
                sprite = null;
            }

            initialized = false;

            Update();
        }

        public void Update()
        {
            gameObject?.SetActive(initialized);
        }

        public ImageFileInfo SetSpriteWidth(int width)
        {
            spriteWidthOverride = width;
            InitSprite();
            return this;
        }

        public ImageFileInfo SetGameObject(GameObject gameObject)
        {
            this.gameObject = gameObject;
            return this;
        }

        public void UpdateGameObject()
        {
            if (!gameObject || !initialized)
                return;

            RectTransform rt = gameObject.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(spriteWidthOverride == 0 ? texture.width : spriteWidthOverride, texture.height);

            Image image = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
        }

        private void InitSprite()
        {
            if (sprite != null)
                UnityEngine.Object.Destroy(sprite);

            sprite = !initialized ? null : Sprite.Create(texture, new Rect(0, 0, spriteWidthOverride == 0 ? texture.width : spriteWidthOverride, texture.height), Vector2.zero);
        }

        private static string GetFilename(string filename) => Path.ChangeExtension(filename, ext);

        public static ImageFileInfo SetGameObject(string id, GameObject gameObject) => GetImageInfo(id).SetGameObject(gameObject);

        public static ImageFileInfo GetImageInfo(string id) => images.TryGetValue(id, out ImageFileInfo imageInfo) ? imageInfo : new ImageFileInfo(id);

        public static void TryClearFile(string filename)
        {
            if (!images.TryGetValue(Path.GetFileNameWithoutExtension(filename), out ImageFileInfo imageInfo))
                return;

            imageInfo.Clear();
            imageInfo.textureChanged?.Invoke();
        }

        public static bool TryLoadFile(string filename)
        {
            return images.TryGetValue(Path.GetFileNameWithoutExtension(filename), out ImageFileInfo imageInfo) && imageInfo.Load();
        }
    }
}
