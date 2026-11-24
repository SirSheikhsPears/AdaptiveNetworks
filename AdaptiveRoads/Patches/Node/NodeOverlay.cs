using AdaptiveRoads.UI.RoadEditor;
using HarmonyLib;
using KianCommons;
using KianCommons.Patches;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static KianCommons.Patches.TranspilerUtils;
using ColossalFramework;
using static UnityEngine.UI.Image;

namespace AdaptiveRoads.Patches.Node {
    public static class NodeOverlay {
        delegate void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties);
        static MethodInfo mDrawMesh =
            DeclaredMethod<DrawMesh>(typeof(Graphics));

        static MethodInfo mCheckFlags =
            GetMethod(typeof(NetInfo.Node), nameof(NetInfo.Node.CheckFlags));

        public static void Patch(List<CodeInstruction> codes, MethodBase method, int occuranceDrawMesh, int counterGetSegment) {
            CodeInstruction ldNodeID = GetLDArg(method, "nodeID");
            CodeInstruction loadRenderData = GetLDArg(method, "data");

            int iDrawMesh = codes.Search(_c => _c.Calls(mDrawMesh), count: occuranceDrawMesh);
            int iLdLocNodeInfo = codes.Search(
                _c => _c.IsLdLoc(typeof(NetInfo.Node), method),
                startIndex: iDrawMesh, count: -1);
            CodeInstruction ldNodeInfo = codes[iLdLocNodeInfo].Clone();

            CodeInstruction[] insertion = new[]{
                ldNodeID,
                loadRenderData,
                ldNodeInfo,
                new CodeInstruction(OpCodes.Call, mOnAfterDrawMesh)
            };
            codes.InsertInstructions(iDrawMesh + 1, insertion, moveLabels: false);
        }

        static MethodInfo mOnAfterDrawMesh = GetMethod(typeof(NodeOverlay), nameof(OnAfterDrawMesh));

        public static void OnAfterDrawMesh(
            ushort nodeID,
            ref RenderManager.Instance renderData,
            NetInfo.Node nodeInfo) {
            if (nodeInfo == Overlay.HoveredInfo)
                Enqueue(nodeID, ref renderData);
            
        }

        static void Enqueue(
            ushort nodeID, ref RenderManager.Instance renderData,
            bool isBendNode = false, bool turnAround = false) {
            int segmentIndex = renderData.m_dataInt0 & 7;
            int segmentIndex2 = renderData.m_dataInt0 >> 4;
            ref var node = ref nodeID.ToNode();
            ushort segmentID = node.GetSegment(segmentIndex);
            ushort segmentID2 = node.GetSegment(segmentIndex2);
            bool isDC = (renderData.m_dataInt0 & 8) != 0; // normal DC
            isDC |= node.m_flags.IsFlagSet(NetNode.Flags.Bend); // bend DC

            var data = new Overlay.NodeData {
                NodeID = nodeID,
                SegmentID = segmentID,
                SegmentID2 = segmentID2,
                IsDC = isDC,
                IsBendNode = isBendNode,
                TurnAround = turnAround,
            };
            Overlay.NodeQueue.Enqueue(data);
        }

        public static void PatchBend(List<CodeInstruction> codes, MethodBase method, int occuranceDrawMesh) {
            CodeInstruction ldNodeID = GetLDArg(method, "nodeID");
            CodeInstruction loadRenderData = GetLDArg(method, "data");
            MethodInfo mCheckFlags = typeof(NetInfo.Segment).GetMethod(nameof(NetInfo.Segment.CheckFlags),
                new[] { typeof(NetSegment.Flags), typeof(NetSegment.Flags2), typeof(bool).MakeByRefType() });
            Assertion.Assert(mCheckFlags != null && mCheckFlags.ReturnType == typeof(bool),
                "Ordinary Bend CheckFlags signature mismatch");

            int iDrawMesh = codes.Search(_c => _c.Calls(mDrawMesh), count: occuranceDrawMesh);
            int iLdLocSegmentInfo = codes.Search(
                _c => _c.IsLdLoc(typeof(NetInfo.Segment), method),
                startIndex: iDrawMesh, count: -1);
            Assertion.Assert(codes[iLdLocSegmentInfo + 1].opcode == OpCodes.Ldfld &&
                Equals(codes[iLdLocSegmentInfo + 1].operand, typeof(NetInfo.Segment).GetField("m_layer")),
                "Ordinary Bend model source mismatch");
            CodeInstruction ldSegmentInfo = codes[iLdLocSegmentInfo].Clone();

            int iCheckFlags = codes.FindIndex(c => c.Calls(mCheckFlags));
            Assertion.Assert(iCheckFlags > 0 && iCheckFlags < iDrawMesh &&
                codes.FindLastIndex(c => c.Calls(mCheckFlags)) == iCheckFlags &&
                codes[iCheckFlags - 1].IsLdLocA(typeof(bool), method),
                "Ordinary Bend orientation source mismatch");
            CodeInstruction loadRefTurnAround = codes[iCheckFlags - 1].Clone();

            CodeInstruction[] insertion = new[]{
                ldNodeID,
                loadRenderData,
                ldSegmentInfo,
                loadRefTurnAround,
                new CodeInstruction(OpCodes.Call, mOnAfterDrawMeshBend)
            };
            codes.InsertInstructions(iDrawMesh + 1, insertion, moveLabels: false);
        }

        [System.ThreadStatic]
        static ushort pendingBend;

        internal static bool MarkOrdinaryCall(List<CodeInstruction> codes, MethodBase method) {
            try {
                var helper = BendOverlayPatch.TargetMethod();
                int ordinary = -1;
                for (int i = 5; i + 1 < codes.Count; ++i) {
                    if (!codes[i].Calls(helper) ||
                        !codes[i - 5].IsLdarg(method, "info") ||
                        !codes[i - 4].IsLdarg(method, "data") ||
                        !codes[i - 3].IsLdLoc(typeof(NetManager), method) ||
                        !codes[i - 2].LoadsConstant(0) ||
                        !codes[i - 1].LoadsConstant(0))
                        continue;
                    if (ordinary >= 0 || codes[i + 1].blocks.Count != 0 ||
                        codes.GetRange(i - 4, 5).Exists(c => c.labels.Count != 0 || c.blocks.Count != 0))
                        return false;
                    ordinary = i;
                }
                if (ordinary < 0) return false;
                var field = AccessTools.Field(typeof(NodeOverlay), nameof(pendingBend));
                if (field == null) return false;
                var nodeArg = GetLDArg(method, "nodeID");
                var candidate = new List<CodeInstruction>(codes);
                candidate.InsertRange(ordinary + 1, new[] {
                    new CodeInstruction(OpCodes.Ldc_I4_0), new CodeInstruction(OpCodes.Stsfld, field)
                });
                candidate.InsertRange(ordinary - 3, new[] {
                    nodeArg, new CodeInstruction(OpCodes.Stsfld, field)
                });
                codes.Clear();
                codes.AddRange(candidate);
                return true;
            } catch (System.Exception) {
                return false;
            }
        }

        static bool TakeOrdinaryBend(ushort nodeID, bool overrideTurnAround) {
            ushort pending = pendingBend;
            pendingBend = 0;
            return nodeID != 0 && !overrideTurnAround && pending == nodeID;
        }

        internal static void PatchOrdinaryBend(List<CodeInstruction> codes, MethodBase method, ILGenerator generator) {
            Assertion.Assert(codes.FindAll(c => c.Calls(mDrawMesh)).Count == 1,
                "Ordinary Bend helper draw count mismatch");
            int draw = codes.Search(c => c.Calls(mDrawMesh));
            Assertion.Assert(codes[0].blocks.Count == 0 && codes[draw].labels.Count == 0 &&
                codes[draw].blocks.Count == 0 && codes[draw + 1].blocks.Count == 0,
                "Ordinary Bend unexpected IL boundary");
            var role = generator.DeclareLocal(typeof(bool));
            var skip = generator.DefineLabel();
            var next = codes[draw + 1];
            PatchBend(codes, method, occuranceDrawMesh: 1);
            next.labels.Add(skip);
            codes.InsertRange(draw + 1, new[] {
                new CodeInstruction(OpCodes.Ldloc, role),
                new CodeInstruction(OpCodes.Brfalse, skip)
            });
            codes.InsertRange(0, new[] {
                GetLDArg(method, "nodeID"), GetLDArg(method, "overrideTurnAround"),
                new CodeInstruction(OpCodes.Call, GetMethod(typeof(NodeOverlay), nameof(TakeOrdinaryBend))),
                new CodeInstruction(OpCodes.Stloc, role)
            });
        }

        static MethodInfo mOnAfterDrawMeshBend = GetMethod(typeof(NodeOverlay), nameof(OnAfterDrawMeshBend));
        public static void OnAfterDrawMeshBend(
            ushort nodeID,
            ref RenderManager.Instance renderData,
            NetInfo.Segment segmentInfo,
            ref bool turnAround) {
            if (segmentInfo == Overlay.HoveredInfo) {
                Enqueue(nodeID, ref renderData, isBendNode:true, turnAround: turnAround);
            }
        }
    }
}
