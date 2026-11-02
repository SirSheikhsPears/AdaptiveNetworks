namespace AdaptiveRoads.LifeCycle {
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using ColossalFramework.Plugins;
    using ColossalFramework.Threading;
    using ICities;

    // ConflictGuard: one startup decision. An included competing AdaptiveRoads DLL blocks ANR,
    // even when the competing mod is disabled in Content Manager.
    internal static class ConflictGuard {
        internal enum Decision { Pending, Allowed, Blocked }
        internal enum DisableResult { NotAttempted, Disabled, Unconfirmed }
        private static volatile Decision decision_;
        private static volatile bool requested_, normalStarted_, bootstrapCompleted_;
        private static bool evaluated_, cleanupStarted_;
        private static UserMod self_;
        internal static Decision State { get { return decision_; } }
        internal static bool CanRun { get { return decision_ == Decision.Allowed && requested_; } }
        internal static bool CanCleanup { get { return normalStarted_; } }
        internal static bool BootstrapCompleted { get { return bootstrapCompleted_; } }
        internal static DisableResult DisableOutcome { get; private set; }
        internal static ConflictGuardDependency.Status DependencyStatus { get; private set; }

        internal static void RequestEnable(UserMod self) {
            // The decision, including an inconclusive inspection, is never repeated this session.
            if (evaluated_) { Write("enable already evaluated; no rescan or hot restart"); return; }
            evaluated_ = true;
            self_ = self;
            requested_ = true;
            DependencyStatus = ConflictGuardDependency.Status.Unknown;
            bool conflict = false;
            PluginManager.PluginInfo own = null;
            PluginManager.PluginInfo[] plugins = null;
            try {
                if (!PluginManager.exists) throw new InvalidOperationException("Plugin catalog unavailable");
                plugins = PluginManager.instance.GetPluginsInfo().ToArray();
                try { own = ResolveOwn(plugins); }
                catch (Exception ex) { Write("self identity unavailable for disable: " + ex.GetType().Name); }
                CurrentANCompetitor = InspectANCompetitor(plugins, self, own);
                conflict = CurrentANCompetitor != ANCompetitorPresence.None;
            } catch (Exception ex) { Write("AN inspection UNKNOWN; no conflict asserted: " + ex.GetType().Name); }

            // TM status has no path to the admission decision or the own-plugin setter.
            if (plugins != null) DependencyStatus = ConflictGuardDependency.Inspect(plugins, self);
            decision_ = conflict ? Decision.Blocked : Decision.Allowed;
            Write("decision=" + decision_ + "; TMPE=" + DependencyStatus);

            // Loading ANR alongside another AN version may cause a CLR type-identity conflict between AdaptiveRoads assemblies:
            // deserialized AssetData may resolve to ANR instead of the other active AN mod, causing a failed cast and
            // preventing restoration of NetInfo metadata for affected AN networks.
            // Disabling ANR does not unload its own AdaptiveRoads.dll.
            if (conflict) DisableOwn(own); // Set Blocked before synchronous OnDisabled.

            if (conflict || ConflictGuardDependency.IsWarning(DependencyStatus)) {
                try { ConflictGuardNotice.Queue(self, conflict, DisableOutcome, DependencyStatus); }
                catch (Exception ex) { Write("notice unavailable; log-only: " + ex.GetType().Name); }
            }
            if (!CanRun) return;
            normalStarted_ = true;
            try { EnableNormal(); }
            catch (Exception ex) { Write("normal enable threw: " + ex.GetType().Name); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EnableNormal() { LifeCycle.Enable(); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DisableNormal() { LifeCycle.Disable(); }

        internal static void MarkBootstrapCompleted() {
            // Admission is already Allowed during synchronous NSC registration.
            // This flag means Enable returned through its successful path, not level/Harmony readiness.
            bootstrapCompleted_ = true;
        }

        internal static void RequestDisable() {
            requested_ = false;
            bootstrapCompleted_ = false;
            if (!normalStarted_ || cleanupStarted_) return;
            cleanupStarted_ = true;
            try { DisableNormal(); }
            catch (Exception ex) { Write("normal disable threw: " + ex.GetType().Name); }
            finally { normalStarted_ = false; }
            // The independent notice survives the own-plugin setter. No session reset.
        }


        internal enum StartupAutoLoadReason {
            None,
            Inactive,
            Conflict,
            DependencyWarning
        }

        [Flags]
        internal enum StartupAutoLoadResult {
            None = 0,
            NativeInvokeCancelled = 1,
            SkyveInvokeCancelled = 2,
            SkyveComponentDisabled = 4
        }

        // A session-wide, MainMenu-scoped record. "Confirmed" describes the observed
        // native/Skyve Invoke states at this checkpoint, NOT a guarantee against
        // unknown loaders or an already-started level transition.
        internal enum StartupAutoLoadStopState { None, Pending, Partial, Confirmed }
        private static StartupAutoLoadStopState autoStopState_;
        internal static StartupAutoLoadStopState AutoLoadStopState { get { return autoStopState_; } }
        internal static bool StartupAutoLoadInterrupted {
            get { return autoStopState_ == StartupAutoLoadStopState.Confirmed; }
        }
        internal static StartupAutoLoadResult AutoLoadStopActions { get; private set; }
        private static MainMenu autoStopMenu_;

        internal static bool HasDirectLoadArguments() {
            if (HasSkyveAutoLoadArgument()) return true;
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg == "-continuelastsave" || arg == "--continuelastsave" ||
                    arg.StartsWith("--continuelastsave=", StringComparison.Ordinal))
                    return true;
            return false;
        }

        internal static bool IsEditorDirectLoadRequested() {
            foreach (string arg in Environment.GetCommandLineArgs()) {
                foreach (string option in new[] { "editor", "loadAsset", "newAsset" }) {
                    if (arg == "-" + option ||
                        arg.StartsWith("--" + option + "=", StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        internal static StartupAutoLoadReason GetStartupAutoLoadReason(UserMod candidate) {
            if (candidate == null) return StartupAutoLoadReason.None;

            try {
                if (decision_ == Decision.Pending) {
                    if (!PluginManager.exists)
                        return StartupAutoLoadReason.None;

                    int matches = 0;
                    bool enabled = false;

                    foreach (var plugin in PluginManager.instance.GetPluginsInfo()) {
                        if (plugin == null || !plugin.instanceCreated) continue;
                        if (!plugin.ContainsAssembly(typeof(UserMod).Assembly)) continue;
                        if (!ReferenceEquals(plugin.userModInstance, candidate)) continue;

                        matches++;
                        enabled = plugin.isEnabled;
                    }

                    return matches == 1 && !enabled
                        ? StartupAutoLoadReason.Inactive
                        : StartupAutoLoadReason.None;
                }

                if (!ReferenceEquals(candidate, self_))
                    return StartupAutoLoadReason.None;

                if (decision_ == Decision.Blocked)
                    return StartupAutoLoadReason.Conflict;

                if (decision_ == Decision.Allowed &&
                    ConflictGuardDependency.IsWarning(DependencyStatus))
                    return StartupAutoLoadReason.DependencyWarning;
            } catch (Exception) {
                // Unknown identity or state: never cancel loading.
            }

            return StartupAutoLoadReason.None;
        }

        private static bool HasSkyveAutoLoadArgument() {
            foreach (string arg in Environment.GetCommandLineArgs()) {
                foreach (string option in new[] {
                    "save", "loadSave", "game", "newGame",
                    "editor", "loadAsset", "newAsset"
                }) {
                    if (arg == "-" + option ||
                        arg.StartsWith("--" + option + "=",
                            StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        private static bool autoStopErrorLogged_;
        internal static StartupAutoLoadResult TryCancelStartupAutoLoad(
            UserMod candidate, bool mainMenuSceneLoaded) {
            StartupAutoLoadResult actions = StartupAutoLoadResult.None;
            bool inspected = false, nativeStopped = false, skyveStopped = false;
            try {
                string scene = UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().name;
                if (scene == "Game" || scene == "AssetEditor") return actions;
                MainMenu menu = UnityEngine.Object.FindObjectOfType<MainMenu>();
                if (menu == null || !menu.gameObject.activeInHierarchy) return actions;
                if (GetStartupAutoLoadReason(candidate) == StartupAutoLoadReason.None) return actions;
                bool skyveArgs = HasSkyveAutoLoadArgument();
                bool nativePending = menu.IsInvoking("AutoContinue");
                if (!ReferenceEquals(autoStopMenu_, menu)) {
                    autoStopMenu_ = menu;
                    AutoLoadStopActions = StartupAutoLoadResult.None;
                    autoStopState_ = StartupAutoLoadStopState.None;
                }
                bool intent = skyveArgs || HasDirectLoadArguments() || nativePending;
                if (!intent && AutoLoadStopActions == StartupAutoLoadResult.None) return actions;
                inspected = true;
                if (nativePending) {
                    menu.CancelInvoke("AutoContinue");
                    if (!menu.IsInvoking("AutoContinue"))
                        actions |= StartupAutoLoadResult.NativeInvokeCancelled;
                }
                nativeStopped = !menu.IsInvoking("AutoContinue");
                skyveStopped = !skyveArgs;
                if (skyveArgs) {
                    // The component may appear after MainMenu.Refresh. Until then,
                    // cancellation is NOT complete, even if native AutoContinue stopped.
                    UnityEngine.MonoBehaviour auto = null;
                    int matches = 0;
                    foreach (UnityEngine.MonoBehaviour component in
                        menu.GetComponents<UnityEngine.MonoBehaviour>()) {
                        if (component == null) continue;
                        Type type = component.GetType();
                        if (type.FullName != "SkyveMod.Util.AutoLoad" ||
                            type.Assembly.GetName().Name != "SkyveMod") continue;
                        matches++;
                        auto = component;
                    }
                    if (matches == 1 && auto != null) {
                        bool wasPending = auto.IsInvoking("Do");
                        bool wasEnabled = auto.enabled;
                        if (wasPending || wasEnabled) {
                            auto.CancelInvoke("Do");
                            if (wasPending && !auto.IsInvoking("Do"))
                                actions |= StartupAutoLoadResult.SkyveInvokeCancelled;
                            auto.enabled = false; // Also prevents Start() from scheduling Do().
                            if (wasEnabled && !auto.enabled)
                                actions |= StartupAutoLoadResult.SkyveComponentDisabled;
                        }
                        skyveStopped = !auto.enabled && !auto.IsInvoking("Do");
                    } else if (mainMenuSceneLoaded) {
                        Write("Skyve AutoLoad unresolved; matches=" + matches +
                            "; stop cannot be confirmed");
                    }
                }
            } catch (Exception ex) {
                if (!autoStopErrorLogged_) {
                    autoStopErrorLogged_ = true;
                    Write("Startup AutoLoad cancellation unavailable: " + ex.GetType().Name);
                }
            } finally {
                if (inspected) {
                    AutoLoadStopActions |= actions;
                    bool loading = false;
                    try {
                        loading = LoadingManager.exists && LoadingManager.instance.m_currentlyLoading;
                    } catch (Exception ex) {
                        loading = true; // Unknown loader state is never a confirmation.
                        Write("Loader state unavailable: " + ex.GetType().Name);
                    }
                    autoStopState_ = AutoLoadStopActions == StartupAutoLoadResult.None
                        ? StartupAutoLoadStopState.Pending
                        : nativeStopped && skyveStopped && !loading
                            ? StartupAutoLoadStopState.Confirmed
                            : StartupAutoLoadStopState.Partial;
                    if (actions != StartupAutoLoadResult.None)
                        Write("AutoLoad actions=" + actions + "; accumulated=" +
                            AutoLoadStopActions + "; stopState=" + autoStopState_);
                }
            }
            return actions;
        }
        private static PluginManager.PluginInfo ResolveOwn(PluginManager.PluginInfo[] plugins) {
            PluginManager.PluginInfo own = null;
            foreach (var plugin in plugins) {
                if (plugin == null || !plugin.instanceCreated
                    || !ReferenceEquals(plugin.userModInstance, self_)) continue;
                if (!plugin.ContainsAssembly(typeof(UserMod).Assembly) || own != null)
                    throw new InvalidOperationException("Self identity ambiguous");
                own = plugin;
            }
            if (own == null) throw new InvalidOperationException("Self identity unavailable");
            return own;
        }

        private static void DisableOwn(PluginManager.PluginInfo own) {
            DisableOutcome = DisableResult.Unconfirmed;
            PluginManager.PluginInfo target = null;
            try {
                if (own == null || normalStarted_ || decision_ != Decision.Blocked
                    || Dispatcher.currentSafe != ThreadHelper.dispatcher
                    || String.IsNullOrEmpty(PluginManager.assetStateSettingsFile)) return;
                target = ResolveOwn(PluginManager.instance.GetPluginsInfo().ToArray());
                if (!ReferenceEquals(target, own)) { target = null; return; }
                if (target.isEnabled) target.isEnabled = false; // The only plugin write; never a competitor.
            } catch (Exception ex) { Write("own disable call failed: " + ex.GetType().Name); }
            finally {
                try {
                    if (target != null && ReferenceEquals(target,
                        ResolveOwn(PluginManager.instance.GetPluginsInfo().ToArray()))
                        && !target.isEnabled && !target.isEnabledNoOverride)
                        DisableOutcome = DisableResult.Disabled;
                } catch (Exception ex) { Write("own disable readback failed: " + ex.GetType().Name); }
                Write("own disable=" + DisableOutcome + "; no disk-flush claim");
            }
        }

        // Inspect only plugins listed by the game's current catalog. Disabled included
        // mods may have no IUserMod instance, yet their AdaptiveRoads DLL can collide.
        internal enum ANCompetitorPresence { None, Enabled, IncludedDisabled }
        internal static ANCompetitorPresence CurrentANCompetitor { get; private set; }

        internal static ANCompetitorPresence InspectANCompetitor(
            PluginManager.PluginInfo[] plugins, IUserMod self,
            PluginManager.PluginInfo own = null) {
            if (plugins == null) return ANCompetitorPresence.None;
            bool disabledFound = false;
            foreach (var plugin in plugins) {
                if (plugin == null || ReferenceEquals(plugin, own)) continue;
                try {
                    if (plugin.instanceCreated && ReferenceEquals(plugin.userModInstance, self))
                        continue;
                    if (!HasANIdentity(plugin)) continue;
                    if (plugin.isEnabled) return ANCompetitorPresence.Enabled;
                    disabledFound = true;
                } catch (Exception ex) {
                    Write("AN catalog record inconclusive: " + ex.GetType().Name);
                }
            }
            return disabledFound ? ANCompetitorPresence.IncludedDisabled
                : ANCompetitorPresence.None;
        }

        private static bool HasANIdentity(PluginManager.PluginInfo plugin) {
            try {
                foreach (Assembly assembly in plugin.GetAssemblies()) {
                    if (assembly == null) continue;
                    Type type = assembly.GetType("AdaptiveRoads.LifeCycle.UserMod", false);
                    if (type != null && IsAdaptiveNetworks(type)) return true;
                }
            } catch (Exception ex) {
                Write("AN assembly identity inconclusive: " + ex.GetType().Name);
            }
            try {
                string folder = plugin.modPath;
                if (String.IsNullOrEmpty(folder)) return false;
                string dll = System.IO.Path.Combine(folder, "AdaptiveRoads.dll");
                if (!System.IO.File.Exists(dll)) return false;
                // PE manifest read only; does not load or execute the candidate DLL.
                return String.Equals(AssemblyName.GetAssemblyName(dll).Name,
                    "AdaptiveRoads", StringComparison.OrdinalIgnoreCase);
            } catch (Exception ex) {
                Write("AN file identity inconclusive: " + ex.GetType().Name);
                return false;
            }
        }
        private static bool IsAdaptiveNetworks(Type type) {
            if (type.FullName != "AdaptiveRoads.LifeCycle.UserMod"
                || !typeof(IUserMod).IsAssignableFrom(type)) return false;
            var assembly = type.Assembly;
            var lifecycle = assembly.GetType("AdaptiveRoads.LifeCycle.LifeCycle", false);
            if (lifecycle == null || assembly.GetType("AdaptiveRoads.Manager.NetworkExtensionManager", false) == null
                || assembly.GetType("AdaptiveRoads.NSInterface.ANImplementation", false) == null) return false;
            foreach (var name in new[] { "Enable", "Disable", "Preload", "Load", "Unload", "Exit" }) {
                var method = lifecycle.GetMethod(name, BindingFlags.Public | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(void)) return false;
            }
            return true;
        }

        internal static void Write(string message) {
            try { UnityEngine.Debug.Log("[ANR ConflictGuard] " + message); }
            catch { /* Logging must not change admission or cleanup. */ }
        }
    }
}
