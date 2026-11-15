namespace AdaptiveRoads.LifeCycle {
    using AdaptiveRoads.Manager;
    using AdaptiveRoads.Patches;
    using AdaptiveRoads.UI.RoadEditor;
    using CitiesHarmony.API;
    using ICities;
    using KianCommons;
    using KianCommons.Plugins;
    using System;
    using System.Diagnostics;
    using UnityEngine.SceneManagement;
    using static KianCommons.ReflectionHelpers;
    using AdaptiveRoads.Util;
    using AdaptiveRoads.UI.Tool;
    using AdaptiveRoads.NSInterface;
    using KianCommons.Serialization;
    using UnityEngine;
    using AdaptiveRoads.Patches.RoadEditor.model;
    using static AdaptiveRoads.Util.Shortcuts;
    using ColossalFramework.Plugins;
    using AdaptiveRoads.UI.VBSTool;
    using AdaptiveRoads.UI;
    using System.Linq;
    using TrafficManager.API.Notifier;

    public static class LifeCycle {
        public static string HARMONY_ID = "CS.Kian.AdaptiveRoads";
        public static string HARMONY_ID_MANUAL = "CS.Kian.AdaptiveRoads.Manual";

        public static SimulationManager.UpdateMode UpdateMode => SimulationManager.instance.m_metaData.m_updateMode;
        public static LoadMode Mode => (LoadMode)UpdateMode;
        public static string Scene => SceneManager.GetActiveScene().name;

        public static bool bHotReload = false;
        // Separate from Guard admission and Enable/NSC registration; true only after Load succeeds.
        internal static bool LevelReady { get; private set; }

        // Only the TMPE subscription and its simulation callbacks share this lock.
        internal static readonly object TMPESyncRoot = new object();
        static INotifier tmpeNotifier_;
        static Action tmpeLevelLoadedHandler_;
        static object tmpeLifetime_;

        static void SetTMPENotifier(INotifier notifier) {
            lock (TMPESyncRoot) {
                if (tmpeLifetime_ != null && ReferenceEquals(tmpeNotifier_, notifier)) return;
                DetachTMPENotifier();
                var lifetime = new object();
                tmpeLifetime_ = lifetime;
                tmpeNotifier_ = notifier;
                if (notifier == null) return;
                Action handler = () => {
                    lock (TMPESyncRoot) {
                        if (ReferenceEquals(tmpeLifetime_, lifetime) && ReferenceEquals(tmpeNotifier_, notifier))
                            OnTMPELoaded();
                    }
                };
                tmpeLevelLoadedHandler_ = handler;
                try {
                    notifier.EventLevelLoaded += handler;
                } catch {
                    DetachTMPENotifier();
                    throw;
                }
            }
        }

        static void DetachTMPENotifier() {
            // Caller holds TMPESyncRoot. Invalidate before touching either publisher.
            var notifier = tmpeNotifier_;
            var handler = tmpeLevelLoadedHandler_;
            tmpeLifetime_ = null;
            tmpeNotifier_ = null;
            tmpeLevelLoadedHandler_ = null;
            tmpeLoaded_ = false;
            try {
                if (notifier != null && handler != null) notifier.EventLevelLoaded -= handler;
            } finally {
                var manager = NetworkExtensionManager.RawInstance;
                // A destroyed Unity wrapper can still own a managed delegate.
                if (!ReferenceEquals(manager, null)) manager.DetachTMPE();
            }
        }

        static void EndTMPELifetime() {
            lock (TMPESyncRoot) {
                loaded_ = false;
                LevelReady = false;
                try { DetachTMPENotifier(); }
                catch (Exception ex) { Log.Exception(ex); }
            }
        }

        // Callers hold TMPESyncRoot; the token identifies this publisher binding.
        internal static bool TryGetTMPEContext(out object lifetime, out INotifier notifier) {
            lifetime = tmpeLifetime_;
            notifier = tmpeNotifier_;
            return IsCurrentTMPEContext(lifetime, notifier);
        }

        internal static bool IsCurrentTMPEContext(object lifetime, INotifier notifier) {
            return ConflictGuard.CanRun && loaded_ && LevelReady && lifetime != null && notifier != null &&
                ReferenceEquals(tmpeLifetime_, lifetime) && ReferenceEquals(tmpeNotifier_, notifier);
        }

        public static void Enable() {
            if (!ConflictGuard.CanRun) return;
            try {
#if DEBUG
                Log.Debug("Testing StackTrace:\n" + new StackTrace(true).ToString(), copyToGameLog: false);
                var ANPlugin = PluginUtil.GetAdaptiveRoads();
#endif

                KianCommons.UI.TextureUtil.EmbededResources = false;
                Log.ReadCommandLineArgs();

                HarmonyHelper.EnsureHarmonyInstalled();
                ANImplementation.Install();

                //LoadingManager.instance.m_simulationDataReady += SimulationDataReady; // load/update data
                LoadingManager.instance.m_levelPreLoaded += Preload;

                if (LoadingManager.instance.m_loadingComplete)
                    HotReload();

                PluginManager.instance.eventPluginsStateChanged += HotReload3rdparty;
                PluginManager.instance.eventPluginsChanged += HotReload3rdparty;
#if FAST_TEST_HARMONY
                HarmonyHelper.DoOnHarmonyReady(() => {
                    HarmonyUtil.InstallHarmony(HARMONY_ID);
                    Process.GetCurrentProcess().Kill();
                });
#endif
                //Test();
                ConflictGuard.MarkBootstrapCompleted();
            } catch (Exception ex) {
                EndTMPELifetime();
                Log.Exception(ex);
            }
        }

#if DEBUG
        static void Test() {
            return;
            LogCalled();
            byte[] data;
            {
                var man = NetworkExtensionManager.CreateNew();
                man.LaneBuffer[1].m_flags = NetLaneExt.Flags.Custom0;
                Log.Debug("Serialize lane flags:" + man.LaneBuffer[1].m_flags);
                var s = SimpleDataSerializer.Writer(new Version(1, 1), 100);
                man.Serialize(s);
                data = s.GetBytes();
            }

            {
                var man = NetworkExtensionManager.CreateNew();
                Log.Debug("Before Deserialize lane flags :" + man.LaneBuffer[1].m_flags);
                var s = SimpleDataSerializer.Reader(data);
                man.DeserializeImp(s);
                Log.Debug("After Deserialize lane flags :" + man.LaneBuffer[1].m_flags);
            }
        }
#endif

        public static void HotReload3rdparty() {
            if (!ConflictGuard.CanRun) return;
            if (loaded_) {
                // check for loaded_ to avoid detecting AN hotreload as 3rdparty hotreload.
                Log.Called();
                try {
                    SetTMPENotifier(PluginUtil.GetTrafficManager().IsActive() ? TMPENotifier : null);
                    OnTMPELoaded();
                } catch (Exception ex) {
                    EndTMPELifetime();
                    Log.Exception(ex);
                }
            }
        }

        public static void HotReload() {
            if (!ConflictGuard.CanRun) return;
            try {
                LogCalled();
                bHotReload = true;
                Preload();
                AssetDataExtension.HotReload();
                //SimulationDataReady();
                NetworkExtensionManager.Instance.HotReload();
                Load();
                RoadEditorUtils.RefreshRoadEditor();
            } catch {
                EndTMPELifetime();
                throw;
            }
        }

        public static void Disable() {
            if (!ConflictGuard.CanCleanup) return;
            EndTMPELifetime();
            Log.Buffered = false;
            ANImplementation.Uninstall();

            //LoadingManager.instance.m_simulationDataReady -= SimulationDataReady;
            LoadingManager.instance.m_levelPreLoaded -= Preload;
            Unload(); // in case of hot unload
            Exit();
#if FAST_TEST_HARMONY
            HarmonyUtil.UninstallHarmony(HARMONY_ID);
#endif
        }

        static bool preloadPatchesApplied_ = false;
        public static void Preload() {
            if (!ConflictGuard.CanRun) return;
            try {
                Log.Info("LifeCycle.Preload() called");
                PluginUtil.LogPlugins();
                TrackManager.Ensure();
                if (!preloadPatchesApplied_) {
                    HarmonyUtil.InstallHarmony<PreloadPatchAttribute>(HARMONY_ID_MANUAL);
                    preloadPatchesApplied_ = true;
                }
                SetTMPENotifier(TMPENotifier);
            } catch (Exception ex) {
                EndTMPELifetime();
                Log.Exception(ex);
            }
        }

        static bool tmpeLoaded_ = false;
        static bool loaded_ = false;
        public static void OnTMPELoaded() {
            if (!ConflictGuard.CanRun) return;
            lock (TMPESyncRoot) {
                if (tmpeLifetime_ == null || tmpeNotifier_ == null) return;
                tmpeLoaded_ = true;
                if (loaded_ && LevelReady) {
                    // If TMPE was loaded last, update after ANR initialization completed.
                    NetworkExtensionManager.OnTMPELoaded();
                }
            }
        }

        public static void Load() {
            if (!ConflictGuard.CanRun) return;
            LevelReady = false;
            try {
                SetTMPENotifier(TMPENotifier);
                Log.Info("LifeCycle.Load() called");
                Log.Debug("testing stack trace:\n" + Environment.StackTrace, false);
                Log.Info($"Scene={Scene} LoadMode={Mode}");

                loaded_ = true;
                _ = NetworkExtensionManager.Instance;

                Log.Info($"Scene={Scene} LoadMode={Mode}");
                if(Scene != "AssetEditor") {
                    Log.Info("Applying in game patches");
                    HarmonyUtil.InstallHarmony(HARMONY_ID, required: typeof(InGamePatchAttribute), forbidden: typeof(PreloadPatchAttribute));
                } else {
                    Log.Info("Applying all patches");
                    HarmonyUtil.InstallHarmony(HARMONY_ID, forbidden:typeof(PreloadPatchAttribute));
                    HintBox.Create();
                    VBSTool.Create().enabled = false;
                    ANWhatsNew.Instance.Regsiter();
                }

                NetInfoExtionsion.Ensure_EditedNetInfos(recalculate:true);


                ARTool.Create().enabled = false;

#if DEBUG
                const bool testPWValues = false;
                if (testPWValues) {
                    UI.Debug.PWSelector.Create();
                    UI.Debug.PWModifier.Create();
                }
#endif

                Log.Flush();
                Log.Succeeded();
                lock (TMPESyncRoot) {
                    LevelReady = true;
                    if (tmpeLoaded_) NetworkExtensionManager.OnTMPELoaded();
                }
            } catch (Exception ex) {
                EndTMPELifetime();
                Log.Exception(ex);
            }
        }

        public static void Unload() {
            if (!ConflictGuard.CanCleanup) return;
            EndTMPELifetime();
            try {
                LogCalled();
#if DEBUG
                try { UI.Debug.PWSelector.Release(); }
                catch (Exception ex) { ConflictGuard.Write("Unload.PWSelector.Release failed: " + ex.ToString()); }
                try { UI.Debug.PWModifier.Release(); }
                catch (Exception ex) { ConflictGuard.Write("Unload.PWModifier.Release failed: " + ex.ToString()); }
#endif
                VBSTool.Release();
                ARTool.Release();
                HintBox.Release();
                HarmonyUtil.UninstallHarmony(HARMONY_ID);
                ImportScalePatch.Release();
            }catch(Exception ex) {
                Log.Exception(ex);
            } finally {
                var manager = NetworkExtensionManager.RawInstance;
                if (!ReferenceEquals(manager, null)) {
                    try { manager.OnUnload(); }
                    catch (Exception ex) { Log.Exception(ex); }
                }
            }
        }

        public static void Exit() {
            if (!ConflictGuard.CanCleanup) return;
            EndTMPELifetime();
            Log.Buffered = false;
            Log.Info("LifeCycle.Exit() called");
            PluginManager.instance.eventPluginsStateChanged -= HotReload3rdparty;
            PluginManager.instance.eventPluginsChanged -= HotReload3rdparty;
            HarmonyUtil.UninstallHarmony(HARMONY_ID_MANUAL);
            preloadPatchesApplied_ = false;
            if (TrackManager.exists) {
                var manager = TrackManager.instance;
                if (manager) GameObject.Destroy(manager.gameObject);
            }
        }

    }
}
