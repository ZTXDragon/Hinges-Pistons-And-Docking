using System;
using System.Collections.Generic;
using System.Text;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class DesignStore
    {
        private const string PREFIX = "ztx_bd_";
        private const int SIGNED_OFFSET = 1 << 24;

        private static readonly ID<PartToggleGuiRules> s_lenId = new ID<PartToggleGuiRules>(PREFIX + "len");
        private static readonly ID<PartToggleGuiRules> s_nameLenId = new ID<PartToggleGuiRules>(PREFIX + "nlen");
        private static readonly ID<PartToggleGuiRules> s_genId = new ID<PartToggleGuiRules>(PREFIX + "gen");
        private static readonly ID<PartToggleGuiRules>[] s_rectIds =
        {
            new ID<PartToggleGuiRules>(PREFIX + "rx"), new ID<PartToggleGuiRules>(PREFIX + "ry"),
            new ID<PartToggleGuiRules>(PREFIX + "rw"), new ID<PartToggleGuiRules>(PREFIX + "rh"),
        };
        private static readonly List<ID<PartToggleGuiRules>> s_wordIds = new List<ID<PartToggleGuiRules>>();
        private static readonly List<ID<PartToggleGuiRules>> s_nameIds = new List<ID<PartToggleGuiRules>>();

        public static int[] ToWords(byte[] bytes)
        {
            int n = bytes?.Length ?? 0;
            var words = new int[(n + 3) / 4];
            for (int i = 0; i < n; i++) words[i / 4] |= bytes[i] << (8 * (i % 4));
            return words;
        }

        public static byte[] FromWords(int[] words, int length)
        {
            var bytes = new byte[Math.Max(0, length)];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)((words[i / 4] >> (8 * (i % 4))) & 0xFF);
            return bytes;
        }

        public static int EncodeSigned(int v) => v + SIGNED_OFFSET;

        public static int DecodeSigned(int stored) => stored - SIGNED_OFFSET;

        public static void Write(Part dock, byte[] design, string name, IntRect rect)
        {
            ShipMetadata meta = dock?.Ship?.Metadata;
            if (meta == null || design == null) return;
            var psd = new PartSaveData(dock);
            int oldWords = (Math.Max(0, (Get(meta, psd, s_lenId) ?? 1) - 1) + 3) / 4;
            int oldName = (Math.Max(0, (Get(meta, psd, s_nameLenId) ?? 1) - 1) + 3) / 4;
            int[] words = ToWords(design);
            for (int i = 0; i < words.Length; i++) Put(meta, psd, WordId(s_wordIds, "w", i), words[i]);
            for (int i = words.Length; i < oldWords; i++) Put(meta, psd, WordId(s_wordIds, "w", i), null);
            byte[] nameBytes = Encoding.UTF8.GetBytes(name ?? "");
            int[] nameWords = ToWords(nameBytes);
            for (int i = 0; i < nameWords.Length; i++) Put(meta, psd, WordId(s_nameIds, "n", i), nameWords[i]);
            for (int i = nameWords.Length; i < oldName; i++) Put(meta, psd, WordId(s_nameIds, "n", i), null);
            Put(meta, psd, s_nameLenId, nameBytes.Length + 1);
            Put(meta, psd, s_rectIds[0], EncodeSigned(rect.X));
            Put(meta, psd, s_rectIds[1], EncodeSigned(rect.Y));
            Put(meta, psd, s_rectIds[2], EncodeSigned(rect.Width));
            Put(meta, psd, s_rectIds[3], EncodeSigned(rect.Height));
            Put(meta, psd, s_lenId, design.Length + 1);
            Put(meta, psd, s_genId, (Get(meta, psd, s_genId) ?? 0) + 1);
        }

        public static int Gen(Part dock)
        {
            ShipMetadata meta = dock?.Ship?.Metadata;
            return meta == null ? 0 : Get(meta, new PartSaveData(dock), s_genId) ?? 0;
        }

        public static bool TryRead(Part dock, out byte[] design, out string name, out IntRect rect)
        {
            design = null;
            name = null;
            rect = default;
            ShipMetadata meta = dock?.Ship?.Metadata;
            if (meta == null) return false;
            var psd = new PartSaveData(dock);
            int len = (Get(meta, psd, s_lenId) ?? 0) - 1;
            if (len <= 0) return false;
            var words = new int[(len + 3) / 4];
            for (int i = 0; i < words.Length; i++) words[i] = Get(meta, psd, WordId(s_wordIds, "w", i)) ?? 0;
            design = FromWords(words, len);
            int nlen = Math.Max(0, (Get(meta, psd, s_nameLenId) ?? 1) - 1);
            var nwords = new int[(nlen + 3) / 4];
            for (int i = 0; i < nwords.Length; i++) nwords[i] = Get(meta, psd, WordId(s_nameIds, "n", i)) ?? 0;
            name = Encoding.UTF8.GetString(FromWords(nwords, nlen));
            int R(int k) => DecodeSigned(Get(meta, psd, s_rectIds[k]) ?? SIGNED_OFFSET);
            rect = new IntRect(R(0), R(1), R(2), R(3));
            return true;
        }

        public static void Clear(Part dock)
        {
            ShipMetadata meta = dock?.Ship?.Metadata;
            if (meta == null) return;
            var psd = new PartSaveData(dock);
            int words = (Math.Max(0, (Get(meta, psd, s_lenId) ?? 1) - 1) + 3) / 4;
            int names = (Math.Max(0, (Get(meta, psd, s_nameLenId) ?? 1) - 1) + 3) / 4;
            for (int i = 0; i < words; i++) Put(meta, psd, WordId(s_wordIds, "w", i), null);
            for (int i = 0; i < names; i++) Put(meta, psd, WordId(s_nameIds, "n", i), null);
            foreach (var id in s_rectIds) Put(meta, psd, id, null);
            Put(meta, psd, s_nameLenId, null);
            Put(meta, psd, s_lenId, null);
            Put(meta, psd, s_genId, (Get(meta, psd, s_genId) ?? 0) + 1);
        }

        private static ID<PartToggleGuiRules> WordId(List<ID<PartToggleGuiRules>> cache, string kind, int i)
        {
            while (cache.Count <= i) cache.Add(new ID<PartToggleGuiRules>(PREFIX + kind + cache.Count));
            return cache[i];
        }

        private static int? Get(ShipMetadata meta, PartSaveData psd, ID<PartToggleGuiRules> id) => meta.PartUIToggleStates.Get((psd, id));

        private static void Put(ShipMetadata meta, PartSaveData psd, ID<PartToggleGuiRules> id, int? value)
        {
            meta.PartUIToggleStates.Set((psd, id), value);
            meta.PartUIToggleStates.Set((psd, id), value, blueprintMode: true);
        }
    }
}
