using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dynamons
{
    internal sealed class GameUi
    {
        public const float Width = 320f, Height = 568f;
        public static readonly Color Ink = new Color32(22, 34, 49, 255), Panel = new Color32(42, 61, 83, 255), Gold = new Color32(253, 199, 80, 255), Green = new Color32(28, 113, 69, 255);
        public RectTransform Frame { get; }
        private readonly Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly AudioSource music, effects;
        private readonly CanvasScaler scaler;
        private string track;
        public GameUi(GameObject root)
        {
            if (Camera.main == null)
            {
                var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black; camera.cullingMask = 0;
            }
            var screen = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            screen.transform.SetParent(root.transform, false);
            screen.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var backdrop = screen.AddComponent<Image>(); backdrop.color = Color.black; backdrop.raycastTarget = false;
            scaler = screen.AddComponent<CanvasScaler>();
            Frame = Box("Portrait frame", screen.transform, 0, 0, 320, 568);
            Frame.anchorMin = Frame.anchorMax = Frame.pivot = new Vector2(.5f, .5f);
            Frame.anchoredPosition = Vector2.zero;
            Frame.gameObject.AddComponent<RectMask2D>();
            Resize();
            if (EventSystem.current == null)
            {
                var input = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                input.transform.SetParent(root.transform, false);
            }
            if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null) root.AddComponent<AudioListener>();
            music = root.AddComponent<AudioSource>(); music.loop = true; music.volume = .45f; music.playOnAwake = false;
            effects = root.AddComponent<AudioSource>(); effects.playOnAwake = false;
        }

        public void Resize() => scaler.scaleFactor = Mathf.Max(.01f, Mathf.Min(Screen.width / 320f, Screen.height / 568f));

        public static RectTransform Box(string name, Transform parent, float x, float y, float w, float h)
        {
            var box = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            box.SetParent(parent, false); Place(box, x, y, w, h); return box;
        }
        public static void Place(RectTransform box, float x, float y, float w, float h)
        {
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0, 1);
            box.anchoredPosition = new Vector2(x, -y); box.sizeDelta = new Vector2(w, h);
        }
        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--) { var child = parent.GetChild(i).gameObject; child.SetActive(false); UnityEngine.Object.Destroy(child); }
        }
        public static Image Fill(Transform parent, Color color, float x, float y, float w, float h, bool hit = false)
        {
            var image = Box("Panel", parent, x, y, w, h).gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = hit; return image;
        }
        private Texture2D Texture(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!textures.TryGetValue(path, out var texture))
            {
                texture = Resources.Load<Texture2D>("Dynamons/Art/" + path);
                if (texture != null) texture.wrapMode = TextureWrapMode.Clamp;
                textures[path] = texture;
            }
            return texture;
        }
        public Vector2 ArtSize(string path)
        {
            var texture = Texture(path);
            return texture != null ? new Vector2(texture.width, texture.height) : Vector2.zero;
        }
        public RawImage Art(Transform parent, string path, float x, float y, float w, float h, bool contain = false, float alpha = 1f, bool flipX = false)
        {
            var texture = Texture(path);
            if (texture != null && contain && texture.width > 0 && texture.height > 0 && w > 0 && h > 0)
            {
                float scale = Mathf.Min(w / texture.width, h / texture.height);
                x += (w - texture.width * scale) * 0.5f; y += (h - texture.height * scale) * 0.5f;
                w = texture.width * scale; h = texture.height * scale;
            }
            var image = Box(path ?? "Art", parent, x, y, w, h).gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            if (flipX) image.uvRect = new Rect(1f, 0f, -1f, 1f);
            image.raycastTarget = false;
            image.enabled = texture != null;
            return image;
        }
        public RawImage ArtFit(Transform parent, string path, float x, float y, float w, float h, float alpha = 1f)
        {
            var texture = Texture(path);
            if (texture != null && texture.width > 0 && texture.height > 0 && w > 0 && h > 0)
            {
                float scale = Mathf.Max(w / texture.width, h / texture.height);
                float dw = texture.width * scale, dh = texture.height * scale;
                return Art(parent, path, x + (w - dw) * 0.5f, y + (h - dh) * 0.5f, dw, dh, false, alpha);
            }
            return Art(parent, path, x, y, w, h, false, alpha);
        }
        public RawImage ArtCell(Transform parent, string path, int cols, int rows, int frameIdx, float x, float y, float w, float h, float alpha = 1f, float rotationDeg = 0f, bool alignBottom = false, bool flipX = false)
        {
            cols = Mathf.Max(1, cols);
            rows = Mathf.Max(1, rows);
            int total = cols * rows;
            int idx = ((frameIdx % total) + total) % total;
            int col = idx % cols;
            int row = idx / cols;
            var texture = Texture(path);
            float dx = x, dy = y, dw = w, dh = h;
            if (texture != null && texture.width > 0 && texture.height > 0 && w > 0 && h > 0)
            {
                float cellW = texture.width / (float)cols;
                float cellH = texture.height / (float)rows;
                float scale = Mathf.Min(w / cellW, h / cellH);
                dw = cellW * scale;
                dh = cellH * scale;
                dx = x + (w - dw) * 0.5f;
                dy = alignBottom ? y + (h - dh) : y + (h - dh) * 0.5f;
            }
            var box = Box(path ?? "ArtCell", parent, dx, dy, dw, dh);
            if (Mathf.Abs(rotationDeg) > 0.001f)
            {
                box.pivot = new Vector2(0.5f, 0.5f);
                box.anchoredPosition = new Vector2(dx + dw * 0.5f, -(dy + dh * 0.5f));
                box.localEulerAngles = new Vector3(0f, 0f, -rotationDeg);
            }
            var image = box.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            float uw = 1f / cols, uh = 1f / rows;
            float u = flipX ? (col + 1) * uw : col * uw;
            float v = 1f - (row + 1) * uh;
            image.uvRect = new Rect(u, v, flipX ? -uw : uw, uh);
            image.raycastTarget = false;
            image.enabled = texture != null;
            return image;
        }
        public Button ArtButton(Transform parent, string artPath, float x, float y, float w, float h, Action onClick, bool enabled = true, float alpha = 1f, string name = null)
        {
            var hit = Box(name ?? artPath ?? "ArtButton", parent, x, y, w, h).gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.interactable = enabled;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            if (!string.IsNullOrEmpty(artPath))
                Art(hit.transform, artPath, 0f, 0f, w, h, true, enabled ? alpha : alpha * 0.45f);
            return button;
        }
        public Button HitBox(Transform parent, float x, float y, float w, float h, Action onClick = null)
        {
            var hit = Fill(parent, Color.clear, x, y, w, h, true);
            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }
        public static Color HexColor(string hex, float alpha = 1f)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var color))
            {
                color.a *= Mathf.Clamp01(alpha);
                return color;
            }
            return new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }
        public Text Label(Transform parent, string content, float x, float y, float w, float h, int size = 14, Color? color = null, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var text = Box(content, parent, x, y, w, h).gameObject.AddComponent<Text>();
            text.font = font; text.text = content; text.fontSize = size; text.alignment = alignment; text.color = color ?? Color.white;
            text.raycastTarget = false; text.supportRichText = false; return text;
        }
        public InputField TextField(Transform parent, string initial, string placeholder, float x, float y, float w, float h)
        {
            var root = Box("TextField", parent, x, y, w, h);
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            var chrome = VectorCanvas.Create("TextFieldChrome", root, 0f, 0f, w, h);
            chrome.FillRoundRect(0f, 0f, w, h, 7f, HexColor("#e8b35a"));
            chrome.FillRoundRect(1.5f, 1.5f, Mathf.Max(2f, w - 3f), Mathf.Max(2f, h - 3f), 5.5f, HexColor("#151026"));
            chrome.Commit();

            float areaW = Mathf.Max(4f, w - 16f);
            float areaH = Mathf.Max(4f, h - 4f);
            var textArea = Box("TextArea", root, 8f, 2f, areaW, areaH);
            textArea.gameObject.AddComponent<RectMask2D>();

            var ph = Box("Placeholder", textArea, 0f, 0f, areaW, areaH).gameObject.AddComponent<Text>();
            ph.font = font;
            ph.fontSize = 13;
            ph.fontStyle = FontStyle.Italic;
            ph.alignment = TextAnchor.MiddleLeft;
            ph.color = new Color(0.66f, 0.63f, 0.78f, 0.65f);
            ph.horizontalOverflow = HorizontalWrapMode.Overflow;
            ph.supportRichText = false;
            ph.raycastTarget = false;
            ph.text = placeholder ?? string.Empty;

            var valueText = Box("Text", textArea, 0f, 0f, areaW, areaH).gameObject.AddComponent<Text>();
            valueText.font = font;
            valueText.fontSize = 13;
            valueText.alignment = TextAnchor.MiddleLeft;
            valueText.color = Color.white;
            valueText.horizontalOverflow = HorizontalWrapMode.Overflow;
            valueText.supportRichText = false;
            valueText.raycastTarget = false;

            var input = root.gameObject.AddComponent<InputField>();
            input.targetGraphic = hit;
            input.transition = Selectable.Transition.None;
            input.textComponent = valueText;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 64;
            input.customCaretColor = true;
            input.caretColor = Color.white;
            input.caretWidth = 2;
            input.selectionColor = new Color(0.28f, 0.56f, 1f, 0.45f);
            input.text = initial ?? string.Empty;
            return input;
        }
        public void Heading(Transform parent, FontData bitmap, string title, float y)
        {
            if (bitmap == null || title.Any(c => c != ' ' && Array.IndexOf(bitmap.chars, (int)c) < 0))
            { Label(parent, title, 5, y, 310, 32, 22, Gold); return; }
            var letters = title.Select(c => c == ' ' ? null : Texture($"fonts/{bitmap.id}/c{(int)c}")).ToArray();
            float height = 23;
            float LetterWidth(Texture2D texture) => texture == null ? 6 : height * texture.width / texture.height;
            float width = letters.Sum(texture => LetterWidth(texture) + 1);
            if (width > 302) { Label(parent, title, 5, y, 310, 32, 22, Gold); return; }
            float x = (320 - width) / 2;
            for (int i = 0; i < title.Length; i++)
            {
                var texture = letters[i]; float w = LetterWidth(texture);
                if (texture != null) Art(parent, $"fonts/{bitmap.id}/c{(int)title[i]}", x, y + 5, w, height);
                x += w + 1;
            }
        }
        public Button Button(Transform parent, string title, float x, float y, float w, float h, Action action, bool enabled = true, string art = null)
        {
            var back = Fill(parent, Green, x, y, w, h, true);
            var button = back.gameObject.AddComponent<Button>(); button.targetGraphic = back;
            button.interactable = enabled; button.onClick.AddListener(() => action());
            if (art != null && Texture(art) != null)
            { back.color = new Color(0, 0, 0, .12f); Art(back.transform, art, 0, 0, w, h, true).color = enabled ? Color.white : new Color(1, 1, 1, .55f); }
            else Label(back.transform, title, 2, 1, w - 4, h - 2, w < 70 ? 11 : 14);
            return button;
        }
        public void Sound(string key, SaveData save)
        {
            if (!save.Sound) return;
            var clip = Clip("sounds/" + key); if (clip != null) effects.PlayOneShot(clip, .7f);
        }
        public void Music(string key, SaveData save)
        {
            if (!save.Music) { music.Stop(); track = null; return; }
            if (track == key && music.isPlaying) return;
            track = key; music.clip = Clip("musics/" + key);
            if (music.clip != null) music.Play();
        }
        private AudioClip Clip(string path)
        {
            if (!clips.TryGetValue(path, out var clip)) clips[path] = clip = Resources.Load<AudioClip>("Dynamons/" + path);
            return clip;
        }
        public static string Mon(string id, string view) => $"general/mons/{id}/{view}";
        public static string Item(string id) => "items/icons/" + id;
        public static string Move(string id) => id.StartsWith("can-", StringComparison.Ordinal) ? Item("can_" + id.Substring(4)) :
            id.StartsWith("item-", StringComparison.Ordinal) ? Item(id.Substring(5)) : "session/abilities/icons/" + id;
    }

}
