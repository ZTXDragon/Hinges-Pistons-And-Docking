using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;

namespace ZTX.HingesPistonsDocking
{
    internal struct GripArea
    {
        public bool On;
        public int Width;
        public int Length;
        public bool InLine;
        public int Offset;
    }

    internal static class GripStore
    {
        public const string PREFIX = "ztx_grip_";
        public static readonly ID<PartToggleGuiRules> AreaId = new ID<PartToggleGuiRules>("ztx_grip_area");
        public static readonly ID<PartToggleGuiRules> WidthId = new ID<PartToggleGuiRules>("ztx_grip_w");
        public static readonly ID<PartToggleGuiRules> LengthId = new ID<PartToggleGuiRules>("ztx_grip_l");
        public static readonly ID<PartToggleGuiRules> PositionId = new ID<PartToggleGuiRules>("ztx_grip_pos");
        public static readonly ID<PartToggleGuiRules> OffsetId = new ID<PartToggleGuiRules>("ztx_grip_off");
        public static readonly ID<PartToggleGuiRules> GripToggleId = new ID<PartToggleGuiRules>("ztx_piston_grip");

        public const int GRIP_OFF = 0;
        public const int GRIP_ATTACH = LogicAction.GRIP_ATTACH;
        public const int GRIP_RELEASE = LogicAction.GRIP_RELEASE;
        public const int DEFAULT_WIDTH = 1, DEFAULT_LENGTH = 3;

        public static GripArea Read(Part basePart)
        {
            var area = new GripArea { Width = DEFAULT_WIDTH, Length = DEFAULT_LENGTH };
            var meta = basePart?.Ship?.Metadata;
            if (meta == null) return area;
            var psd = new PartSaveData(basePart);
            area.On = (meta.PartUIToggleStates.Get((psd, AreaId)) ?? 0) != 0;
            area.Width = Clamp(meta.PartUIToggleStates.Get((psd, WidthId)) ?? DEFAULT_WIDTH);
            area.Length = Clamp(meta.PartUIToggleStates.Get((psd, LengthId)) ?? DEFAULT_LENGTH);
            area.InLine = (meta.PartUIToggleStates.Get((psd, PositionId)) ?? 0) != 0;
            area.Offset = meta.PartUIToggleStates.Get((psd, OffsetId)) ?? 0;
            return area;
        }

        public static int Clamp(int cells) => cells < 1 ? 1 : cells > GripMath.MAX_CELLS ? GripMath.MAX_CELLS : cells;

        public static int Desired(Part basePart) => LogicSource.Mode(basePart, GripToggleId, GRIP_OFF);

        public static MPInput WriteInputAll(IReadOnlyList<Part> parts, params (ID<PartToggleGuiRules> Id, int Value)[] writes)
        {
            var batch = new List<MPInput>();
            foreach (Part p in parts)
            {
                if (p?.Ship == null) continue;
                var psd = new PartSaveData(p);
                foreach (var (id, value) in writes)
                    batch.Add(new MPSetUIToggleMetadataInput(p.Ship, psd, id, value, false));
            }
            return new MPMultiInput(batch);
        }

        public static MPInput WriteInput(Part basePart, params (ID<PartToggleGuiRules> Id, int Value)[] writes)
        {
            var batch = new List<MPInput>();
            var psd = new PartSaveData(basePart);
            foreach (var (id, value) in writes)
                batch.Add(new MPSetUIToggleMetadataInput(basePart.Ship, psd, id, value, false));
            return new MPMultiInput(batch);
        }
    }
}
