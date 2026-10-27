using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Rendering;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailArcArt
    {
        public static List<ManagedAtlasQuad> Create(Ship ship, RailArc.Shape shape, ZtxRailArcRules rules)
        {
            var made = new List<ManagedAtlasQuad>();
            ShipRenderer renderer = ship?.Renderer;
            if (renderer == null || rules == null) return made;
            List<RailArc.ArtQuad> pieces = RailArc.ArtQuads(shape, rules.BandHalfWidth, rules.SegmentLength);
            Add(made, renderer.GetLayerQuads(rules.BedLayer), rules.BedSprite, pieces, ship.WorldUniformScale);
            Add(made, renderer.GetLayerQuads(rules.LineLayer), rules.LineSprite, pieces, ship.WorldUniformScale);
            return made;
        }

        public static void Destroy(List<ManagedAtlasQuad> quads)
        {
            foreach (ManagedAtlasQuad q in quads) q?.Dispose();
            quads.Clear();
        }

        private static bool s_warnedNoFile;

        private static void Add(List<ManagedAtlasQuad> made, AtlasQuadManager layer, AtlasSprite sprite, List<RailArc.ArtQuad> pieces, float shipScale)
        {
            if (layer == null || sprite == null) return;
            if ((sprite.Files == null || sprite.Files.Length == 0) && !s_warnedNoFile)
            {
                s_warnedNoFile = true;
                Log.Error("Curve Rail: a curve sprite has no image File (rail_curve.rules CurveArc LineSprite / BedSprite must use File = \"...\") -- the curve is invisible.");
            }
            foreach (RailArc.ArtQuad p in pieces)
            {
                var q = new AtlasQuad(new Rect(p.P1, new Vector2(1f, 1f)), 0f, 1f, sprite, shipScale);
                Set(ref q.V1, p.P1, p.U0, 0f, sprite);
                Set(ref q.V2, p.P2, p.U1, 0f, sprite);
                Set(ref q.V3, p.P3, p.U0, 1f, sprite);
                Set(ref q.V4, p.P4, p.U1, 1f, sprite);
                made.Add(layer.CreateQuad(q, sprite.SubLayerIndex));
            }
        }

        private static void Set(ref AtlasVert v, Vector2 p, float u, float vv, AtlasSprite sprite)
        {
            v.Location.XY = p;
            v.Center.XY = p;
            v.RotateAround.XY = p;
            Rect uv = sprite.UVRect;
            v.AnimTextureUV = new Vector2(uv.X + uv.Width * u, uv.Y + uv.Height * vv);
            v.SpriteUV = new Vector2(u, vv);
        }
    }
}
