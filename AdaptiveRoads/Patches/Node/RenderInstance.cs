namespace AdaptiveRoads.Patches.Node {
    using HarmonyLib;
    using KianCommons;
    using KianCommons.Patches;
    using System;
    using System.Collections.Generic;
    using System.Reflection;

    [HarmonyPatch()]
    [InGamePatch]
    public static class RenderInstance {
        public static MethodBase TargetMethod() {
            // RenderInstance(RenderManager.CameraInfo cameraInfo, ushort nodeID, NetInfo info, int iter, FlagsLong flags, ref uint instanceIndex, ref RenderManager.Instance data)
            var types = new Type[] {
                typeof(RenderManager.CameraInfo), typeof(ushort), typeof(NetInfo), typeof(int),
                typeof(NetNode.FlagsLong), typeof(uint).MakeByRefType(),
                typeof(RenderManager.Instance).MakeByRefType()
            };
            var ret = typeof(NetNode).GetMethod("RenderInstance", BindingFlags.NonPublic | BindingFlags.Instance, null, types, null);
            Assertion.Assert(ret != null, "did not manage to find original function to patch");
            return ret;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase original) {
            try {
                var codes = TranspilerUtils.ToCodeList(instructions);
                CheckNodeFlagsCommons.PatchCheckFlags(codes, original, occuranceCheckFlags: 1, counterGetSegment: 2, true); //DC
                CheckNodeFlagsCommons.PatchCheckFlags(codes, original, occuranceCheckFlags: 2, counterGetSegment: 2); //Junction
                CheckNodeFlagsCommons.PatchCheckFlags(codes, original, occuranceCheckFlags: 3, counterGetSegment: 0); //End

                // Bend node -> segment.Checkflags (does not use info flags.)
                CheckNodeFlagsCommons.PatchCheckFlags(codes, original, occuranceCheckFlags: 4, counterGetSegment: 2, true); // DC bend


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

        public static MethodBase TargetMethod() {
            // RenderInstance(RenderManager.CameraInfo cameraInfo, ushort nodeID, NetInfo info, int iter, FlagsLong flags, ref uint instanceIndex, ref RenderManager.Instance data)
            var types = new Type[] {
                typeof(RenderManager.CameraInfo), typeof(ushort), typeof(NetInfo), typeof(int),
                typeof(NetNode.FlagsLong), typeof(uint).MakeByRefType(),
                typeof(RenderManager.Instance).MakeByRefType()
            };
            var ret = typeof(NetNode).GetMethod("RenderInstance", BindingFlags.NonPublic | BindingFlags.Instance, null, types, null);
            Assertion.Assert(ret != null, "did not manage to find original function to patch");
            return ret;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase original) {
            try {
                var codes = TranspilerUtils.ToCodeList(instructions);
                NodeOverlay.Patch(codes, original, occuranceDrawMesh: 1, counterGetSegment: 2); // DC
                NodeOverlay.Patch(codes, original, occuranceDrawMesh: 2, counterGetSegment: 1); // junction
                NodeOverlay.Patch(codes, original, occuranceDrawMesh: 3, counterGetSegment: 0); // end
                NodeOverlay.Patch(codes, original, occuranceDrawMesh: 4, counterGetSegment: 0); // DC-bend
                if (!NodeOverlay.MarkOrdinaryCall(codes, original))
                    Log.Info("Ordinary Bend overlay could not be initialized. Other node overlays remain active.");
                Log.Info($"{ReflectionHelpers.ThisMethod} patched {original} successfully!");
                return codes;
            } catch(Exception e) {
                Log.Error(e.ToString());
                throw e;
            }
        }
    } // end class

    [HarmonyPatch()]
    internal static class BendOverlayPatch {
        public static MethodInfo TargetMethod() {
            var types = new[] {
                typeof(RenderManager.CameraInfo), typeof(ushort), typeof(NetInfo),
                typeof(RenderManager.Instance).MakeByRefType(), typeof(NetManager),
                typeof(bool), typeof(bool)
            };
            var method = typeof(NetNode).GetMethod("RenderSegments",
                BindingFlags.NonPublic | BindingFlags.Instance, null, types, null);
            Assertion.Assert(method != null && method.ReturnType == typeof(void),
                "ANR: Ordinary Bend helper signature mismatch");
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase original,
            System.Reflection.Emit.ILGenerator generator) {
            var codes = TranspilerUtils.ToCodeList(instructions);
            NodeOverlay.PatchOrdinaryBend(codes, original, generator);
            return codes;
        }
    }
} // end name space
