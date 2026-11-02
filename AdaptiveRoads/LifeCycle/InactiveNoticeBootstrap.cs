
namespace AdaptiveRoads.LifeCycle {
    using System;
    using System.Threading;
    using ColossalFramework.Threading;
    using UnityEngine;

    internal static class InactiveNoticeBootstrap {
        private static int scheduled_;

        internal static void TrySchedule(UserMod self) {
            if (self == null) return;
            if (Interlocked.CompareExchange(ref scheduled_, 1, 0) != 0) return;

            try {
                var dispatcher = ThreadHelper.dispatcher;
                UnityEngine.Debug.Log("[ANR InactiveNotice] Bootstrap dispatcher=" +
                    (dispatcher == null ? "NULL" : "READY") +
                    "; onMainThread=" +
                    (dispatcher != null && Dispatcher.currentSafe == dispatcher));
                if (dispatcher == null) {
                    Interlocked.Exchange(ref scheduled_, 0);
                    return;
                }

                Action create = delegate {
                    GameObject go = null;
                    try {
                        if (ConflictGuard.State != ConflictGuard.Decision.Pending)
                            return;

                        go = new GameObject("ANR Inactive Notice Gate");
                        UnityEngine.Object.DontDestroyOnLoad(go);

                        var gate = go.AddComponent<InactiveNoticeGate>();
                        gate.Initialize(self);
                    } catch (Exception ex) {
                        if (go != null)
                            UnityEngine.Object.Destroy(go);

                        UnityEngine.Debug.LogWarning(
                            "[ANR InactiveNotice] Gate creation failed: "
                            + ex.GetType().Name);
                    }
                };
                if (Dispatcher.currentSafe == dispatcher) create();
                else dispatcher.Dispatch(create);
            } catch (Exception ex) {
                Interlocked.Exchange(ref scheduled_, 0);
                try {
                    UnityEngine.Debug.LogWarning(
                        "[ANR InactiveNotice] Dispatch unavailable: "
                        + ex.GetType().Name);
                } catch { }
            }
        }
    }
}
