using System;
using System.Collections.Generic;
using Halfling;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class MergeMath
    {
        public const float ALIGN_TOL_CELLS = 0.02f;
        public const float ALIGN_SPEED_CELLS = 0.05f;

        public static bool Settled(int alignedSince, int now, int settleTicks) => alignedSince >= 0 && now - alignedSince >= settleTicks;

        public static bool StepDone(bool atTarget, bool mergeWanted, bool merged, bool mergeBlocked) =>
            atTarget && (!mergeWanted || merged || mergeBlocked);

        public static bool TryAlignedSlide(float slideCells, float speedCells, float maxExtension, out int k)
        {
            k = (int)Math.Round(slideCells);
            if (Math.Abs(slideCells - k) > ALIGN_TOL_CELLS) return false;
            if (Math.Abs(speedCells) > ALIGN_SPEED_CELLS) return false;
            return k >= 0 && k <= maxExtension + ALIGN_TOL_CELLS;
        }

        public static IntVector2 Side(IntVector2 facing, bool flipX)
        {
            IntVector2 s = facing.Rotate90(3);
            return flipX ? -s : s;
        }

        public static IntVector2 ToBaseFrame(IntVector2 baseLoc, IntVector2 facing, bool flipX, IntVector2 cell)
        {
            IntVector2 d = cell - baseLoc;
            IntVector2 s = Side(facing, flipX);
            return new IntVector2(d.X * s.X + d.Y * s.Y, d.X * facing.X + d.Y * facing.Y);
        }

        public static IntVector2 FromBaseFrame(IntVector2 baseLoc, IntVector2 facing, bool flipX, IntVector2 rel)
        {
            return baseLoc + Side(facing, flipX) * rel.X + facing * rel.Y;
        }

        public static void RelRotation(int baseRot, bool baseFlip, int partRot, bool partFlip, out int relRot, out bool relFlip)
        {
            int d = Mathx.Wrap(partRot - baseRot, 0, 3);
            relRot = baseFlip ? Mathx.Wrap(-d, 0, 3) : d;
            relFlip = partFlip != baseFlip;
        }

        public static void AbsRotation(int baseRot, bool baseFlip, int relRot, bool relFlip, out int partRot, out bool partFlip)
        {
            int d = baseFlip ? Mathx.Wrap(-relRot, 0, 3) : relRot;
            partRot = Mathx.Wrap(baseRot + d, 0, 3);
            partFlip = relFlip != baseFlip;
        }

        public static void Reframe(IntVector2 outerLoc, IntVector2 outerFacing, bool outerFlip, int outerRot,
            IntVector2 innerLoc, IntVector2 innerFacing, bool innerFlip, int innerRot,
            IntVector2 rel, int relRot, bool relFlip, bool hasRotation,
            out IntVector2 outRel, out int outRelRot, out bool outRelFlip)
        {
            IntVector2 cell = FromBaseFrame(innerLoc, innerFacing, innerFlip, rel);
            outRel = ToBaseFrame(outerLoc, outerFacing, outerFlip, cell);
            int rot = innerRot;
            bool flip = innerFlip;
            if (hasRotation) AbsRotation(innerRot, innerFlip, relRot, relFlip, out rot, out flip);
            RelRotation(outerRot, outerFlip, rot, flip, out outRelRot, out outRelFlip);
        }

        public static int QuarterTurns(float relAngle)
        {
            return Mathx.Wrap((int)Math.Round(relAngle / (Math.PI / 2.0)), 0, 3);
        }

        public static bool HolderAgrees(GridTransform xf, IntVector2 leadFacing, int leadK,
            IntVector2 baseLoc, IntVector2 facing, int k, IntVector2 tipCellOnSection)
        {
            if (facing != leadFacing || k != leadK) return false;
            return xf.TransformCell(tipCellOnSection) == baseLoc + facing * (1 + k);
        }

        public static bool WantMerged(int mergeAt, bool? signal, int popoutMode, float commandedCells,
            bool merged, int mergedSlide, out int needSlide)
        {
            needSlide = -1;
            bool flagged = mergeAt != 0
                ? (signal == true ? (mergeAt & 1) != 0 : (mergeAt & 2) != 0)
                : popoutMode == 1 || popoutMode == 2;
            if (!flagged) return false;
            int target = (int)Math.Round(commandedCells);
            if (Math.Abs(commandedCells - target) > ALIGN_TOL_CELLS) return false;
            needSlide = target;
            return !merged || target == mergedSlide;
        }

        public static bool GripFirst(int gripMode, bool holding, bool refusedRecently)
        {
            if (refusedRecently) return false;
            return (gripMode == GripStore.GRIP_ATTACH && !holding) || (gripMode == GripStore.GRIP_RELEASE && holding);
        }

        public static GridTransform PistonTransform(IntVector2 baseLoc, IntVector2 facing, int k, IntVector2 tipCellOnSection, int quarterTurns)
        {
            IntVector2 target = baseLoc + facing * (1 + k);
            IntVector2 t = target - tipCellOnSection.Rotate90(quarterTurns);
            return new GridTransform(quarterTurns, t);
        }

        public static List<T> NestedClosure<T>(List<T> start, Func<T, IEnumerable<T>> nestedOf)
        {
            var all = new List<T>();
            var seen = new HashSet<T>();
            foreach (T p in start)
                if (seen.Add(p)) all.Add(p);
            for (int i = 0; i < all.Count; i++)
                foreach (T q in nestedOf(all[i]))
                    if (seen.Add(q)) all.Add(q);
            return all;
        }
    }
}
