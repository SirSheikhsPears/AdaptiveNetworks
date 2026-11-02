namespace AdaptiveRoads.LifeCycle {
    using System;
    using ColossalFramework.Plugins;
    using ICities;

    // Informational startup catalog inspection; never binds or invokes TMPE.API.
    internal static class ConflictGuardDependency {
        internal enum Status { Unknown, NotDetected, Disabled, Active, ConnectionProblem }
        internal static bool IsWarning(Status status) {
            return status == Status.NotDetected || status == Status.Disabled || status == Status.ConnectionProblem;
        }

        internal static Status Inspect(PluginManager.PluginInfo[] plugins, IUserMod self) {
            bool candidateUncertain = false, unidentified = false, found = false, active = false;
            foreach (var plugin in plugins) {
                if (plugin == null) continue;
                bool candidate = false;
                try {
                    bool legacyHint = false;
                    foreach (var assembly in plugin.GetAssemblies())
                        if (assembly != null && assembly.GetName().Name == "TrafficManager") { legacyHint = true; candidate = true; }
                    if (!plugin.instanceCreated) {
                        // isEnabled=false alone does not prove a deliberate disable when creation failed.
                        if (candidate) candidateUncertain = true;
                        else unidentified = true;
                        continue;
                    }
                    var instance = plugin.userModInstance as IUserMod;
                    if (instance == null) {
                        if (candidate) candidateUncertain = true;
                        else unidentified = true;
                        continue;
                    }
                    if (ReferenceEquals(instance, self)) continue;
                    // Content Manager's existing public label survives DLL/type rearrangement.
                    // Read once; no constructors, manager getters or lifecycle calls.
                    string label = instance.Name;
                    bool publicLabel = IsPublicLabel(label);
                    candidate = candidate || publicLabel;
                    if (!publicLabel) {
                        if (legacyHint) candidateUncertain = true; // Changed identity is UNKNOWN, not an API error.
                        continue;
                    }
                    if (!plugin.ContainsAssembly(instance.GetType().Assembly)) { candidateUncertain = true; continue; }
                    found = true;
                    if (plugin.isEnabled) active = true;
                } catch (Exception ex) {
                    if (candidate) candidateUncertain = true;
                    else unidentified = true;
                    ConflictGuard.Write("TMPE catalog record UNKNOWN: " + ex.GetType().Name);
                }
            }
            if (active) return Status.Active;
            if (candidateUncertain) return Status.Unknown;
            // An unrelated uncreated record cannot erase a positively identified TMPE state.
            if (found) return Status.Disabled;
            // Without any identified TMPE, incomplete records still prevent a false Not Found.
            return unidentified ? Status.Unknown : Status.NotDetected;
        }

        private static bool IsPublicLabel(string label) {
            if (String.IsNullOrEmpty(label)) return false;
            label = label.Trim();
            return label == "TM:PE" || label.StartsWith("TM:PE ", StringComparison.Ordinal)
                || label == "Traffic Manager: President Edition"
                || label.StartsWith("Traffic Manager: President Edition ", StringComparison.Ordinal);
        }
        // ConnectionProblem is retained for the approved text contract. No reliable one-shot,
        // side-effect-free API failure detector has been proved. Inspect NEVER returns it.
        // Active describes the catalog only: API health is UNKNOWN / NO ALERT, not PASS.
    }
}
