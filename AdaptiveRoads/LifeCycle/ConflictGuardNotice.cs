namespace AdaptiveRoads.LifeCycle {
    extern alias UnifedUILib;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using ColossalFramework.UI;
    using ColossalFramework.Threading;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    // UI delivery only: no plugin/API scans. One attempt to create the panel; no reopening.
    internal sealed class ConflictGuardNotice : MonoBehaviour {
        private const string SessionKey = "AdaptiveNetworksRemain.ConflictGuard.NoticeClaimed";
        private bool conflict_, direct_, opened_, pushed_, closing_, finishing_, syncingScrolls_;
        private UserMod owner_;
        private float unverifiedSince_ = -1f;
        private ConflictGuard.DisableResult disabled_;
        private ConflictGuardDependency.Status tm_;
        private LoadingManager deliveryLoader_;
        private bool editorEndOfFramePending_;
        private float deadline_;
        private Scene scene_;
        private int width_, height_;
        private UIView view_;
        private UIPanel panel_, header_, separator_, frame_, viewport_, content_, leftTrack_, leftThumb_;
        private UIButton ok_;
        private UILabel title_, subtitle_;
        private UISprite icon_;
        private UIScrollbar scroll_, leftScroll_;
        private UIFont regular_, semibold_;
        private UITextureAtlas atlas_;
        private Texture2D iconTexture_, whiteTexture_, atlasTexture_;
        private Material material_;
        private ConflictGuardText.Message message_;

        internal static void Queue(UserMod self, bool conflict,
            ConflictGuard.DisableResult disabled, ConflictGuardDependency.Status tm) {
            Action create = delegate {
                GameObject go = null;
                try {
                    if (Equals(AppDomain.CurrentDomain.GetData(SessionKey), true)) return;
                    AppDomain.CurrentDomain.SetData(SessionKey, true);
                    go = new GameObject("ANR ConflictGuard Notice");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    var notice = go.AddComponent<ConflictGuardNotice>();
                    notice.owner_ = self;
                    notice.conflict_ = conflict;
                    notice.disabled_ = disabled;
                    notice.tm_ = tm;
                    notice.direct_ = ConflictGuard.HasDirectLoadArguments();
                    notice.deadline_ = Time.realtimeSinceStartup + 1800f;
                    SceneManager.activeSceneChanged += notice.SceneChanged;
                    SceneManager.sceneLoaded += notice.OnSceneLoaded;
                    notice.TryCancelDirectLoad(false); // Covers an already-created MainMenu.
                    // Early AutoLoad cancellation and UI delivery have separate readiness rules.
                    try {
                        if (LoadingManager.exists) {
                            var loader = LoadingManager.instance;
                            if (loader != null) {
                                loader.m_levelLoaded += notice.LevelLoaded;
                                notice.deliveryLoader_ = loader;
                            }
                        }
                    } catch (Exception ex) {
                        ConflictGuard.Write("editor delivery hook unavailable; readiness fallback: " + ex.GetType().Name);
                    }
                } catch (Exception ex) {
                    ConflictGuard.Write("notice setup failed; log-only: " + ex.GetType().Name);
                    if (go != null) UnityEngine.Object.Destroy(go);
                }
            };
            if (Dispatcher.currentSafe == ThreadHelper.dispatcher) create();
            else ThreadHelper.dispatcher.Dispatch(create);
        }

        // Direct-load argument matching is shared with K40 in ConflictGuard.

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (scene.name == "MainMenu" || scene.name == "IntroScreen")
                TryCancelDirectLoad(true);
        }

        private void TryCancelDirectLoad(bool mainMenuSceneLoaded) {
            if (closing_ || owner_ == null) return;
            var result = ConflictGuard.TryCancelStartupAutoLoad(owner_, mainMenuSceneLoaded);
            if (result != ConflictGuard.StartupAutoLoadResult.None ||
                ConflictGuard.AutoLoadStopState != ConflictGuard.StartupAutoLoadStopState.None)
                direct_ = true; // Includes a pending native AutoContinue without CLI args.
        }

        private void SceneChanged(Scene previous, Scene current) {
            if (!opened_) return;
            closing_ = true; // Never recreate a panel after a scene transition.
            TryFinish();
        }

        private void DetachLevelLoaded() {
            if (deliveryLoader_ != null) deliveryLoader_.m_levelLoaded -= LevelLoaded;
            deliveryLoader_ = null;
        }

        private void LevelLoaded(SimulationManager.UpdateMode mode) {
            // In the preserved loader this event precedes the asset selector's ShowModal.
            // Do not push here: the loader would subsequently push the selector above us.
            try {
                DetachLevelLoaded();
                var scene = SceneManager.GetActiveScene();
                if (opened_ || closing_ || scene.name != "AssetEditor") return;
                editorEndOfFramePending_ = true;
                ConflictGuard.Write("editor delivery scheduled; frame=" + Time.frameCount);
                StartCoroutine(DeliverEditorAtEndOfFrame(scene));
            } catch (Exception ex) {
                editorEndOfFramePending_ = false;
                ConflictGuard.Write("editor delivery hook unavailable: " + ex.GetType().Name);
            }
        }

        private System.Collections.IEnumerator DeliverEditorAtEndOfFrame(Scene expectedScene) {
            // One yield, no diagnostic loop. The selector is pushed before the loader yields.
            yield return new WaitForEndOfFrame();
            editorEndOfFramePending_ = false;
            if (!opened_ && !closing_ && SceneManager.GetActiveScene().Equals(expectedScene)) {
                ConflictGuard.Write("editor end-of-frame checkpoint; frame=" + Time.frameCount);
                Update(); // Reuse all readiness, foreign-modal and exception checks unchanged.
            }
            // First-input ordering in CS1 remains PENDING RUNTIME VERIFICATION.
        }

        private void Update() {
            try {
                if (closing_) { TryFinish(); return; }
                TryCancelDirectLoad(false); // Before loader/readiness/UI delivery checks.
                if (opened_) {
                    if (view_ == null || panel_ == null || !SceneManager.GetActiveScene().Equals(scene_)
                        || !panel_.isVisible || (LoadingManager.exists && LoadingManager.instance.m_currentlyLoading)) {
                        closing_ = true;
                        TryFinish();
                    } else if (width_ != view_.fixedWidth || height_ != view_.fixedHeight) Layout();
                    return;
                }
                if (Time.realtimeSinceStartup > deadline_) {
                    ConflictGuard.Write("notice delivery expired; log-only");
                    closing_ = true; TryFinish(); return;
                }
                if (editorEndOfFramePending_) return;
                if (!LoadingManager.exists || LoadingManager.instance.m_currentlyLoading) return;
                var scene = SceneManager.GetActiveScene();
                bool destination = (scene.name == "Game" || scene.name == "AssetEditor")
                    && LoadingManager.instance.m_loadingComplete;
                if (destination) direct_ = false;
                bool isMainMenu = scene.name == "MainMenu" || scene.name == "Startup"
                    || scene.name == "IntroScreen";
                if (!destination && !isMainMenu) return;
                if (isMainMenu && direct_) {
                    if (!ConflictGuard.StartupAutoLoadInterrupted) {
                        // Do not hide a problem indefinitely when no matching loader
                        // can be found. This fallback is diagnostic ONLY, never a
                        // claim that loading was actually stopped.
                        if (unverifiedSince_ < 0f)
                            unverifiedSince_ = Time.realtimeSinceStartup;
                        if (Time.realtimeSinceStartup - unverifiedSince_ < 1.5f) return;
                    }
                }
                if (isMainMenu && !direct_ && scene.name == "IntroScreen") return;
                var view = UIView.GetAView();
                if (view == null || !UIView.isVisible || view.defaultFont == null) return;
                if (isMainMenu) {
                    var whatsNew = GameObject.Find("(Library) WhatsNewPanelPanel");
                    if (whatsNew != null) {
                        var newsPanel = whatsNew.GetComponent<UIPanel>();
                        if (newsPanel != null && newsPanel.isVisible) return;
                    }
                }

                UIComponent modal = UIView.GetModalComponent();
                if (UIView.HasModalInput() && !(scene.name == "AssetEditor" && IsAssetSelection(modal))) return;
                Open(view, scene);
            } catch (Exception ex) {
                ConflictGuard.Write("notice failure; no retry: " + ex.ToString());
                closing_ = true;
                TryFinish();
            }
        }

        private static bool IsAssetSelection(UIComponent component) {
            // The real loader pushes these BEFORE m_loadingComplete. Never pop/hide them.
            // Ordering before the player's first selection still requires R05; no runtime PASS.
            return component != null && (component.GetComponent<LoadAssetPanel>() != null
                || component.GetComponent<AssetImporterWizard>() != null);
        }

        private void Open(UIView view, Scene scene) {
            DetachLevelLoaded();
            opened_ = true; view_ = view; scene_ = scene;
            regular_ = FindFont("OpenSans-Regular", view.defaultFont);
            semibold_ = FindFont("OpenSans-Semibold", view.defaultFont);
            if (regular_ == null || semibold_ == null) throw new InvalidOperationException("No usable UI font");
            bool inMenu = scene.name == "MainMenu" || scene.name == "Startup" ||
                scene.name == "IntroScreen";
            bool interrupted = inMenu && direct_ && ConflictGuard.StartupAutoLoadInterrupted;
            bool unverified = inMenu && direct_ && !interrupted;
            bool editor = scene.name == "AssetEditor" ||
                (direct_ && ConflictGuard.IsEditorDirectLoadRequested());
            message_ = ConflictGuardText.Create(conflict_, disabled_, tm_, editor, interrupted);
            CreateAtlas(view);
            panel_ = view.AddUIComponent(typeof(UIPanel)) as UIPanel;
            panel_.name = "ANR_ConflictGuard_Panel";
            panel_.isVisible = false;
            Paint(panel_, new Color32(58, 57, 57, 255));
            panel_.isInteractive = true;
            header_ = Box(panel_, new Color32(41, 41, 41, 255));
            separator_ = Box(panel_, new Color32(119, 119, 119, 255));
            icon_ = header_.AddUIComponent<UISprite>();
            icon_.atlas = atlas_; icon_.spriteName = "ANRIcon"; icon_.size = new Vector2(40, 40);
            icon_.relativePosition = new Vector3(8, 8);
            title_ = Label(header_, semibold_, 25f, Color.white);
            title_.wordWrap = false;
            title_.text = (UserMod.GuardShortName == "ANR Alpha" ? "Remain Alpha" : "Remain") + ":";
            subtitle_ = Label(header_, semibold_, 21f, new Color32(248, 247, 247, 255));
            subtitle_.wordWrap = false;
            subtitle_.text = message_.Title.Substring(UserMod.GuardShortName.Length + 3);
            frame_ = Box(panel_, new Color32(41, 41, 41, 255));
            viewport_ = Box(frame_, new Color32(52, 51, 51, 255));
            viewport_.clipChildren = true;
            viewport_.eventMouseWheel += Wheel;
            scroll_ = frame_.AddUIComponent<UIScrollbar>();
            scroll_.orientation = UIOrientation.Vertical;
            scroll_.autoHide = false; scroll_.autoDisableButtons = false;
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
            ok_.atlas = atlas_; ok_.normalBgSprite = ok_.hoveredBgSprite = ok_.pressedBgSprite = "White";
            ok_.color = new Color32(52, 51, 51, 255);
            ok_.hoveredColor = new Color32(72, 71, 71, 255);
            ok_.pressedColor = new Color32(41, 41, 41, 255);
            ok_.focusedColor = new Color32(52, 51, 51, 255);
            ok_.font = semibold_; ok_.textScale = 16f / semibold_.size;
            ok_.textColor = Color.white; ok_.text = "OK"; ok_.isInteractive = true;
            ok_.eventClicked += Acknowledge;
            Layout(); // All fallible resource/layout work precedes acquiring modal input.
            panel_.isVisible = true;
            panel_.BringToFront();
            UIView.PushModal(panel_, component => { pushed_ = false; closing_ = true; });
            pushed_ = true;
            if (unverified)
                ConflictGuard.Write("Direct Load stop not confirmed; standard K39 notice shown; stopState=" +
                    ConflictGuard.AutoLoadStopState);
            ok_.Focus();
            ConflictGuard.Write("modal open; scene=" + scene.name + "; awaiting OK; no simulation/autosave pause");
        }

        private static UIFont FindFont(string name, UIFont fallback) {
            foreach (var font in Resources.FindObjectsOfTypeAll<UIFont>())
                if (font != null && font.name == name && font.isValid && font.size > 0) return font;
            ConflictGuard.Write("font fallback for " + name + "; appearance not guaranteed");
            return fallback != null && fallback.isValid && fallback.size > 0 ? fallback : null;
        }

        private void CreateAtlas(UIView view) {
            string path = UnifedUILib::UnifiedUI.Helpers.UUIHelpers.GetFullPath<UserMod>("uui_ar.png");
            if (!File.Exists(path)) path = UnifedUILib::UnifiedUI.Helpers.UUIHelpers.GetFullPath<UserMod>("Resources", "uui_ar.png");
            iconTexture_ = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!iconTexture_.LoadImage(File.ReadAllBytes(path)) || iconTexture_.width != 40 || iconTexture_.height != 40)
                throw new InvalidOperationException("Original 40x40 ANR icon unavailable");
            iconTexture_.name = "ANRIcon";
            whiteTexture_ = new Texture2D(16, 16, TextureFormat.ARGB32, false);
            var whitePixels = new Color[16 * 16];
            for (int i = 0; i < whitePixels.Length; i++)
                whitePixels[i] = Color.white;
            whiteTexture_.SetPixels(whitePixels);
            whiteTexture_.Apply(); whiteTexture_.name = "White";
            atlas_ = ScriptableObject.CreateInstance<UITextureAtlas>();
            material_ = new Material(view.defaultAtlas.material);
            // A cloned material still shares its texture until replaced. Never pack into the game atlas.
            atlasTexture_ = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            material_.mainTexture = atlasTexture_;
            atlas_.material = material_;
            atlas_.AddTextures(new[] { whiteTexture_, iconTexture_ });
            atlas_.material.mainTexture.filterMode = FilterMode.Point;
        }

        private void Paint(UIPanel panel, Color32 color) {
            panel.atlas = atlas_; panel.backgroundSprite = "White"; panel.color = color;
        }
        private UIPanel Box(UIComponent parent, Color32 color) {
            var box = parent.AddUIComponent<UIPanel>(); Paint(box, color); return box;
        }
        private static UILabel Label(UIComponent parent, UIFont font, float points, Color32 color) {
            var label = parent.AddUIComponent<UILabel>();
            label.font = font; label.textScale = points / font.size;
            label.autoSize = false; label.autoHeight = false; label.wordWrap = false;
            label.processMarkup = false; label.textColor = color; label.padding = new RectOffset();
            return label;
        }
        private static Vector2 Measure(UIFont font, float points, string text, float width, bool wrap) {
            var renderer = font.ObtainRenderer();
            try {
                renderer.textScale = points / font.size; renderer.pixelRatio = 1f;
                renderer.processMarkup = false; renderer.multiLine = true; renderer.wordWrap = wrap;
                renderer.maxSize = new Vector2(width, 10000f);
                return renderer.MeasureString(text);
            } finally { renderer.Dispose(); }
        }

        private void Layout() {
            width_ = view_.fixedWidth; height_ = view_.fixedHeight;
            float nameWidth = Measure(semibold_, 25f, title_.text, 10000f, false).x;
            float subtitleWidth = Measure(semibold_, 21f, subtitle_.text, 10000f, false).x;
            float subtitleGap = 1f + Measure(semibold_, 25f, " ", 10000f, false).x;
            float requiredWidth = 80f + nameWidth + subtitleGap + subtitleWidth + 16f;
            float width = Math.Min(Math.Max(720f, requiredWidth), width_ - 24f), maxHeight = height_ - 24f;
            if (width < 240f || maxHeight < 200f) throw new InvalidOperationException("UI viewport too small for readable notice");
            float titleHeight = Math.Max(25f, Measure(semibold_, 25f, title_.text, 10000f, false).y + 4f);
            float headerHeight = Math.Max(56f, titleHeight + 16f);
            header_.size = new Vector2(width, headerHeight);
            header_.relativePosition = Vector3.zero;
            icon_.relativePosition = new Vector3(35f, (headerHeight - 40f) / 2f);
            separator_.size = new Vector2(width, 1f);
            separator_.relativePosition = new Vector3(0f, headerHeight);
            float subtitleHeight = Math.Max(21f, Measure(semibold_, 21f, subtitle_.text, 10000f, false).y + 4f);
            float titleY = (headerHeight - titleHeight) / 2f + 3f;
            title_.relativePosition = new Vector3(80f, titleY);
            title_.size = new Vector2(nameWidth + 1f, titleHeight);
            subtitle_.relativePosition = new Vector3(80f + nameWidth + subtitleGap, titleY + titleHeight - subtitleHeight);
            subtitle_.size = new Vector2(subtitleWidth + 1f, subtitleHeight);
            title_.size = new Vector2(Math.Max(1f, width - 76f), titleHeight);
            if (content_ != null) { content_.isVisible = false; Destroy(content_.gameObject); }
            content_ = viewport_.AddUIComponent<UIPanel>(); content_.autoSize = false;
            float contentWidth = width - 72f;
            float contentHeight = Flow(content_, contentWidth);
            float bodyHeight = Math.Min(contentHeight + 24f, maxHeight - headerHeight - 80f);
            if (bodyHeight < 48f) throw new InvalidOperationException("Insufficient room for notice text");
            panel_.size = new Vector2(width, headerHeight + bodyHeight + 80f);
            panel_.relativePosition = new Vector3((width_ - width) / 2f, (height_ - panel_.height) / 2f);
            frame_.relativePosition = new Vector3(16, headerHeight + 12f);
            frame_.size = new Vector2(width - 32f, bodyHeight);
            viewport_.relativePosition = new Vector3(13f, 1f);
            viewport_.size = new Vector2(frame_.width - 26f, bodyHeight - 2f);
            content_.size = new Vector2(contentWidth, contentHeight + 24f);
            content_.relativePosition = new Vector3(12, 12);
            scroll_.relativePosition = new Vector3(frame_.width - 13f, 1);
            scroll_.size = new Vector2(12, bodyHeight - 2f);
            scroll_.trackObject.size = scroll_.size;
            scroll_.thumbObject.width = 12f;
            scroll_.minValue = 0f; scroll_.maxValue = contentHeight + 24f;
            scroll_.scrollSize = viewport_.height; scroll_.value = 0f;
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
            ok_.parent.size = new Vector2(122, 42);
            ok_.parent.relativePosition = new Vector3((width - 122f) / 2f, panel_.height - 54f);
            ok_.relativePosition = new Vector3(1, 1); ok_.size = new Vector2(120, 40);
        }

        private float Flow(UIPanel container, float width) {
            float line = (float)Math.Ceiling(Math.Max(regular_.lineHeight * 16f / regular_.size,
                semibold_.lineHeight * 16f / semibold_.size)) + 3f;
            float y = 0f;
            int paragraphIndex = 0;
            foreach (string paragraph in message_.Paragraphs) {
                float x = 0f; bool bold = false;
                foreach (string raw in paragraph.Replace("\n", " \n ").Split(' ')) {
                    if (raw == "\n") { x = 0f; y += line; continue; }
                    if (raw.Length == 0) continue;
                    bool wordBold = bold || raw.IndexOf('{') >= 0;
                    if (raw.IndexOf('{') >= 0) bold = true;
                    string word = raw.Replace("{", "").Replace("}", "");
                    UIFont font = wordBold ? semibold_ : regular_;
                    int split = word.IndexOf("ANR", StringComparison.Ordinal);
                    string part1 = split < 0 ? word : word.Substring(0, split + 2);
                    string part2 = split < 0 ? null : word.Substring(split + 2);
                    float width1 = Measure(font, 16f, part1, 10000f, false).x;
                    float width2 = part2 == null ? 0f : Measure(font, 16f, part2, 10000f, false).x;
                    float size = (float)Math.Ceiling(width1 + width2 + (part2 == null ? 0f : 0.8f)) + 1f;
                    if (size > width) throw new InvalidOperationException("A word exceeds readable content width");
                    if (x > 0f && x + size > width) { x = 0f; y += line; }
                    Color32 color = wordBold ? new Color32(255, 255, 255, 255) : new Color32(223, 230, 230, 255);
                    var label = Label(container, font, 16f, color);
                    label.text = part1; label.size = new Vector2((part2 == null ? size : width1) + 1f, line);
                    label.relativePosition = new Vector3(x, y);
                    if (part2 != null) {
                        var tail = Label(container, font, 16f, color);
                        tail.text = part2; tail.size = new Vector2(width2 + 1f, line);
                        tail.relativePosition = new Vector3(x + width1 + 0.8f, y);
                    }
                    x += size + Measure(font, 16f, " ", 10000f, false).x;
                    if (raw.IndexOf('}') >= 0) bold = false;
                }
                y += line + ((paragraphIndex++ == 0 && conflict_) ? 0f : 12f);
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
            if (scroll_ != null && scroll_.isVisible && scroll_.isInteractive) { scroll_.value -= p.wheelDelta * 24f; p.Use(); }
        }
        private void Acknowledge(UIComponent component, UIMouseEventParameter p) {
            if (!pushed_ || !ReferenceEquals(UIView.GetModalComponent(), panel_)) return;
            ConflictGuard.Write("notice acknowledged by OK; decision unchanged");
            closing_ = true; TryFinish();
        }

        private void TryFinish() {
            if (finishing_) return;
            // A foreign modal above ours must close first. Keep our entry alive until then.
            // UIView.OnDestroy clears its stack; UIComponent.OnDestroy alone does not.
            if (pushed_ && view_ != null && !ReferenceEquals(UIView.GetModalComponent(), panel_)) return;
            try {
                if (pushed_ && view_ != null) UIView.PopModal(); // Never PopAllModal or a foreign top.
            } catch (Exception ex) {
                ConflictGuard.Write("modal release exception: " + ex.GetType().Name);
                // Do not destroy a component whose modal entry has not been released.
                if (view_ != null && ReferenceEquals(UIView.GetModalComponent(), panel_)) return;
            }
            pushed_ = false;
            finishing_ = true;
            DetachLevelLoaded();
            StopAllCoroutines();
            editorEndOfFramePending_ = false;
            SceneManager.activeSceneChanged -= SceneChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (ok_ != null) ok_.eventClicked -= Acknowledge;
            if (scroll_ != null) scroll_.eventValueChanged -= Scrolled;
            if (leftScroll_ != null) leftScroll_.eventValueChanged -= Scrolled;
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
            DetachLevelLoaded();
            SceneManager.activeSceneChanged -= SceneChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            // Normal destruction is exclusively through TryFinish after releasing our entry.
            if (!finishing_) { closing_ = true; TryFinish(); }
        }
    }
}
