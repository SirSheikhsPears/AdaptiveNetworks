using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using KianCommons.Patches;
using static KianCommons.Patches.TranspilerUtils;
using KianCommons;
using ColossalFramework.Math;
using System;
using UnityEngine;
using AdaptiveRoads.UI.RoadEditor;

namespace AdaptiveRoads.Patches.Segment {
    public static class SegmentOverlay {
        delegate void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties);
        static MethodInfo mDrawMesh =
            DeclaredMethod<DrawMesh>(typeof(Graphics));

        public static void Patch(List<CodeInstruction> codes, MethodBase method) {
            int draw = codes.FindIndex(c => c.Calls(mDrawMesh));
            if (draw < 14 || draw != codes.FindLastIndex(c => c.Calls(mDrawMesh)) || draw + 1 >= codes.Count)
                throw new InvalidOperationException("S01: expected one detail DrawMesh in RenderSegments");

            var mesh = typeof(NetInfo.Segment).GetField(nameof(NetInfo.Segment.m_segmentMesh));
            var material = typeof(NetInfo.Segment).GetField(nameof(NetInfo.Segment.m_segmentMaterial));
            var layer = typeof(NetInfo.Segment).GetField(nameof(NetInfo.Segment.m_layer));
            var model = codes[draw - 14];
            if (!model.IsLdLoc(typeof(NetInfo.Segment), method) || !model.IsLdLoc(out int modelIndex) ||
                !codes[draw - 13].LoadsField(mesh) || !codes[draw - 8].IsLdLoc(modelIndex) ||
                !codes[draw - 7].LoadsField(material) || !codes[draw - 6].IsLdLoc(modelIndex) ||
                !codes[draw - 5].LoadsField(layer) ||
                codes.GetRange(draw - 13, 14).Exists(c => c.labels.Count != 0 || c.blocks.Count != 0) ||
                codes[draw + 1].blocks.Count != 0)
                throw new InvalidOperationException("S01: detail mesh, material and layer must use one segment model");

            // Read the boolean actually consumed by the MeshScale X/Y reversal.
            var x = typeof(Vector4).GetField(nameof(Vector4.x));
            var y = typeof(Vector4).GetField(nameof(Vector4.y));
            int flip = codes.FindIndex(c => c.opcode == OpCodes.Stfld && Equals(c.operand, x)) - 6;
            if (flip < 0 || flip + 12 >= draw ||
                codes.FindLastIndex(draw, c => c.opcode == OpCodes.Stfld && Equals(c.operand, x)) != flip + 6 ||
                !codes[flip].IsLdLoc(typeof(bool), method) ||
                !codes[flip].IsLdLoc(out int directionIndex) ||
                !(codes[flip + 1].opcode == OpCodes.Brfalse || codes[flip + 1].opcode == OpCodes.Brfalse_S) ||
                !(codes[flip + 1].operand is Label skip) || !codes[flip + 12].labels.Contains(skip) ||
                !codes[flip + 4].LoadsField(x) || codes[flip + 5].opcode != OpCodes.Neg ||
                !codes[flip + 9].LoadsField(y) || codes[flip + 10].opcode != OpCodes.Neg ||
                codes[flip + 11].opcode != OpCodes.Stfld || !Equals(codes[flip + 11].operand, y))
                throw new InvalidOperationException("S01: final direction of the selected detail draw is ambiguous");
            foreach (int offset in new[] { 2, 3, 7, 8 })
                if (!codes[flip + offset].IsLdLocA(typeof(Vector4), method) ||
                    RenderDirectionPatch.Local(codes[flip + offset]) != RenderDirectionPatch.Local(codes[flip + 2]))
                    throw new InvalidOperationException("S01: MeshScale X/Y must use one Vector4 local");
            for (int i = flip + 1; i < draw; ++i)
                if ((codes[i].opcode.Name.StartsWith("stloc") || codes[i].opcode == OpCodes.Ldloca ||
                    codes[i].opcode == OpCodes.Ldloca_S) && RenderDirectionPatch.Local(codes[i]) == directionIndex)
                    throw new InvalidOperationException("S01: direction can change after MeshScale reversal");

            var insertion = new[]{
                new CodeInstruction(OpCodes.Ldarg_0),
                model.Clone(),
                codes[flip].Clone(),
                GetLDArg(method, "wOffsetParent"),
                GetLDArg(method, "netManager"),
                new CodeInstruction(OpCodes.Ldarga_S, method.GetArgLoc("data")),
                new CodeInstruction(OpCodes.Call, mAfterRoadDraw)
            };
            codes.InsertInstructions(draw + 1, insertion, moveLabels: false);
        }

        static MethodInfo mAfterRoadDraw = GetMethod(typeof(SegmentOverlay), nameof(AfterRoadDraw));
        static unsafe void AfterRoadDraw(ref NetSegment receiver, NetInfo.Segment model, bool turnAround,
            float wOffsetParent, NetManager manager, ref RenderManager.Instance data) {
            if (wOffsetParent != 0f || model != Overlay.HoveredInfo ||
                data.m_holder <= Manager.TrackManager.SEGMENT_HOLDER) return;
            uint id = data.m_holder - Manager.TrackManager.SEGMENT_HOLDER;
            var buffer = manager.m_segments.m_buffer;
            if (id > ushort.MaxValue || id >= buffer.Length) return;

            // Confirm the holder names this exact buffer slot, including for parallel segments.
            fixed (ushort* drawn = &receiver.m_startNode)
            fixed (ushort* slot = &buffer[id].m_startNode)
                if (drawn != slot) return;
            OnAfterDrawMesh((ushort)id, model, turnAround);
        }

        public static void OnAfterDrawMesh(
            ushort segmentID,
            NetInfo.Segment segmentInfo,
            bool turnAround) { 
            if(segmentInfo == Overlay.HoveredInfo) {
                var data = new Overlay.SegmentData {
                    SegmentID = segmentID,
                    TurnAround = turnAround,
                };
                Overlay.SegmentQueue.Enqueue(data);
            }
        }
    }
}
