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
            if (!textures.TryGetValue(path, out var texture)) textures[path] = texture = Resources.Load<Texture2D>("Dynamons/Art/" + path);
            return texture;
        }
        public RawImage Art(Transform parent, string path, float x, float y, float w, float h, bool contain = false)
        {
            var texture = Texture(path);
            if (texture != null && contain)
            {
                float scale = Mathf.Min(w / texture.width, h / texture.height);
                x += (w - texture.width * scale) / 2; y += (h - texture.height * scale) / 2;
                w = texture.width * scale; h = texture.height * scale;
            }
            var image = Box(path, parent, x, y, w, h).gameObject.AddComponent<RawImage>(); image.texture = texture;
            image.raycastTarget = false; image.enabled = texture != null; return image;
        }
        public Text Label(Transform parent, string content, float x, float y, float w, float h, int size = 14, Color? color = null, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var text = Box(content, parent, x, y, w, h).gameObject.AddComponent<Text>();
            text.font = font; text.text = content; text.fontSize = size; text.alignment = alignment; text.color = color ?? Color.white;
            text.raycastTarget = false; text.supportRichText = false; return text;
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
