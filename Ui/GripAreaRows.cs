using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Halfling.Events;
using Halfling.Gui;

namespace ZTX.HingesPistonsDocking
{
    internal abstract partial class RailPartWindow
    {
        private readonly Dictionary<int, ListItem> _areaItems = new Dictionary<int, ListItem>();
        private readonly Dictionary<int, ListItem> _positionItems = new Dictionary<int, ListItem>();
        private DropList _area, _position;
        private TextEditField _width, _length, _offset;
        private int _lastWidth = -1, _lastLength = -1, _lastOffset;
        private Part _gripBound;

        protected void AddGripArea()
        {
            AddSection("ZtxGripPanel/AreaSection");
            _area = AddGripList("ZtxGripPanel/Area", "ZtxGripPanel/TipArea", v => WriteGrip(GripStore.AreaId, v));
            _width = AddGripField("ZtxGripPanel/Width", "ZtxGripPanel/TipWidth", (s, e) => OnGripSizeChanged(_width, GripStore.WidthId, ref _lastWidth));
            _length = AddGripField("ZtxGripPanel/Length", "ZtxGripPanel/TipLength", (s, e) => OnGripSizeChanged(_length, GripStore.LengthId, ref _lastLength));
            _offset = AddGripField("ZtxGripPanel/Offset", "ZtxGripPanel/TipOffset", (s, e) => OnGripOffsetChanged());
            _position = AddGripList("ZtxGripPanel/Position", "ZtxGripPanel/TipPosition", v => WriteGrip(GripStore.PositionId, v));
            Fill(_area, _areaItems, new List<(int, string)>
            {
                (0, Text("ZtxGripPanel/AreaOff")),
                (1, Text("ZtxGripPanel/AreaOn")),
            });
            Fill(_position, _positionItems, new List<(int, string)>
            {
                (0, Text("ZtxAreaPanel/PosFront")),
                (1, Text("ZtxGripPanel/PosInLine")),
            });
        }

        protected void RefreshGripArea()
        {
            Part p = _part;
            if (p == null || _area == null) return;
            GripArea a = GripStore.Read(p);
            if (!ReferenceEquals(p, _gripBound))
            {
                _gripBound = p;
                _refreshing = true;
                try
                {
                    _lastWidth = a.Width;
                    _lastLength = a.Length;
                    _lastOffset = GripMath.ClampOffset(a.Offset, a.Width);
                    LogicGui.SetFieldText(_width, a.Width.ToString());
                    LogicGui.SetFieldText(_length, a.Length.ToString());
                    LogicGui.SetFieldText(_offset, _lastOffset.ToString());
                }
                finally { _refreshing = false; }
            }
            Select(_area, _areaItems, a.On ? 1 : 0);
            Select(_position, _positionItems, a.InLine ? 1 : 0);
        }

        private DropList AddGripList(string labelKey, string tipKey, Action<int> picked)
        {
            LayoutBox row = Row();
            row.DockedChildren.Add(RowLabel(labelKey, tipKey));
            DropList list = MakeList(FieldWidth);
            list.ToolTips.TextProvider = Strings.KeyString(tipKey);
            list.ListBox.SelectionManager.WidgetSelected += (s, e) =>
            {
                if (_refreshing || _part == null || !(e.Widget.UserData is int v)) return;
                picked(v);
            };
            row.DockedChildren.Add(list);
            AddChild(row);
            return list;
        }

        private TextEditField AddGripField(string labelKey, string tipKey, EventHandler<ValueChangedEventArgs<string>> changed)
        {
            LayoutBox row = Row();
            row.DockedChildren.Add(RowLabel(labelKey, tipKey));
            var field = LogicGui.Typing(new TextEditField(TextEditField.Style.SingleLine) { DockMode = DockMode.Left, Width = 80f });
            field.ToolTips.TextProvider = Strings.KeyString(tipKey);
            field.TextChanged += changed;
            row.DockedChildren.Add(field);
            AddChild(row);
            return field;
        }

        private void OnGripSizeChanged(TextEditField field, ID<PartToggleGuiRules> id, ref int last)
        {
            if (_refreshing || _part == null) return;
            try
            {
                if (!int.TryParse(field.Text?.Trim(), out int cells) || cells < 1) return;
                cells = GripStore.Clamp(cells);
                if (cells == last) return;
                last = cells;
                WriteGrip(id, cells);
            }
            catch (Exception ex) { Log.Exception("RailPartWindow.OnGripSizeChanged", ex); }
        }

        private void OnGripOffsetChanged()
        {
            if (_refreshing || _part == null) return;
            try
            {
                if (!int.TryParse(_offset.Text?.Trim(), out int cells)) return;
                cells = GripMath.ClampOffset(cells, GripStore.Read(_part).Width);
                if (cells == _lastOffset) return;
                _lastOffset = cells;
                WriteGrip(GripStore.OffsetId, cells);
            }
            catch (Exception ex) { Log.Exception("RailPartWindow.OnGripOffsetChanged", ex); }
        }

        private void WriteGrip(ID<PartToggleGuiRules> id, int value)
        {
            if (_part?.Ship == null) return;
            WriteValues((id, value));
            Log.Info("grip area at " + _part.Location + (Targets().Count > 1 ? " and " + (Targets().Count - 1) + " more" : "") + ": " + id + " -> " + value + ".");
        }
    }
}
