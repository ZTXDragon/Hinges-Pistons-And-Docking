using System;
using System.Collections.Generic;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Halfling.Gui;

namespace ZTX.HingesPistonsDocking
{
    internal abstract partial class RailPartWindow
    {
        private sealed class TypedRow
        {
            public LayoutBox Row;
            public TextEditField Field;
            public Func<Part, float?> Read;
            public Action<float> Write;
            public string Last;
            public Part ShownFor;
        }

        private readonly List<TypedRow> _typedRows = new List<TypedRow>();

        protected void AddTypedRow(string labelKey, string unitKey, string tipKey, Func<Part, float?> read, Action<float> write)
        {
            LayoutBox row = Row();
            row.DockedChildren.Add(RowLabel(labelKey, tipKey));
            var field = LogicGui.Typing(new TextEditField(TextEditField.Style.SingleLine) { DockMode = DockMode.Left, Width = 100f });
            if (tipKey != null) field.ToolTips.TextProvider = Strings.KeyString(tipKey);
            row.DockedChildren.Add(field);
            var unit = new Label { DockMode = DockMode.Left, Width = 80f };
            unit.TextProvider = Strings.KeyString(unitKey);
            row.DockedChildren.Add(unit);
            AddChild(row);
            var r = new TypedRow { Row = row, Field = field, Read = read, Write = write };
            field.TextChanged += (s, e) => OnTyped(r);
            _typedRows.Add(r);
        }

        protected void RefreshTypedRows()
        {
            Part p = _part;
            foreach (TypedRow r in _typedRows)
            {
                float? v = p != null ? r.Read(p) : null;
                bool has = v.HasValue;
                if (r.Row.SelfActive != has) r.Row.SelfActive = has;
                if (!has) continue;
                bool rebind = !ReferenceEquals(p, r.ShownFor);
                if (!rebind && r.Field.FocusController.IsFocused) continue;
                string text = TypedSettings.Format(v.Value);
                if (!rebind && text == r.Last) continue;
                r.ShownFor = p;
                r.Last = text;
                _refreshing = true;
                try { LogicGui.SetFieldText(r.Field, text); }
                finally { _refreshing = false; }
            }
        }

        private void OnTyped(TypedRow r)
        {
            if (_refreshing || _part == null) return;
            try
            {
                if (!TypedSettings.TryParse(r.Field.Text, out float v)) return;
                r.Write(v);
            }
            catch (Exception ex) { Log.Exception("RailPartWindow.OnTyped", ex); }
        }
    }
}
