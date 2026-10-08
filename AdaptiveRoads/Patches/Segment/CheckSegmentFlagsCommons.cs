using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using AdaptiveRoads.Data.NetworkExtensions;

namespace AdaptiveRoads.Patches.Segment {
    using AdaptiveRoads.Manager;
    using KianCommons;
    using System;
    using KianCommons.Patches;

    public static class CheckSegmentFlagsCommons {
        static NetworkExtensionManager man_ => NetworkExtensionManager.Instance;

        /// <summary>
        /// Set by RenderInstance transpiler before the game calls RenderSegments,
        /// so that our RenderSegments transpiler can look up segment extension data
        /// without needing segmentID as a parameter.
        /// </summary>
        internal static ushort s_currentSegmentID;

        // Called by transpiler AFTER game's base CheckFlags already ran.
        // turnAround has already been set by the game's CheckFlags call.
        // segmentID is either passed directly (methods that have it as a param: PopulateGroupData,
        // CalculateGroupData) or comes from s_currentSegmentID (RenderSegments, which has none).
        public static bool CheckFlags(NetInfo.Segment segmentInfo, ushort segmentID, ref bool turnAround) {
            if (segmentInfo == null) return true; // bypass
            var segmentInfoExt = segmentInfo.GetMetaData();
            if (segmentInfoExt == null) return true; // bypass

            ref NetSegmentExt netSegmentExt = ref man_.SegmentBuffer[segmentID];
            ref NetSegment netSegment = ref segmentID.ToSegment();
            ref NetNode netNodeStart = ref netSegment.m_startNode.ToNode();
            ref NetNode netNodeEnd = ref netSegment.m_endNode.ToNode();
            ref NetNodeExt netNodeExtStart = ref man_.NodeBuffer[netSegment.m_startNode];
            ref NetNodeExt netNodeExtEnd = ref man_.NodeBuffer[netSegment.m_endNode];

            var segmentTailFlags = netSegmentExt.Start.m_flags;
            var segmentHeadFlags = netSegmentExt.End.m_flags;
            var nodeTailFlags = netNodeStart.flags;
            var nodeHeadFlags = netNodeEnd.flags;
            var nodeExtTailFlags = netNodeExtStart.m_flags;
            var nodeExtHeadFlags = netNodeExtEnd.m_flags;

            bool reverse = NetUtil.LHT;
            if (reverse) {
                Helpers.Swap(ref segmentTailFlags, ref segmentHeadFlags);
                Helpers.Swap(ref nodeTailFlags, ref nodeHeadFlags);
            }

            for (int direction = 0; direction < 2; ++direction) {
                bool reverseMesh = direction != 0;

                if (!segmentInfo.CheckFlags(netSegment.m_flags, netSegment.m_flags2, reverseMesh))
                    continue;

                if (segmentInfoExt.CheckFlags(
                        netSegmentExt.m_flags,
                        tailFlags: segmentTailFlags,
                        headFlags: segmentHeadFlags,
                        tailNodeFlags: nodeTailFlags,
                        headNodeFlags: nodeHeadFlags,
                        tailNodeExtFlags: nodeExtTailFlags,
                        headNodeExtFlags: nodeExtHeadFlags,
                        userData: netSegmentExt.UserData,
                        turnAround: reverseMesh)) {
                    turnAround = reverseMesh;
                    return true;
                }
            }

            turnAround = false;
            return false;
        }

        static MethodInfo mCheckFlagsExt => typeof(CheckSegmentFlagsCommons).GetMethod(
            "CheckFlags", new[] { typeof(NetInfo.Segment), typeof(ushort), typeof(bool).MakeByRefType() })
            ?? throw new Exception("mCheckFlagsExt is null");
        static MethodInfo mCheckFlags => typeof(NetInfo.Segment).GetMethod("CheckFlags")
            ?? throw new Exception("mCheckFlags is null");
        static FieldInfo fCurrentSegmentID => typeof(CheckSegmentFlagsCommons)
            .GetField(nameof(s_currentSegmentID), BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new Exception("fCurrentSegmentID is null");

        // Injects an AR extension flag check after the game's CheckFlags call in RenderSegments.
        // Uses s_currentSegmentID, set at RenderInstance entry by the Harmony Prefix,
        // to look up the correct segment extension data on every rendering path.
        public static void PatchCheckFlags(List<CodeInstruction> codes, MethodBase method, int occurance = 1) {
            // callvirt NetInfo+Segment.CheckFlags(Flags, Flags2, bool&)
            var index = codes.Search(c => c.Calls(mCheckFlags), count: occurance, throwOnError: false);
            if (index < 1) {
                throw new Exception($"PatchCheckFlags: Could not find CheckFlags call (occurrence {occurance}) in {method.Name}.");
            }

            CodeInstruction LDLoc_SegmentInfo = GetPrevLdLocSegmentInfo(method, codes, index);
            CodeInstruction LDLoca_turnAround = new CodeInstruction(codes[index - 1]);
            Assertion.Assert(LDLoca_turnAround.opcode == OpCodes.Ldloca_S, $"expected Ldloca_S, got {LDLoca_turnAround.opcode}");

            // If the target method has a segmentID parameter, load it directly.
            // Otherwise (e.g. RenderSegments) load from s_currentSegmentID set by RenderInstance.
            bool hasSegmentIDParam = Array.Find(method.GetParameters(), p => p.Name == "segmentID") != null;
            CodeInstruction loadSegmentID = hasSegmentIDParam
                ? TranspilerUtils.GetLDArg(method, "segmentID")
                : new CodeInstruction(OpCodes.Ldsfld, fCurrentSegmentID);

            { // insert our checkflags after base checkflags
                var newInstructions = new[]{
                    LDLoc_SegmentInfo,
                    loadSegmentID,
                    LDLoca_turnAround,
                    new CodeInstruction(OpCodes.Call, mCheckFlagsExt),
                    new CodeInstruction(OpCodes.And),
                };
                codes.InsertInstructions(index + 1, newInstructions);
            }
        }

        static FieldInfo fSegments => typeof(NetInfo).GetField("m_segments") ?? throw new Exception("fSegments is null");

        public static CodeInstruction GetPrevLdLocSegmentInfo(MethodBase method, List<CodeInstruction> codes, int index) {
            index = codes.Search(c => c.IsLdLoc(typeof(NetInfo.Segment), method), index, count: -1);
            return codes[index].Clone(); // duplicated without lables and blocks
        }

    }
}
