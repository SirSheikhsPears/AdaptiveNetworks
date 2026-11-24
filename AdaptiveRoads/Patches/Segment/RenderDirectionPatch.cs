using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AdaptiveRoads.Patches.Segment {
    // No offsets or ordinal stloc matching.
    // Validates the complete Fence/auto-invert expression and branch destinations
    // before changing the caller's list. Does not replay any other transpiler.
    internal static class RenderDirectionPatch {
        static void Require(bool ok, string reason) {
            if (!ok) throw new InvalidOperationException("S03_PATCH_INCOMPLETE: " + reason);
        }
        internal static int Local(CodeInstruction c) {
            if (c.operand is LocalBuilder) return ((LocalBuilder)c.operand).LocalIndex;
            if (c.operand is LocalVariableInfo) return ((LocalVariableInfo)c.operand).LocalIndex;
            if (c.opcode == OpCodes.Ldloc_0 || c.opcode == OpCodes.Stloc_0) return 0;
            if (c.opcode == OpCodes.Ldloc_1 || c.opcode == OpCodes.Stloc_1) return 1;
            if (c.opcode == OpCodes.Ldloc_2 || c.opcode == OpCodes.Stloc_2) return 2;
            if (c.opcode == OpCodes.Ldloc_3 || c.opcode == OpCodes.Stloc_3) return 3;
            return Convert.ToInt32(c.operand);
        }
        static int Arg(CodeInstruction c) {
            if(c.opcode == OpCodes.Ldarg_0) return 0;
            if(c.opcode == OpCodes.Ldarg_1) return 1;
            if(c.opcode == OpCodes.Ldarg_2) return 2;
            if(c.opcode == OpCodes.Ldarg_3) return 3;
            return c.operand is ParameterInfo ? ((ParameterInfo)c.operand).Position + 1 : Convert.ToInt32(c.operand);
        }
        static bool LdArg(CodeInstruction c,int n) { return c.opcode.Name.StartsWith("ldarg") && !c.opcode.Name.StartsWith("ldarga") && Arg(c)==n; }
        static bool LdLoc(CodeInstruction c,int n) { return c.opcode.Name.StartsWith("ldloc") && !c.opcode.Name.StartsWith("ldloca") && Local(c)==n; }
        static bool StLoc(CodeInstruction c,int n) { return c.opcode.Name.StartsWith("stloc") && Local(c)==n; }
        static bool I4(CodeInstruction c,int n) {
            if(c.opcode==OpCodes.Ldc_I4_0) return n==0;
            return (c.opcode==OpCodes.Ldc_I4 || c.opcode==OpCodes.Ldc_I4_S) && Convert.ToInt32(c.operand)==n;
        }
        static bool Field(CodeInstruction c, Type t,string name) { return c.opcode==OpCodes.Ldfld && Equals(c.operand,t.GetField(name)); }
        static bool Branch(CodeInstruction c,OpCode op, CodeInstruction destination) {
            return (c.opcode==op || c.opcode.Name==op.Name+".s") && c.operand is Label && destination.labels.Contains((Label)c.operand);
        }
        static bool Op(CodeInstruction c,OpCode op) {return c.opcode==op;}
        static CodeInstruction LoadLocal(int n) {return new CodeInstruction(OpCodes.Ldloc,n);}
        internal static void Apply(List<CodeInstruction> codes,MethodBase original) {
            var parameters=original.GetParameters();
            Require(original.DeclaringType==typeof(NetSegment) && original.Name=="RenderSegments" && !original.IsStatic,
                "target role");
            Type[] signature={typeof(RenderManager.CameraInfo),typeof(NetInfo),typeof(RenderManager.Instance),typeof(float),typeof(NetManager)};
            Require(parameters.Length==signature.Length,"ABI arity");
            for(int j=0;j<signature.Length;j++) Require(parameters[j].ParameterType==signature[j],"ABI parameter "+j);
            var check=typeof(NetInfo.Segment).GetMethod("CheckFlags",new[]{typeof(NetSegment.Flags),typeof(NetSegment.Flags2),typeof(bool).MakeByRefType()});
            var auto=typeof(NetAI).GetMethod("CanAutoRenderInverted",Type.EmptyTypes);
            int ci=-1,ai=-1,nc=0,na=0;
            for(int j=0;j<codes.Count;j++) {
                if(Op(codes[j],OpCodes.Callvirt) && Equals(codes[j].operand,check)){ci=j;nc++;}
                if(Op(codes[j],OpCodes.Callvirt) && Equals(codes[j].operand,auto)){ai=j;na++;}
            }
            Require(nc==1 && na==1 && ci>=6 && ai>ci+26,"unique CheckFlags / CanAutoRenderInverted producer");
            Require(Op(codes[ci-1],OpCodes.Ldloca)||Op(codes[ci-1],OpCodes.Ldloca_S),"out bool local");
            int direction=Local(codes[ci-1]),mesh=Local(codes[ci-6]);
            var locals=original.GetMethodBody().LocalVariables;
            Require(direction>=0 && direction<locals.Count && locals[direction].LocalType==typeof(bool),"bool local type");
            Require(mesh>=0 && mesh<locals.Count && locals[mesh].LocalType==typeof(NetInfo.Segment),"selected mesh local type");
            Require(LdLoc(codes[ci-6],mesh) && LdArg(codes[ci-5],0) && Field(codes[ci-4],typeof(NetSegment),"m_flags") &&
                LdArg(codes[ci-3],0) && Field(codes[ci-2],typeof(NetSegment),"m_flags2"),"CheckFlags producer stack");
            Require(codes[ci+1].opcode==OpCodes.Brfalse || codes[ci+1].opcode==OpCodes.Brfalse_S,"predicate consumer");
            int start=ai-26;
            Require(ai+11<codes.Count,"orientation expression length");
            var w=codes.GetRange(start,38);
            bool valid=LdArg(w[0],4)&&Op(w[1],OpCodes.Ldc_R4)&&Equals(w[1].operand,0f)&&Branch(w[2],OpCodes.Bge_Un,w[11])&&
                LdArg(w[3],0)&&Field(w[4],typeof(NetSegment),"m_flags2")&&I4(w[5],(int)NetSegment.Flags2.FenceLeftInvert)&&
                Op(w[6],OpCodes.And)&&I4(w[7],0)&&Op(w[8],OpCodes.Ceq)&&StLoc(w[9],direction)&&Branch(w[10],OpCodes.Br,w[37])&&
                LdArg(w[11],4)&&Op(w[12],OpCodes.Ldc_R4)&&Equals(w[12].operand,0f)&&Branch(w[13],OpCodes.Ble_Un,w[24])&&
                LdArg(w[14],0)&&Field(w[15],typeof(NetSegment),"m_flags2")&&I4(w[16],(int)NetSegment.Flags2.FenceRightInvert)&&
                Op(w[17],OpCodes.And)&&I4(w[18],0)&&Op(w[19],OpCodes.Ceq)&&I4(w[20],0)&&Op(w[21],OpCodes.Ceq)&&StLoc(w[22],direction)&&Branch(w[23],OpCodes.Br,w[37])&&
                LdArg(w[24],2)&&Field(w[25],typeof(NetInfo),"m_netAI")&&Branch(w[27],OpCodes.Brfalse,w[37])&&
                LdArg(w[28],0)&&Field(w[29],typeof(NetSegment),"m_flags")&&I4(w[30],(int)NetSegment.Flags.Invert)&&
                Op(w[31],OpCodes.And)&&I4(w[32],0)&&Op(w[33],OpCodes.Ceq)&&I4(w[34],0)&&Op(w[35],OpCodes.Ceq)&&StLoc(w[36],direction)&&LdLoc(w[37],direction);
            Require(valid,"Fence/auto-invert expression or branch destinations changed");
            int stores=0;
            for(int j=0;j<codes.Count;j++) if(StLoc(codes[j],direction))stores++;
            Require(stores==3,"unexpected final-direction writer");
            // Interior labels could enter the expression with a different stack.
            for(int j=0;j<w.Count;j++) {
                Require(w[j].blocks.Count==0,"EH boundary in orientation expression");
                if(j!=0&&j!=11&&j!=24&&j!=37)Require(w[j].labels.Count==0,"interior orientation branch entry");
            }
            Require(codes[ci].blocks.Count==0 && codes[ci+1].blocks.Count==0 && codes[ci+1].labels.Count==0,
                "CheckFlags insertion boundary");
            var replacement=new List<CodeInstruction>(codes); // no mutation before complete validation
            replacement[ai]=new CodeInstruction(OpCodes.Call,typeof(CheckSegmentFlagsCommons).GetMethod("AllowNativeAutoInvert"));
            replacement.Insert(ai,LoadLocal(mesh));
            replacement.InsertRange(ci+1,new[]{LoadLocal(mesh),
                new CodeInstruction(OpCodes.Ldsfld,typeof(CheckSegmentFlagsCommons).GetField("s_currentSegmentID",BindingFlags.Static|BindingFlags.NonPublic)),
                new CodeInstruction(OpCodes.Ldloca,direction),new CodeInstruction(OpCodes.Ldarg,4),
                new CodeInstruction(OpCodes.Call,typeof(CheckSegmentFlagsCommons).GetMethod("CheckFlagsForRender")),new CodeInstruction(OpCodes.And)});
            codes.Clear(); codes.AddRange(replacement);
        }
    }
}
