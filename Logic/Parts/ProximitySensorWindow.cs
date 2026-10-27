using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Gui;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(PartTriggerButton), "OnClicked")]
    internal static class ProximityPanelOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> TriggerId = new ID<PartTriggerGuiRules>("ztx_prox_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            if (!__instance.Rules.TriggerID.Equals(TriggerId)) return true;
            try
            {
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, TriggerId) == null) continue;
                    ProximitySensorWindow.Open(__instance.Game, p);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("ProximityPanelOpenPatch.Prefix", ex);
            }
            return false;
        }
    }

    internal sealed class ProximitySensorWindow : RailPartWindow
    {
        private static ProximitySensorWindow s_instance;

        private readonly Dictionary<int, ListItem> _relationItems = new Dictionary<int, ListItem>();
        private readonly Dictionary<int, ListItem> _measureItems = new Dictionary<int, ListItem>();
        private readonly Dictionary<int, ListItem> _sectorItems = new Dictionary<int, ListItem>();
        private readonly Dictionary<int, ListItem> _dockedItems = new Dictionary<int, ListItem>();
        private readonly DropList _relation, _measure, _sector, _docked;
        private readonly Label _readout;

        public static bool Shows(Part p) => s_instance != null && s_instance.IsActive && ReferenceEquals(s_instance._part, p);

        public static void Open(GameRoot game, Part sensor)
        {
            if (game == null || sensor == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new ProximitySensorWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance._part = sensor;
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
            SensorAreaOverlay.Attach(game.Sim);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private ProximitySensorWindow(GameRoot game) : base(game, "ZtxProxPanel/Title", new Vector2(480f, 360f))
        {
            _relation = AddRow("ZtxProxPanel/Relation", "ZtxProxPanel/TipRelation", ZtxProximitySensorComponent.RelationId);
            AddTypedRow("ZtxProxPanel/Range", "ZtxProxPanel/UnitTiles", "ZtxProxPanel/TipRange", ReadRange, WriteRange);
            _measure = AddRow("ZtxProxPanel/Measure", "ZtxProxPanel/TipMeasure", ZtxProximitySensorComponent.MeasureId);
            _sector = AddRow("ZtxProxPanel/Sector", "ZtxProxPanel/TipSector", ZtxProximitySensorComponent.SectorId);
            AddTypedRow("ZtxProxPanel/SizeMin", "ZtxProxPanel/UnitTilesAcross", "ZtxProxPanel/TipSizeMin", ReadSizeMin, WriteSizeMin);
            AddTypedRow("ZtxProxPanel/SizeMax", "ZtxProxPanel/UnitTilesAcross", "ZtxProxPanel/TipSizeMax", ReadSizeMax, WriteSizeMax);
            _docked = AddRow("ZtxProxPanel/Docked", "ZtxProxPanel/TipDocked", ZtxProximitySensorComponent.DockedId);
            _readout = new Label { Height = RowHeight };
            AddChild(_readout);

            var relations = new List<(int, string)>();
            for (int i = 0; i <= ZtxProximitySensorComponent.RELATION_ANY; i++) relations.Add((i, Text("ZtxProxPanel/Rel_" + i)));
            Fill(_relation, _relationItems, relations, v => "ZtxProxPanel/TipRel_" + v);
            Fill(_measure, _measureItems, Items("ZtxProxPanel/Measure_", 3), v => "ZtxProxPanel/TipMeasure_" + v);
            Fill(_sector, _sectorItems, Items("ZtxProxPanel/Sector_", 5), v => "ZtxProxPanel/TipSector");
            Fill(_docked, _dockedItems, Items("ZtxProxPanel/Docked_", 2), v => "ZtxProxPanel/TipDocked_" + v);
        }

        private static List<(int, string)> Items(string keyPrefix, int count)
        {
            var l = new List<(int, string)>();
            for (int i = 0; i < count; i++) l.Add((i, Text(keyPrefix + i)));
            return l;
        }

        private DropList AddRow(string labelKey, string tipKey, ID<PartToggleGuiRules> id)
        {
            LayoutBox row = Row();
            row.DockedChildren.Add(RowLabel(labelKey, tipKey));
            DropList list = MakeList(FieldWidth);
            list.ToolTips.TextProvider = Strings.KeyString(tipKey);
            list.ListBox.SelectionManager.WidgetSelected += (s, e) => OnPicked(id, e);
            row.DockedChildren.Add(list);
            AddChild(row);
            return list;
        }

        protected override void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (PartGone()) { SelfActive = false; return; }
                Select(_relation, _relationItems, LogicSource.Mode(_part, ZtxProximitySensorComponent.RelationId, 0));
                Select(_measure, _measureItems, LogicSource.Mode(_part, ZtxProximitySensorComponent.MeasureId, 0));
                Select(_sector, _sectorItems, LogicSource.Mode(_part, ZtxProximitySensorComponent.SectorId, 0));
                RefreshTypedRows();
                Select(_docked, _dockedItems, LogicSource.Mode(_part, ZtxProximitySensorComponent.DockedId, 0));
                string readout = Readout(_part);
                if (_readout.Text != readout) _readout.Text = readout;
            }
            catch (Exception ex)
            {
                Log.Exception("ProximitySensorWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }

        private static string Readout(Part p)
        {
            float? percent = LogicNet.ReadValue(p);
            float? units = LogicNet.ReadUnits(p);
            bool on = LogicNet.ReadOutput(p);
            int count = p.TryGetComponent<ZtxProximitySensorComponent>(new ID<PartComponentRules>("LogicOut"), out ZtxProximitySensorComponent s) ? s.InRangeCount : 0;
            return Strings.FormatText("ZtxProxPanel/ReadoutFmt",
                percent.HasValue && !float.IsNaN(percent.Value) ? ((int)percent.Value).ToString() : "-",
                units.HasValue && !float.IsNaN(units.Value) ? ((int)units.Value).ToString() : "-",
                Text(on ? "ZtxLogicPanel/SigOn" : "ZtxLogicPanel/SigOff"), count);
        }

        private static float? ReadRange(Part p)
        {
            if (PartUIToggle.GetOnPart(p, ZtxProximitySensorComponent.RangeId) == null) return null;
            return TypedSettings.ProxRange(TypedSettings.Stored(p, TypedSettings.ProxRangeId),
                LogicSource.Mode(p, ZtxProximitySensorComponent.RangeId, 3), ZtxProximitySensorComponent.RangeTiles);
        }

        private void WriteRange(float typed)
        {
            float v = Math.Max(TypedSettings.PROX_RANGE_MIN, Math.Min(TypedSettings.PROX_RANGE_MAX, typed));
            Write((ZtxProximitySensorComponent.RangeId, TypedSettings.NearestIndex(ZtxProximitySensorComponent.RangeTiles, v)));
            WriteValues((TypedSettings.ProxRangeId, TypedSettings.Encode(v, 1, TypedSettings.PROX_RANGE_MIN, TypedSettings.PROX_RANGE_MAX)));
            Log.Info("proximity sensor at " + _part?.Location + ": range " + TypedSettings.Format(v) + " tiles.");
        }

        private static float? ReadSizeMin(Part p) => SizeLimits(p, out float min, out _) ? min : (float?)null;

        private static float? ReadSizeMax(Part p) => SizeLimits(p, out _, out float max) ? max : (float?)null;

        private static bool SizeLimits(Part p, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (PartUIToggle.GetOnPart(p, ZtxProximitySensorComponent.SizeId) == null) return false;
            int mode = LogicSource.Mode(p, ZtxProximitySensorComponent.SizeId, ProximityMath.SIZE_ANY);
            int storedMin = TypedSettings.Stored(p, TypedSettings.ProxSizeMinId), storedMax = TypedSettings.Stored(p, TypedSettings.ProxSizeMaxId);
            if (TypedSettings.ProxSizeCustom(storedMin, storedMax, mode))
            {
                min = TypedSettings.ProxSizeValue(storedMin);
                max = TypedSettings.ProxSizeValue(storedMax);
                return true;
            }
            switch (mode)
            {
                case ProximityMath.SIZE_SMALL: max = ProximityMath.SMALL_BELOW; break;
                case ProximityMath.SIZE_MEDIUM: min = ProximityMath.SMALL_BELOW; max = ProximityMath.LARGE_ABOVE; break;
                case ProximityMath.SIZE_LARGE: min = ProximityMath.LARGE_ABOVE; break;
                case ProximityMath.SIZE_NOT_SMALL: min = ProximityMath.SMALL_BELOW; break;
            }
            return true;
        }

        private void WriteSizeMin(float typed) => WriteSize(typed, ReadSizeMax(_part) ?? 0f);

        private void WriteSizeMax(float typed) => WriteSize(ReadSizeMin(_part) ?? 0f, typed);

        private void WriteSize(float min, float max)
        {
            float lo = Math.Max(0f, Math.Min(TypedSettings.PROX_SIZE_MAX, min)), hi = Math.Max(0f, Math.Min(TypedSettings.PROX_SIZE_MAX, max));
            Write((ZtxProximitySensorComponent.SizeId, ProximityMath.SIZE_ANY));
            WriteValues((TypedSettings.ProxSizeMinId, TypedSettings.Encode(lo, 1, 0f, TypedSettings.PROX_SIZE_MAX)),
                        (TypedSettings.ProxSizeMaxId, hi <= 0f ? 0 : TypedSettings.Encode(hi, 1, 0f, TypedSettings.PROX_SIZE_MAX)));
            Log.Info("proximity sensor at " + _part?.Location + ": size " + TypedSettings.Format(lo) + " to " + (hi <= 0f ? "any" : TypedSettings.Format(hi)) + " tiles across.");
        }

        private void OnPicked(ID<PartToggleGuiRules> id, WidgetEventArgs e)
        {
            if (_refreshing || _part == null) return;
            try
            {
                if (!(e.Widget.UserData is int value)) return;
                if (LogicSource.Mode(_part, id, -1) == value) return;
                Write((id, value));
                Log.Info("proximity sensor at " + _part.Location + ": " + id + " -> " + value + ".");
            }
            catch (Exception ex) { Log.Exception("ProximitySensorWindow.OnPicked", ex); }
        }
    }
}
