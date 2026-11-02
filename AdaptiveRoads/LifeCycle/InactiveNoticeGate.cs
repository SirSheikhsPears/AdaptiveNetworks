
namespace AdaptiveRoads.LifeCycle {
    using System;
    using ColossalFramework.Plugins;
    using ColossalFramework.UI;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    internal sealed class InactiveNoticeGate : MonoBehaviour {
        private UserMod self_;
        private float deadline_;
        private float readySince_;
        private bool attempted_;
        private float unverifiedSince_ = -1f;

        internal void Initialize(UserMod self) {
            self_ = self;
            deadline_ = Time.realtimeSinceStartup + 1800f;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (scene.name != "MainMenu" ||
                self_ == null ||
                attempted_ ||
                ConflictGuard.State != ConflictGuard.Decision.Pending)
                return;
            Debug.Log("[ANR InactiveNotice] MainMenu sceneLoaded; reason=" +
                ConflictGuard.GetStartupAutoLoadReason(self_));
            var result = ConflictGuard.TryCancelStartupAutoLoad(self_, true);
            Debug.Log("[ANR InactiveNotice] MainMenu AutoLoad result=" + result);
        }

        private void Update() {
            if (attempted_) return;

            try {
                if (self_ == null ||
                    ConflictGuard.State != ConflictGuard.Decision.Pending) {
                    Finish();
                    return;
                }

                var result = ConflictGuard.TryCancelStartupAutoLoad(self_, false);
                if (result != ConflictGuard.StartupAutoLoadResult.None)
                    Debug.Log("[ANR InactiveNotice] Update AutoLoad result=" + result);

                if (Time.realtimeSinceStartup > deadline_) {
                    Finish();
                    return;
                }

                string scene = SceneManager.GetActiveScene().name;

                if (scene == "Game" || scene == "AssetEditor") {
                    Finish();
                    return;
                }

                if (scene != "IntroScreen" && scene != "Startup") {
                    readySince_ = 0f;
                    return;
                }

                if (!LoadingManager.exists ||
                    LoadingManager.instance.m_currentlyLoading) {
                    readySince_ = 0f;
                    return;
                }

                UIView view = UIView.GetAView();
                if (view == null || !UIView.isVisible ||
                    view.defaultFont == null) {
                    readySince_ = 0f;
                    return;
                }

                if (UIView.HasModalInput() ||
                    UIView.GetModalComponent() != null) {
                    readySince_ = 0f;
                    return;
                }

                GameObject news = GameObject.Find(
                    "(Library) WhatsNewPanelPanel");

                if (news != null) {
                    UIPanel panel = news.GetComponent<UIPanel>();
                    if (panel != null && panel.isVisible) {
                        readySince_ = 0f;
                        return;
                    }
                }

                bool isEnabled;
                if (!TryReadOwnState(out isEnabled)) {
                    readySince_ = 0f;
                    return;
                }

                if (isEnabled) {
                    Finish();
                    return;
                }

                if (readySince_ == 0f) {
                    readySince_ = Time.realtimeSinceStartup;
                    return;
                }

                if (Time.realtimeSinceStartup - readySince_ < 1f)
                    return;

                // Show "broken off" only after a confirmed Direct Load stop.
                bool direct = ConflictGuard.HasDirectLoadArguments() ||
                    ConflictGuard.AutoLoadStopState != ConflictGuard.StartupAutoLoadStopState.None;
                bool interrupted = direct && ConflictGuard.StartupAutoLoadInterrupted;
                if (direct && !interrupted) {
                    if (unverifiedSince_ < 0f) unverifiedSince_ = Time.realtimeSinceStartup;
                    if (Time.realtimeSinceStartup - unverifiedSince_ < 1.5f) return;
                }
                if (InactiveNotice.TryOpen(interrupted, self_, direct && !interrupted)) {
                    attempted_ = true;
                    Finish();
                } else {
                    readySince_ = 0f;
                }
            } catch (Exception ex) {
                Debug.LogWarning(
                    "[ANR InactiveNotice] Gate failed: " +
                    ex.GetType().Name);
                Finish();
            }
        }

        private bool TryReadOwnState(out bool isEnabled) {
            isEnabled = false;
            if (!PluginManager.exists) return false;

            int matches = 0;

            foreach (var plugin in
                PluginManager.instance.GetPluginsInfo()) {

                if (plugin == null || !plugin.instanceCreated)
                    continue;

                if (!plugin.ContainsAssembly(typeof(UserMod).Assembly))
                    continue;

                if (!ReferenceEquals(plugin.userModInstance, self_))
                    continue;

                matches++;
                isEnabled = plugin.isEnabled;
            }

            return matches == 1;
        }

        private void Finish() {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            enabled = false;
            Destroy(gameObject);
        }

        private void OnDestroy() {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }
}
