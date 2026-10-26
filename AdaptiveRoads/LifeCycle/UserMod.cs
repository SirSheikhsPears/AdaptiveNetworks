namespace AdaptiveRoads.LifeCycle {
    using ICities;
    using JetBrains.Annotations;
    using System;

    public class UserMod : IUserMod {
        public static Version ModVersion => typeof(UserMod).Assembly.GetName().Version;
        public static string VersionString => ModVersion.ToString(4);
        private static string BuildConfiguration {
            get {
                var attributes = typeof(UserMod).Assembly.GetCustomAttributes(
                    typeof(System.Reflection.AssemblyConfigurationAttribute), false);
                return attributes.Length == 1
                    ? ((System.Reflection.AssemblyConfigurationAttribute)attributes[0]).Configuration
                    : "";
            }
        }
        public string Name => BuildConfiguration == "STABLE"
            ? "Adaptive Networks Remain " + VersionString
            : BuildConfiguration == "Debug" || BuildConfiguration == "ALPHA"
                ? "Adaptive Networks Remain Alpha " + VersionString
                : "Adaptive Networks " + ModVersion.ToString(2);
        public string Description => "foundation for networks with extra flexibility and variability.";

        [UsedImplicitly]
        public void OnEnabled() => LifeCycle.Enable();

        [UsedImplicitly]
        public void OnDisabled() => LifeCycle.Disable();

        [UsedImplicitly]
        public void OnSettingsUI(UIHelperBase helper) {
            UI.ModSettings.OnSettingsUI(helper as UIHelper);
        }

    }
}
