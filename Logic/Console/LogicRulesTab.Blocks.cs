using System;
using System.Collections.Generic;
using Cosmoteer.Gui;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;

namespace ZTX.HingesPistonsDocking
{
    internal sealed partial class LogicRulesTab
    {
        private static float BlockPad => 4f * LogicUiScale.Rules;
        private static float BoxLine => 2f * LogicUiScale.Rules;
        private static float BoxGap => 4f * LogicUiScale.Rules;
        private static float BoxStep => 8f * LogicUiScale.Rules;
        private const string BarAlpha = "90";
        private static float s_blockInset = -1f;
        private bool _barsFailed;

        private static LayoutBox BlockFrame()
        {
            var box = new LayoutBox();
            box.NineSlice.CopySettingsFrom(WidgetRules.Instance.HollowWidget.NineSlice);
            box.Width = InnerWidth;
            box.Height = RowHeight;
            box.Children.LayoutAlgorithm = LayoutAlgorithms.TileTopLeftVertical;
            box.Children.BorderPadding = new Borders(BlockPad);
            box.Children.WidgetPadding = new Vector2(0f, GapY);
            box.Children.ClipChildren = false;
            return box;
        }

        private static float BlockInnerWidth
        {
            get
            {
                if (s_blockInset < 0f) s_blockInset = HorizontalInset(BlockFrame().NineSlice);
                return InnerWidth - s_blockInset - 2f * BlockPad;
            }
        }

        private static void FitFrame(LayoutBox box)
        {
            float h = 0f;
            int n = 0;
            foreach (Widget w in box.Children)
            {
                if (!w.SelfActive) continue;
                h += w.Height;
                n++;
            }
            box.Height = h + Math.Max(0, n - 1) * GapY + 2f * BlockPad + VerticalInset(box.NineSlice);
        }

        private static void FitBlock(RuleBlock b)
        {
            foreach (SeqBlock sb in b.Seqs) if (sb.Box != null) FitFrame(sb.Box);
            if (b.VarBox != null) FitFrame(b.VarBox);
            FitHeight(b.Box);
        }

        private void AttachBars(RuleBlock block)
        {
            RuleBlock captured = block;
            block.Box.Components.Add(new WidgetDelegatedRenderer((gfx, pixels, rect, scale) => DrawBlockBars(captured, gfx, scale), block.Box.NineSlice));
        }

        private void DrawBlockBars(RuleBlock b, GraphicsManager gfx, Color scale)
        {
            if (_barsFailed || _engine == null || !b.Complete || IsCollapsed(b.Rule)) return;
            try
            {
                Color c = LogicPalette.ToColor(LogicPalette.Set + BarAlpha) * scale;
                bool material = false;
                foreach (SeqBlock sb in b.Seqs)
                {
                    if (sb.Box == null || !sb.Box.SelfActive || !IsSeqOpen(sb.Id)) continue;
                    IReadOnlyList<int> ids = _engine.SeqStepIds(sb.Id);
                    var kinds = new List<int>(ids.Count);
                    foreach (int k in ids) kinds.Add((int)_engine.Step(k).Kind);
                    int[] partner = LogicFlow.Match(kinds);
                    int[] depth = LogicFlow.Depths(kinds);
                    for (int i = 0; i < kinds.Count; i++)
                    {
                        if (!LogicFlow.IsOpener(kinds[i]) || partner[i] < 0) continue;
                        int next = partner[i];
                        bool hasElse = kinds[next] == (int)SeqKind.Else;
                        int end = hasElse ? partner[next] : next;
                        if (end < 0) continue;
                        Widget top = ItemBox(sb, ids[i]), bottom = ItemBox(sb, ids[end]);
                        if (top == null || bottom == null || !top.SelfActive || !bottom.SelfActive) continue;
                        Rect a = top.ScreenRect, z = bottom.ScreenRect;
                        float x0 = a.X + IndentWidth + depth[i] * BlockIndent - BoxGap;
                        float x1 = a.X + a.Width - BoxGap - depth[i] * BoxStep;
                        float y0 = a.Y - GapY / 2f, y1 = z.Y + z.Height + GapY / 2f;
                        if (x1 <= x0 || y1 <= y0) continue;
                        if (!material) { gfx.SetToMaterial(null); material = true; }
                        DrawBox(gfx, x0, y0, x1, y1, c);
                        Widget divider = hasElse ? ItemBox(sb, ids[next]) : null;
                        if (divider != null && divider.SelfActive)
                            gfx.Draw<Quad>(new Quad(new Rect(x0, divider.ScreenRect.Y - GapY / 2f - BoxLine / 2f, x1 - x0, BoxLine), c));
                    }
                }
            }
            catch (Exception ex)
            {
                _barsFailed = true;
                Log.Exception("LogicRulesTab.DrawBlockBars", ex);
            }
        }

        private void DrawBox(GraphicsManager gfx, float x0, float y0, float x1, float y1, Color c)
        {
            float t = BoxLine;
            gfx.Draw<Quad>(new Quad(new Rect(x0, y0, x1 - x0, t), c));
            gfx.Draw<Quad>(new Quad(new Rect(x0, y1 - t, x1 - x0, t), c));
            gfx.Draw<Quad>(new Quad(new Rect(x0, y0, t, y1 - y0), c));
            gfx.Draw<Quad>(new Quad(new Rect(x1 - t, y0, t, y1 - y0), c));
        }

        private void LogStatementWidths()
        {
            try
            {
                foreach (RuleBlock b in _blocks)
                    foreach (SeqBlock sb in b.Seqs)
                    {
                        ActRow act = b.Acts.Find(x => x.Seq == sb);
                        StepRow step = sb.Steps.Count > 0 ? sb.Steps[0] : null;
                        if (act == null && step == null) continue;
                        Log.Info("console: statement rows declare " + (act != null ? "Set " + Declared(act.Box) + " " : "")
                            + (step != null ? "statement " + Declared(step.Box) + " " : "") + "of " + BlockInnerWidth.ToString("0") + " px.");
                        return;
                    }
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.LogStatementWidths", ex); }
        }

        private static string Declared(LayoutBox row)
        {
            float w = 0f;
            int n = 0;
            foreach (Widget c in row.DockedChildren)
            {
                if (!c.SelfActive) continue;
                w += c.Width;
                n++;
            }
            return (w + Math.Max(0, n - 1) * RowGap).ToString("0");
        }

        private static Widget ItemBox(SeqBlock sb, int id)
        {
            foreach (var item in sb.Items) if (item.Id == id) return item.Box;
            return null;
        }
    }
}
