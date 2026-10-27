using System;
using System.Collections.Generic;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal enum MergeState { None = 0, Out = 1, Merged = 2, Lost = 3 }

    internal struct MergeMember
    {
        public uint Hash;
        public IntVector2 Rel;
        public int RelRot;
        public bool RelFlip;
        public bool HasRotation;
        public int Parent;
    }

    internal struct MergeSetting
    {
        public int Member;
        public uint ToggleHash;
        public int Value;
    }

    internal sealed class MergeRecord
    {
        public const int FORMAT = 2;
        public const int KIND_PISTON = 1;
        public const string PREFIX = "ztx_mg_";

        public MergeState State;
        public int Kind = KIND_PISTON;
        public int Slide;
        private readonly List<MergeMember> _members = new List<MergeMember>();
        private readonly List<MergeSetting> _settings = new List<MergeSetting>();

        public int SettingCount => _settings.Count;
        public MergeSetting Setting(int i) => _settings[i];

        public void AddSetting(int member, uint toggleHash, int value)
        {
            _settings.Add(new MergeSetting { Member = member, ToggleHash = toggleHash, Value = value });
        }

        public int MemberCount => _members.Count;
        public MergeMember Member(int i) => _members[i];
        public void AddMember(string rulesId, IntVector2 rel)
        {
            _members.Add(new MergeMember { Hash = Hash(rulesId), Rel = rel, Parent = -1 });
        }

        public void AddMember(string rulesId, IntVector2 rel, int relRot, bool relFlip)
        {
            AddMemberHashed(Hash(rulesId), rel, relRot, relFlip);
        }

        public MergeRecord Keep(Func<int, bool> keep)
        {
            var r = new MergeRecord { State = State, Kind = Kind, Slide = Slide };
            var map = new int[_members.Count];
            for (int i = 0; i < _members.Count; i++)
            {
                map[i] = -1;
                if (!keep(i)) continue;
                map[i] = r._members.Count;
                r._members.Add(_members[i]);
            }
            for (int i = 0; i < r._members.Count; i++)
            {
                MergeMember m = r._members[i];
                if (m.Parent < 0) continue;
                m.Parent = m.Parent < map.Length ? map[m.Parent] : -1;
                r._members[i] = m;
            }
            foreach (MergeSetting t in _settings)
                if (t.Member >= 0 && t.Member < map.Length && map[t.Member] >= 0)
                    r._settings.Add(new MergeSetting { Member = map[t.Member], ToggleHash = t.ToggleHash, Value = t.Value });
            return r;
        }

        public void Append(MergeRecord other)
        {
            if (other == null) return;
            int offset = _members.Count;
            foreach (MergeMember m in other._members)
            {
                MergeMember c = m;
                c.Parent = m.Parent >= 0 ? m.Parent + offset : -1;
                _members.Add(c);
            }
            foreach (MergeSetting t in other._settings)
                _settings.Add(new MergeSetting { Member = t.Member + offset, ToggleHash = t.ToggleHash, Value = t.Value });
        }

        public void AddMemberHashed(uint hash, IntVector2 rel, int relRot, bool relFlip)
        {
            _members.Add(new MergeMember { Hash = hash, Rel = rel, RelRot = relRot & 3, RelFlip = relFlip, HasRotation = true, Parent = -1 });
        }

        public void SetParent(int member, int parent)
        {
            MergeMember m = _members[member];
            m.Parent = parent;
            _members[member] = m;
        }

        public static uint Hash(string s)
        {
            uint h = 2166136261u;
            if (s == null) return h;
            foreach (char c in s)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h;
        }

        public static int PackCell(IntVector2 c) => (c.X & 0xFFFF) | (c.Y << 16);
        public static IntVector2 UnpackCell(int v) => new IntVector2((short)(v & 0xFFFF), v >> 16);

        private static bool KnownFormat(int s)
        {
            int f = (s >> 8) & 15;
            return f == 1 || f == 2;
        }

        public void ClearStale(Action<string> clear, int oldMembers, int oldSettings)
        {
            for (int i = _members.Count; i < oldMembers; i++)
            {
                clear(PREFIX + "h_" + i);
                clear(PREFIX + "c_" + i);
                clear(PREFIX + "r_" + i);
                clear(PREFIX + "p_" + i);
            }
            for (int j = _settings.Count; j < oldSettings; j++)
            {
                clear(PREFIX + "tm_" + j);
                clear(PREFIX + "tt_" + j);
                clear(PREFIX + "tv_" + j);
            }
        }

        public void WriteTo(Action<string, int> set)
        {
            set(PREFIX + "s", (int)State | (Kind << 4) | (FORMAT << 8));
            set(PREFIX + "k", Slide);
            set(PREFIX + "n", _members.Count);
            for (int i = 0; i < _members.Count; i++)
            {
                MergeMember m = _members[i];
                set(PREFIX + "h_" + i, unchecked((int)m.Hash));
                set(PREFIX + "c_" + i, PackCell(m.Rel));
                set(PREFIX + "r_" + i, m.HasRotation ? (m.RelRot & 3) | (m.RelFlip ? 4 : 0) | 8 : 0);
                set(PREFIX + "p_" + i, m.Parent + 1);
            }
            set(PREFIX + "tn", _settings.Count);
            for (int j = 0; j < _settings.Count; j++)
            {
                MergeSetting t = _settings[j];
                set(PREFIX + "tm_" + j, t.Member);
                set(PREFIX + "tt_" + j, unchecked((int)t.ToggleHash));
                set(PREFIX + "tv_" + j, t.Value);
            }
        }

        public static MergeRecord ReadFrom(Func<string, int?> get)
        {
            int? s = get(PREFIX + "s");
            if (!s.HasValue || !KnownFormat(s.Value)) return null;
            bool v2 = ((s.Value >> 8) & 15) >= 2;
            var r = new MergeRecord
            {
                State = (MergeState)(s.Value & 15),
                Kind = (s.Value >> 4) & 15,
                Slide = get(PREFIX + "k") ?? 0,
            };
            int n = Math.Max(0, get(PREFIX + "n") ?? 0);
            for (int i = 0; i < n; i++)
            {
                int? h = get(PREFIX + "h_" + i);
                int? c = get(PREFIX + "c_" + i);
                if (!h.HasValue || !c.HasValue) break;
                var m = new MergeMember { Hash = unchecked((uint)h.Value), Rel = UnpackCell(c.Value), Parent = -1 };
                int pp = get(PREFIX + "p_" + i) ?? 0;
                if (pp > 0 && pp - 1 < n && pp - 1 != i) m.Parent = pp - 1;
                int rr = v2 ? get(PREFIX + "r_" + i) ?? 0 : 0;
                if ((rr & 8) != 0)
                {
                    m.RelRot = rr & 3;
                    m.RelFlip = (rr & 4) != 0;
                    m.HasRotation = true;
                }
                r._members.Add(m);
            }
            int tn = Math.Max(0, get(PREFIX + "tn") ?? 0);
            for (int j = 0; j < tn; j++)
            {
                int? tm = get(PREFIX + "tm_" + j);
                int? tt = get(PREFIX + "tt_" + j);
                int? tv = get(PREFIX + "tv_" + j);
                if (!tm.HasValue || !tt.HasValue || !tv.HasValue) break;
                if (tm.Value < 0 || tm.Value >= r._members.Count) continue;
                r._settings.Add(new MergeSetting { Member = tm.Value, ToggleHash = unchecked((uint)tt.Value), Value = tv.Value });
            }
            return r;
        }

        public static MergeState ReadState(Func<string, int?> get)
        {
            int? s = get(PREFIX + "s");
            if (!s.HasValue || !KnownFormat(s.Value)) return MergeState.None;
            return (MergeState)(s.Value & 15);
        }
    }
}
