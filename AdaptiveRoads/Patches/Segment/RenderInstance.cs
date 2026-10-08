namespace AdaptiveRoads.Patches.Segment {
    using HarmonyLib;
    using KianCommons;
    using KianCommons.Patches;
    using System;
    using System.Collections.Generic;
    using System.Reflection;

    [InGamePatch]
    [HarmonyPatch]
    public static class RenderInstance {
        // private void NetSegment.RenderInstance(RenderManager.CameraInfo cameraInfo, ushort segmentID, int layerMask, NetInfo info, ref RenderManager.Instance data)
        public static MethodBase TargetMethod() {
            var types = new Type[] {
                typeof(RenderManager.CameraInfo), typeof(ushort), typeof(int),
                typeof(NetInfo), typeof(RenderManager.Instance).MakeByRefType()
            };
            return typeof(NetSegment).GetMethod("RenderInstance", BindingFlags.NonPublic | BindingFlags.Instance, null, types, null);
        }

        public static void Prefix(ushort segmentID) {
            CheckSegmentFlagsCommons.s_currentSegmentID = segmentID;
        }
    } // end class

    /// <summary>
    /// Patches NetSegment.RenderSegments to apply AR extension flag checks after the game's
    /// CheckFlags call. Works together with RenderInstance which stores the segmentID into
    /// CheckSegmentFlagsCommons.s_currentSegmentID before calling RenderSegments.
    /// </summary>
    [InGamePatch]
    [HarmonyPatch]
    public static class RenderSegmentsPatch {
        // private void NetSegment.RenderSegments(CameraInfo cameraInfo, NetInfo info, RenderManager.Instance data, float wOffsetParent, NetManager netManager)
        public static MethodBase TargetMethod() {
            var types = new Type[] {
                typeof(RenderManager.CameraInfo), typeof(NetInfo), typeof(RenderManager.Instance),
                typeof(float), typeof(NetManager)
            };
            return typeof(NetSegment).GetMethod("RenderSegments", BindingFlags.NonPublic | BindingFlags.Instance, null, types, null);
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) {
            try {
                var codes = TranspilerUtils.ToCodeList(instructions);
                CheckSegmentFlagsCommons.PatchCheckFlags(codes, original);
                Log.Info($"{ReflectionHelpers.ThisMethod} patched {original} successfully!");
                return codes;
            } catch(Exception e) {
                Log.Error(e.ToString());
                throw e;
            }
        }
    } // end class

    [HarmonyPatch()]
    public static class RenderInstanceOverlayPatch {
        // private void NetSegment.RenderInstance(RenderManager.CameraInfo cameraInfo, ushort segmentID, int layerMask, NetInfo info, ref RenderManager.Instance data)
        public static MethodBase TargetMethod() {
            var types = new Type[] {
                typeof(RenderManager.CameraInfo), typeof(ushort), typeof(int),
                typeof(NetInfo), typeof(RenderManager.Instance).MakeByRefType()
            };
            return typeof(NetSegment).GetMethod("RenderInstance", BindingFlags.NonPublic | BindingFlags.Instance, null, types, null);
        } 

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) {
            try {
                var codes = TranspilerUtils.ToCodeList(instructions);
                SegmentOverlay.Patch(codes, original);
                Log.Info($"{ReflectionHelpers.ThisMethod} patched {original} successfully!");
                return codes;
            } catch(Exception e) {
                Log.Error(e.ToString());
                throw e;
            }
        }
    } // end class
} // end name space
