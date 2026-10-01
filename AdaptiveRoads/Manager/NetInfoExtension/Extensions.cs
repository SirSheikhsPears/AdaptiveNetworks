namespace AdaptiveRoads.Manager {
    using AdaptiveRoads.Data.NetworkExtensions;
    using KianCommons;
    using KianCommons.Math;
    using PrefabMetadata.API;
    using PrefabMetadata.Helpers;
    using System.Collections.Generic;
    using static AdaptiveRoads.Manager.NetInfoExtionsion;

    public static class Extensions {
        internal static NetInfo GetInfo(ushort index) =>
            PrefabCollection<NetInfo>.GetPrefab(index);

        internal static ushort GetIndex(this NetInfo info) =>
            MathUtil.Clamp2U16(info.m_prefabDataIndex);

        public static Segment GetMetaData(this NetInfo.Segment segment) =>
            (segment as IInfoExtended)?.GetMetaData<Segment>();

        public static Manager.NetInfoExtionsion.Node GetMetaData(this NetInfo.Node node) =>
            (node as IInfoExtended)?.GetMetaData<Manager.NetInfoExtionsion.Node>();

        public static LaneProp GetMetaData(this NetLaneProps.Prop prop) =>
            (prop as IInfoExtended)?.GetMetaData<LaneProp>();

        public static Net GetMetaData(this NetInfo netInfo) =>
            NetMetadataContainer.GetMetadata(netInfo);

        public static Net GetOrCreateMetaData(this NetInfo netInfo) =>
            NetMetadataContainer.GetOrCreateMetadata(netInfo);

        public static void SetMetedata(this NetInfo netInfo, Net value) =>
            NetMetadataContainer.SetMetadata(netInfo, value);

        public static void RecalculateMetaData(this NetInfo netInfo) {
            netInfo.GetMetaData()?.Recalculate(netInfo);
        }

        public static void RemoveMetadataContainer(this NetInfo netInfo) =>
            NetMetadataContainer.RemoveContainer(netInfo);

        public static Segment GetOrCreateMetaData(this NetInfo.Segment segment) {
            Assertion.Assert(segment is IInfoExtended);
            var segment2 = segment as IInfoExtended;
            var ret = segment2.GetMetaData<Segment>();
            if(ret == null) {
                ret = new Segment(segment);
                segment2.SetMetaData(ret);
            }
            return ret;
        }

        public static Manager.NetInfoExtionsion.Node GetOrCreateMetaData(this NetInfo.Node node) {
            Assertion.Assert(node is IInfoExtended);
            var node2 = node as IInfoExtended;
            var ret = node2.GetMetaData<Manager.NetInfoExtionsion.Node>();
            if(ret == null) {
                ret = new Manager.NetInfoExtionsion.Node(node);
                node2.SetMetaData(ret);
            }
            return ret;
        }

        public static LaneProp GetOrCreateMetaData(this NetLaneProps.Prop prop) {
            Assertion.Assert(prop is IInfoExtended);
            var prop2 = prop as IInfoExtended;
            var ret = prop2.GetMetaData<LaneProp>();
            if(ret == null) {
                ret = new LaneProp(prop);
                prop2.SetMetaData(ret);
            }
            return ret;
        }

        public static bool CheckRange(this Range range, float value) => range?.InRange(value) ?? true;

        public static bool CheckFlags(this NetInfo.Segment segmentInfo, NetSegment.Flags flags, bool turnAround) {
            if(!turnAround)
                return flags.CheckFlags(segmentInfo.m_forwardRequired, segmentInfo.m_forwardForbidden);
            else
                return flags.CheckFlags(segmentInfo.m_backwardRequired, segmentInfo.m_backwardForbidden);
        }

        // Check one specific orientation, including the flag word added by Race Day.
        // The game's ref-bool overload chooses a direction before AN flags are checked.
        public static bool CheckFlags(this NetInfo.Segment segmentInfo, NetSegment.Flags flags,
                NetSegment.Flags2 flags2, bool turnAround) {
            var required2 = turnAround ? segmentInfo.m_backwardRequired2 : segmentInfo.m_forwardRequired2;
            var forbidden2 = turnAround ? segmentInfo.m_backwardForbidden2 : segmentInfo.m_forwardForbidden2;
            return segmentInfo.CheckFlags(flags, turnAround) &&
                (flags2 & (required2 | forbidden2)) == required2;
        }

        /// <param name="layerMask">for calculate/populate group data <c>layerMask=1<<layer</c></param>
        /// <returns>if there is any matching layer</returns>
        public static bool CheckNetLayers(this NetInfo info, int layerMask) => info && (layerMask & info.m_netLayers) != 0;
        public static int TrackLaneCount(this NetInfo info) => info?.GetMetaData()?.TrackLaneCount ?? 0;
        public static bool HasTitlableTracks(this NetInfo info) => info?.GetMetaData()?.HasTitlableTracks ?? false;

        public static float GetFinalShift(this NetInfo info) {
            float shift = info?.GetMetaData()?.Shift ?? 0;
            if (NetUtil.LHT)
                shift = -shift;
            return shift;
        }

        public static void CheckProp(this NetInfo netInfo, PropInfo propInfo, HashSet<PropInfo> traversed = null) {
            ReflectionHelpers.InvokeMethod(netInfo, "CheckProp", propInfo, traversed);
        }
    }

}
