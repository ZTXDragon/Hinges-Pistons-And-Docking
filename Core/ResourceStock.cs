using System;
using System.Collections.Generic;
using System.Linq;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Resources;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class ResourceStock
    {
        private readonly List<IResourceSource> _sources;

        public Ship Ship { get; }

        public int SourceCount => _sources?.Count ?? -1;

        private ResourceStock(Ship ship, List<IResourceSource> sources)
        {
            Ship = ship;
            _sources = sources;
        }

        public static ResourceStock Whole(Ship ship) => new ResourceStock(ship, null);

        public static ResourceStock InReach(Ship ship, Vector2 atWorld, float reachCells)
        {
            var found = new List<(IResourceSource Src, float D)>();
            if (ship?.Resources != null)
            {
                float scale = ship.Scale.X > 0f ? ship.Scale.X : 1f;
                foreach (IResourceSource src in ship.Resources.Sources)
                {
                    if (src?.Part == null || !ReferenceEquals(src.Part.Ship, ship) || src.Part.IsUnderConstruction) continue;
                    float d = (src.Part.WorldPhysicalCenter - atWorld).Length / scale;
                    if (d <= reachCells) found.Add((src, d));
                }
            }
            List<IResourceSource> ordered = found
                .OrderBy(f => f.D).ThenBy(f => f.Src.Part.Location.Y).ThenBy(f => f.Src.Part.Location.X)
                .Select(f => f.Src).ToList();
            return new ResourceStock(ship, ordered);
        }

        public static int[] PlanTake(int[] spare, int amount)
        {
            var takes = new int[spare.Length];
            int left = Math.Max(0, amount);
            for (int i = 0; i < spare.Length && left > 0; i++)
            {
                takes[i] = Math.Min(Math.Max(0, spare[i]), left);
                left -= takes[i];
            }
            return takes;
        }

        public int Total(ID<ResourceRules> type)
        {
            if (Ship?.Resources == null) return 0;
            if (_sources == null) return Ship.Resources.GetResourceTotal(type, includeStored: true, includeCarried: false);
            int total = 0;
            foreach (IResourceSource src in _sources)
                if (Usable(src, type)) total += Spare(src);
            return total;
        }

        public int Remove(ID<ResourceRules> type, int amount)
        {
            if (Ship?.Resources == null || amount <= 0) return 0;
            if (_sources == null) return Ship.Resources.RemoveResources(type, amount);
            var usable = _sources.Where(src => Usable(src, type)).ToList();
            int[] takes = PlanTake(usable.Select(Spare).ToArray(), amount);
            int removed = 0;
            for (int i = 0; i < takes.Length; i++)
                if (takes[i] > 0) removed += usable[i].SubtractResources(takes[i]);
            return removed;
        }

        public static string SourceText(int reactors, int others)
        {
            string r = reactors == 1 ? "1 reactor" : reactors + " reactors";
            string o = others == 1 ? "1 other storage" : others + " other storages";
            if (reactors > 0 && others > 0) return r + " and " + o;
            if (reactors > 0) return r;
            return others > 0 ? o : "nothing";
        }

        public string Describe(ID<ResourceRules> type)
        {
            if (_sources == null) return "the whole ship";
            int reactors = 0, others = 0;
            var seen = new HashSet<Cosmoteer.Ships.Parts.Part>();
            foreach (IResourceSource src in _sources)
            {
                if (!Usable(src, type) || Spare(src) <= 0 || !seen.Add(src.Part)) continue;
                if (src.Part.Rules.ID.ToString().IndexOf("reactor", StringComparison.OrdinalIgnoreCase) >= 0) reactors++;
                else others++;
            }
            return SourceText(reactors, others);
        }

        private static bool Usable(IResourceSource src, ID<ResourceRules> type) =>
            src?.Part != null && !src.Part.IsDestroyed && src.ResourceType == type && src.IsValidResourceSource;

        private int Spare(IResourceSource src) =>
            Math.Max(0, src.Resources - Ship.Resources.GetAnticipatedPickUp(src, MPValueType.Confirmed));
    }
}
