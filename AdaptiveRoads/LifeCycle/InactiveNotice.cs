namespace AdaptiveRoads.LifeCycle {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using ColossalFramework.Plugins;
    using ColossalFramework.UI;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    // information-only notice for ANR disabled before startup.
    // No Harmony, normal ANR lifecycle, plugin setters, or external UI dependencies.
    internal sealed class InactiveNotice : MonoBehaviour {
        private const string SessionKey = "AdaptiveNetworksRemain.InactiveNotice.Shown";
        private static bool reportedFailure_;
        private bool pushed_, closing_, finishing_;
        private Scene scene_;
        private int viewWidth_, viewHeight_;
        private UIView view_;
        private UIPanel panel_, header_, separator_, frame_, viewport_, content_;
        private UIButton ok_;
        private UILabel name_, subtitle_;
        private UISprite icon_;
        private UIScrollbar scroll_;
        private UIScrollbar leftScroll_;
        private UIPanel leftTrack_, leftThumb_;
        private bool syncingScrolls_;
        private UIFont regular_, semibold_;
        private UITextureAtlas atlas_;
        private Texture2D whiteTexture_, iconTexture_, atlasTexture_;
        private Material material_;
        private readonly List<string> paragraphs_ = new List<string>();

        // Called by InactiveNoticeGate.Update(), already on the Unity main thread.
        // Return true only when this notice owns an actually pushed modal.
        internal static bool TryOpen(bool directLoadInterrupted, UserMod owner,
            bool directLoadUnverified) {

            GameObject go = null;
            try {
                if (Equals(AppDomain.CurrentDomain.GetData(SessionKey), true)) return true;
                if (ConflictGuard.State != ConflictGuard.Decision.Pending) return false;
                if (SceneManager.GetActiveScene().name != "IntroScreen" &&
                    SceneManager.GetActiveScene().name != "Startup") return false;
                UIView view = UIView.GetAView();
                if (view == null || !UIView.isVisible || view.defaultFont == null
                    || UIView.HasModalInput() || UIView.GetModalComponent() != null) return false;

                go = new GameObject("ANR Inactive Notice");
                UnityEngine.Object.DontDestroyOnLoad(go);
                var notice = go.AddComponent<InactiveNotice>();
                var tm = ConflictGuardDependency.Status.Unknown;
                var otherAN = ConflictGuard.ANCompetitorPresence.None;
                if (owner != null) {
                    try {
                        if (PluginManager.exists) {
                            var plugins = PluginManager.instance.GetPluginsInfo().ToArray();
                            tm = ConflictGuardDependency.Inspect(plugins, owner);
                            otherAN = ConflictGuard.InspectANCompetitor(plugins, owner);
                        }
                    } catch (Exception ex) {
                        Debug.LogWarning("[ANR InactiveNotice] Read-only catalog status unavailable: " +
                            ex.GetType().Name);
                    }
                }
                notice.Open(view, directLoadInterrupted, directLoadUnverified, tm, otherAN);
                if (!notice.pushed_) throw new InvalidOperationException("Inactive modal not acquired");
                AppDomain.CurrentDomain.SetData(SessionKey, true);
                return true;
            } catch (Exception ex) {
                if (!reportedFailure_) {
                    reportedFailure_ = true;
                    Debug.LogWarning("[ANR InactiveNotice] Open unavailable: " + ex.GetType().Name);
                }
                if (go != null) UnityEngine.Object.Destroy(go);
                return false;
            }
        }

        private void Open(UIView view, bool directLoadInterrupted,
            bool directLoadUnverified, ConflictGuardDependency.Status tm,
            ConflictGuard.ANCompetitorPresence otherAN) {
            view_ = view;
            scene_ = SceneManager.GetActiveScene();
            regular_ = FindFont("OpenSans-Regular", view.defaultFont);
            semibold_ = FindFont("OpenSans-Semibold", view.defaultFont);
            if (regular_ == null || semibold_ == null)
                throw new InvalidOperationException("No usable UI font");

            string fullName = UserMod.GuardFullName;
            paragraphs_.Add("{" + fullName + "} is disabled in Content Manager." +
                (directLoadInterrupted ? "\n" + ConflictGuardText.AutoLoadStoppedLine : ""));
            if (tm != ConflictGuardDependency.Status.Unknown || otherAN != ConflictGuard.ANCompetitorPresence.None) {
                if (otherAN == ConflictGuard.ANCompetitorPresence.Enabled)
                    paragraphs_.Add("Another version of Adaptive Networks is enabled. ANR is disabled, but its DLL may still be loaded. Exclude one of the Adaptive Networks versions before restarting.");
                else if (otherAN == ConflictGuard.ANCompetitorPresence.IncludedDisabled)
                    paragraphs_.Add("Another version of Adaptive Networks is included but disabled. Its AdaptiveRoads.dll may still collide with ANR. Exclude one of the competing versions before restarting.");
                if (tm == ConflictGuardDependency.Status.Active)
                    paragraphs_.Add("{TM:PE is enabled} in Content Manager (API connection not verified).");
                else if (tm == ConflictGuardDependency.Status.Disabled)
                    paragraphs_.Add("{TM:PE is disabled} in Content Manager.");
                else if (tm == ConflictGuardDependency.Status.NotDetected)
                    paragraphs_.Add("{TM:PE was not detected.} Check your TM:PE subscription, SKYVE playset and Content Manager settings.");
                else
                    paragraphs_.Add("TM:PE status could not be verified. Check Content Manager, your TM:PE subscription or SKYVE playset.");
            }
            paragraphs_.Add(tm == ConflictGuardDependency.Status.Disabled
                ? "{Don't panic!} Go to Content Manager and enable both {" + fullName + "} and {TM:PE}!"
                : "{Don't panic!} Go to Content Manager and enable {" + fullName + "} easily!");
            paragraphs_.Add("{Quit to desktop.}");
            paragraphs_.Add("Make sure no other version of Adaptive Networks is loaded, then restart the game.");
            bool assetEditorRequested = ConflictGuard.IsEditorDirectLoadRequested();

            paragraphs_.Add(assetEditorRequested
                ? "{Do not save your session!}"
                : "{Do not save your city now!}");

            CreateAtlas(view);
            panel_ = (UIPanel)view.AddUIComponent(typeof(UIPanel));
            panel_.name = "ANR_InactiveNotice_Panel";
            panel_.isVisible = false;
            Paint(panel_, new Color32(58, 57, 57, 255));
            panel_.isInteractive = true;

            header_ = Box(panel_, new Color32(41, 41, 41, 255));
            separator_ = Box(panel_, new Color32(119, 119, 119, 255));
            icon_ = header_.AddUIComponent<UISprite>();
            icon_.atlas = atlas_;
            icon_.spriteName = "ANRIcon";
            icon_.size = new Vector2(40f, 40f);
            icon_.relativePosition = new Vector3(8f, 8f);
            name_ = Label(header_, semibold_, 25f, new Color32(255, 255, 255, 255));
            name_.text = (UserMod.GuardShortName == "ANR Alpha" ? "Remain Alpha" : "Remain") + ":";
            subtitle_ = Label(header_, semibold_, 21f, new Color32(248, 247, 247, 255));
            subtitle_.text = tm == ConflictGuardDependency.Status.Disabled
                ? "Not Enabled | TM:PE Disabled"
                : tm == ConflictGuardDependency.Status.NotDetected
                    ? "Not Enabled | TM:PE Not Found"
                    : "Not Enabled";
            frame_ = Box(panel_, new Color32(41, 41, 41, 255));
            viewport_ = Box(frame_, new Color32(52, 51, 51, 255));
            viewport_.clipChildren = true;
            viewport_.eventMouseWheel += Wheel;
            scroll_ = frame_.AddUIComponent<UIScrollbar>();
            scroll_.orientation = UIOrientation.Vertical;
            scroll_.autoHide = false;
            scroll_.autoDisableButtons = false;
            scroll_.incrementAmount = 24f;
            scroll_.trackObject = Box(scroll_, new Color32(41, 41, 41, 255));
            scroll_.thumbObject = Box(scroll_.trackObject, new Color32(41, 41, 41, 255));
            leftScroll_ = frame_.AddUIComponent<UIScrollbar>();
            leftScroll_.orientation = UIOrientation.Vertical;
            leftScroll_.autoHide = false;
            leftScroll_.autoDisableButtons = false;
            leftScroll_.incrementAmount = 24f;
            leftTrack_ = Box(leftScroll_, new Color32(41, 41, 41, 255));
            leftScroll_.trackObject = leftTrack_;
            leftThumb_ = Box(leftTrack_, new Color32(41, 41, 41, 255));
            leftScroll_.thumbObject = leftThumb_;
            scroll_.eventValueChanged += Scrolled;
            leftScroll_.eventValueChanged += Scrolled;

            var buttonBorder = Box(panel_, new Color32(58, 57, 57, 255));
            buttonBorder.name = "OK border";
            ok_ = buttonBorder.AddUIComponent<UIButton>();
            ok_.atlas = atlas_;
            ok_.normalBgSprite = ok_.hoveredBgSprite = ok_.pressedBgSprite = "White";
            ok_.color = new Color32(52, 51, 51, 255);
            ok_.hoveredColor = new Color32(72, 71, 71, 255);
            ok_.pressedColor = new Color32(41, 41, 41, 255);
            ok_.focusedColor = new Color32(52, 51, 51, 255);
            ok_.font = semibold_;
            ok_.textScale = 16f / semibold_.size;
            ok_.textColor = Color.white;
            ok_.text = "OK";
            ok_.isInteractive = true;
            ok_.eventClicked += Acknowledge;

            Layout();
            if (ConflictGuard.State != ConflictGuard.Decision.Pending
                || !SceneManager.GetActiveScene().Equals(scene_)
                || UIView.HasModalInput() || UIView.GetModalComponent() != null)
                throw new InvalidOperationException("Main Menu is no longer available");

            panel_.isVisible = true;
            panel_.BringToFront();
            UIView.PushModal(panel_, component => { pushed_ = false; closing_ = true; });
            pushed_ = true;
            SceneManager.activeSceneChanged += SceneChanged;
            if (directLoadUnverified)
                Debug.LogWarning("[ANR InactiveNotice] Direct Load stop not confirmed; standard K40 notice shown; stopState=" +
                    ConflictGuard.AutoLoadStopState);
            try { ok_.Focus(); } catch { }
            Debug.Log("[ANR InactiveNotice] Modal opened in IntroScreen; ANR remains disabled.");
        }

        private static UIFont FindFont(string name, UIFont fallback) {
            foreach (var font in Resources.FindObjectsOfTypeAll<UIFont>())
                if (font != null && font.name == name && font.isValid && font.size > 0) return font;
            return fallback != null && fallback.isValid && fallback.size > 0 ? fallback : null;
        }

        private void CreateAtlas(UIView view) {
            whiteTexture_ = new Texture2D(16, 16, TextureFormat.ARGB32, false);
            var whitePixels = new Color[16 * 16];
            for (int i = 0; i < whitePixels.Length; i++)
                whitePixels[i] = Color.white;
            whiteTexture_.SetPixels(whitePixels);
            whiteTexture_.Apply();
            whiteTexture_.name = "White";
            iconTexture_ = TryLoadOwnIcon();
            atlas_ = ScriptableObject.CreateInstance<UITextureAtlas>();
            material_ = new Material(view.defaultAtlas.material);
            atlasTexture_ = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            material_.mainTexture = atlasTexture_;
            atlas_.material = material_;
            atlas_.AddTextures(new[] { whiteTexture_, iconTexture_ });
            atlas_.material.mainTexture.filterMode = FilterMode.Point;
        }

        private static Texture2D TryLoadOwnIcon() {
            try {
                if (!PluginManager.exists) return null;
                foreach (var p in PluginManager.instance.GetPluginsInfo()) {
                    if (p == null || !p.instanceCreated || String.IsNullOrEmpty(p.modPath)
                        || !p.ContainsAssembly(typeof(UserMod).Assembly)) continue;
                    string path = Path.Combine(p.modPath, "uui_ar.png");
                    if (!File.Exists(path))
                        path = Path.Combine(Path.Combine(p.modPath, "Resources"), "uui_ar.png");
                    if (!File.Exists(path)) return null;
                    var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (texture.LoadImage(File.ReadAllBytes(path))
                        && texture.width == 40 && texture.height == 40) {
                        texture.name = "ANRIcon";
                        return texture;
                    }
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
            } catch (Exception) { /* Use title without an icon. */ }
            return null;
        }

        private void Paint(UIPanel panel, Color32 color) {
            panel.atlas = atlas_;
            panel.backgroundSprite = "White";
            panel.color = color;
        }
        private UIPanel Box(UIComponent parent, Color32 color) {
            var box = parent.AddUIComponent<UIPanel>();
            Paint(box, color);
            return box;
        }
        private static UILabel Label(UIComponent parent, UIFont font, float points, Color32 color) {
            var label = parent.AddUIComponent<UILabel>();
            label.font = font;
            label.textScale = points / font.size;
            label.autoSize = false;
            label.autoHeight = false;
            label.wordWrap = false;
            label.processMarkup = false;
            label.textColor = color;
            label.padding = new RectOffset();
            return label;
        }
        private static Vector2 Measure(UIFont font, float points, string text, float width, bool wrap) {
            var renderer = font.ObtainRenderer();
            try {
                renderer.textScale = points / font.size;
                renderer.pixelRatio = 1f;
                renderer.processMarkup = false;
                renderer.multiLine = true;
                renderer.wordWrap = wrap;
                renderer.maxSize = new Vector2(width, 10000f);
                return renderer.MeasureString(text);
            } finally { renderer.Dispose(); }
        }

        private void Layout() {
            viewWidth_ = view_.fixedWidth;
            viewHeight_ = view_.fixedHeight;
            float startX = 80f;
            float nameWidth = Measure(semibold_, 25f, name_.text, 10000f, false).x;
            float subtitleWidth = Measure(semibold_, 21f, subtitle_.text, 10000f, false).x;
            float subtitleGap = 1f + Measure(semibold_, 25f, " ", 10000f, false).x;
            float requiredWidth = startX + nameWidth + subtitleGap + subtitleWidth + 16f;
            float width = Math.Min(Math.Max(720f, requiredWidth), viewWidth_ - 24f);
            float maxHeight = viewHeight_ - 24f;
            if (width < 240f || maxHeight < 220f)
                throw new InvalidOperationException("UI viewport too small");

            bool stacked = nameWidth + subtitleGap + subtitleWidth > width - startX - 12f;
            float titleHeight = Math.Max(25f,
                Measure(semibold_, 25f, name_.text, 10000f, false).y + 4f);
            float subtitleHeight = Math.Max(21f,
                Measure(semibold_, 21f, subtitle_.text, 10000f, false).y + 4f);
            float headerHeight = stacked
                ? Math.Max(78f, titleHeight + subtitleHeight + 12f)
                : Math.Max(56f, titleHeight + 16f);

            header_.size = new Vector2(width, headerHeight);
            header_.relativePosition = Vector3.zero;
            icon_.relativePosition = new Vector3(35f, (headerHeight - 40f) / 2f);

            separator_.size = new Vector2(width, 1f);
            separator_.relativePosition = new Vector3(0f, headerHeight);

            float titleY = stacked ? 8f : (headerHeight - titleHeight) / 2f + 3f;
            name_.size = new Vector2(
                Math.Max(1f, Math.Min(nameWidth + 1f, width - startX - 12f)),
                titleHeight);
            name_.relativePosition = new Vector3(startX, titleY);

            float subtitleX = stacked ? startX : startX + nameWidth + subtitleGap;
            float subtitleY = stacked
                ? titleY + titleHeight + 4f
                : titleY + titleHeight - subtitleHeight;
            subtitle_.size = new Vector2(
                Math.Max(1f, Math.Min(subtitleWidth + 1f, width - subtitleX - 12f)),
                subtitleHeight);
            subtitle_.relativePosition = new Vector3(subtitleX, subtitleY);

            if (content_ != null) { content_.isVisible = false; Destroy(content_.gameObject); }
            content_ = viewport_.AddUIComponent<UIPanel>();
            content_.autoSize = false;
            float contentWidth = width - 72f;
            float contentHeight = Flow(content_, contentWidth);
            float bodyHeight = Math.Min(contentHeight + 24f, maxHeight - headerHeight - 80f);
            if (bodyHeight < 48f) throw new InvalidOperationException("No room for readable text");
            panel_.size = new Vector2(width, headerHeight + bodyHeight + 80f);
            panel_.relativePosition = new Vector3((viewWidth_ - width) / 2f,
                (viewHeight_ - panel_.height) / 2f);
            frame_.relativePosition = new Vector3(16f, headerHeight + 12f);
            frame_.size = new Vector2(width - 32f, bodyHeight);
            viewport_.relativePosition = new Vector3(13f, 1f);
            viewport_.size = new Vector2(frame_.width - 26f, bodyHeight - 2f);
            content_.size = new Vector2(contentWidth, contentHeight + 24f);
            content_.relativePosition = new Vector3(12f, 12f);

            scroll_.relativePosition = new Vector3(frame_.width - 13f, 1f);
            scroll_.size = new Vector2(12f, bodyHeight - 2f);
            scroll_.trackObject.size = scroll_.size;
            scroll_.thumbObject.width = 12f;
            scroll_.minValue = 0f;
            scroll_.maxValue = contentHeight + 24f;
            scroll_.scrollSize = viewport_.height;
            scroll_.value = 0f;

            bool needsScroll = contentHeight + 24f > viewport_.height;
            scroll_.isVisible = true;
            scroll_.isInteractive = needsScroll;

            leftScroll_.relativePosition = new Vector3(1f, 1f);
            leftScroll_.size = new Vector2(12f, bodyHeight - 2f);
            leftTrack_.relativePosition = Vector3.zero;
            leftTrack_.size = leftScroll_.size;
            leftThumb_.width = 12f;
            leftScroll_.minValue = scroll_.minValue;
            leftScroll_.maxValue = scroll_.maxValue;
            leftScroll_.scrollSize = scroll_.scrollSize;
            leftScroll_.value = scroll_.value;
            leftScroll_.isVisible = scroll_.isVisible;
            leftScroll_.isInteractive = scroll_.isInteractive;
            ok_.parent.size = new Vector2(122f, 42f);
            ok_.parent.relativePosition = new Vector3((width - 122f) / 2f, panel_.height - 54f);
            ok_.relativePosition = new Vector3(1f, 1f);
            ok_.size = new Vector2(120f, 40f);
        }

        private float Flow(UIPanel container, float width) {
            float line = (float)Math.Ceiling(Math.Max(
                regular_.lineHeight * 16f / regular_.size,
                semibold_.lineHeight * 16f / semibold_.size)) + 3f;
            float y = 0f;
            foreach (string paragraph in paragraphs_) {
                float x = 0f;
                bool bold = false;
                foreach (string raw in paragraph.Replace("\n", " \n ").Split(' ')) {
                    if (raw == "\n") {
                        x = 0f;
                        y += line;
                        continue;
                    }
                    if (raw.Length == 0) continue;
                    bool wordBold = bold || raw.IndexOf('{') >= 0;
                    if (raw.IndexOf('{') >= 0) bold = true;
                    string word = raw.Replace("{", "").Replace("}", "");
                    UIFont font = wordBold ? semibold_ : regular_;
                    float size = (float)Math.Ceiling(Measure(font, 16f, word, 10000f, false).x) + 1f;
                    if (size > width) throw new InvalidOperationException("Word exceeds readable width");
                    if (x > 0f && x + size > width) { x = 0f; y += line; }
                    var label = Label(container, font, 16f,
                        wordBold ? new Color32(255, 255, 255, 255) : new Color32(223, 230, 230, 255));
                    label.text = word;
                    label.size = new Vector2(size + 1f, line);
                    label.relativePosition = new Vector3(x, y);
                    x += size + Measure(font, 16f, " ", 10000f, false).x;
                    if (raw.IndexOf('}') >= 0) bold = false;
                }
                y += line + 12f;
            }
            return Math.Max(line, y - 12f);
        }

        private void Scrolled(UIComponent component, float value) {
            if (syncingScrolls_) return;
            if (!ReferenceEquals(component, scroll_) &&
                !ReferenceEquals(component, leftScroll_)) return;

            syncingScrolls_ = true;
            try {
                if (ReferenceEquals(component, leftScroll_)) {
                    if (scroll_ != null && scroll_.value != value)
                        scroll_.value = value;
                } else {
                    if (leftScroll_ != null && leftScroll_.value != value)
                        leftScroll_.value = value;
                }

                if (content_ != null)
                    content_.relativePosition = new Vector3(12f, 12f - value);
            } finally {
                syncingScrolls_ = false;
            }
        }

        private void Wheel(UIComponent component, UIMouseEventParameter p) {
            if (scroll_ != null && scroll_.isVisible && scroll_.isInteractive) {
                scroll_.value -= p.wheelDelta * 24f;
                p.Use();
            }
        }
        private void Acknowledge(UIComponent component, UIMouseEventParameter p) {
            if (!pushed_ || !ReferenceEquals(UIView.GetModalComponent(), panel_)) return;
            closing_ = true;
            TryFinish();
        }
        private void SceneChanged(Scene previous, Scene current) {
            if (!current.Equals(scene_)) { closing_ = true; TryFinish(); }
        }
        private void Update() {
            try {
                if (closing_) { TryFinish(); return; }
                if (view_ == null || panel_ == null
                    || !SceneManager.GetActiveScene().Equals(scene_)
                    || !panel_.isVisible) {
                    closing_ = true;
                    TryFinish();
                } else if (viewWidth_ != view_.fixedWidth || viewHeight_ != view_.fixedHeight) {
                    Layout();
                }
            } catch (Exception ex) {
                Debug.LogWarning("[ANR InactiveNotice] Update failed: " + ex.GetType().Name);
                closing_ = true;
                TryFinish();
            }
        }
        private void TryFinish() {
            if (finishing_) return;
            if (pushed_ && view_ != null
                && !ReferenceEquals(UIView.GetModalComponent(), panel_)) return;
            try {
                if (pushed_ && view_ != null) UIView.PopModal();
            } catch (Exception ex) {
                Debug.LogWarning("[ANR InactiveNotice] Modal release: " + ex.GetType().Name);
                if (view_ != null && ReferenceEquals(UIView.GetModalComponent(), panel_)) return;
            }
            pushed_ = false;
            finishing_ = true;
            SceneManager.activeSceneChanged -= SceneChanged;
            if (scroll_ != null) scroll_.eventValueChanged -= Scrolled;
            if (leftScroll_ != null) leftScroll_.eventValueChanged -= Scrolled;
            if (viewport_ != null) viewport_.eventMouseWheel -= Wheel;
            if (ok_ != null) ok_.eventClicked -= Acknowledge;
            if (panel_ != null) { panel_.isVisible = false; Destroy(panel_.gameObject); }
            if (atlas_ != null) Destroy(atlas_);
            if (material_ != null) Destroy(material_);
            if (atlasTexture_ != null) Destroy(atlasTexture_);
            if (iconTexture_ != null) Destroy(iconTexture_);
            if (whiteTexture_ != null) Destroy(whiteTexture_);
            enabled = false;
            Destroy(gameObject);
        }
        private void OnDestroy() {
            SceneManager.activeSceneChanged -= SceneChanged;
            if (!finishing_) { closing_ = true; TryFinish(); }
        }
    }
}
