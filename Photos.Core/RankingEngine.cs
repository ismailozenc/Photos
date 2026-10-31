using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Photos.Core
{
    /// <summary>
    /// Ranking by binary insertion (ported from Lumina's rankingEngine).
    ///
    /// The library is one ordered list, best first. Photos join it one at a time: the
    /// photo being placed is compared with the middle of the list and the window it can
    /// land in is halved after each answer. A full ranking of N photos takes about
    /// N·log2(N) comparisons, and the list is always a total order, so if A beats B,
    /// everything ranked above A is also ranked above B.
    ///
    /// Photos are identified by their content signature (<see cref="FileRecord.Sig"/>), so
    /// byte-identical copies are ranked as one photo.
    /// </summary>
    [MessagePackObject]
    public sealed class RankingState
    {
        [Key(0)]
        public List<ulong> Order { get; set; } = new();

        [Key(1)]
        public bool HasInsertion { get; set; }

        [Key(2)]
        public ulong InsertionSig { get; set; }

        [Key(3)]
        public int Lo { get; set; }

        [Key(4)]
        public int Hi { get; set; }

        public RankingState Clone() => new RankingState
        {
            Order = new List<ulong>(Order),
            HasInsertion = HasInsertion,
            InsertionSig = InsertionSig,
            Lo = Lo,
            Hi = Hi,
        };
    }

    public readonly struct RankingProgress
    {
        public readonly int Ranked;
        public readonly int Total;
        /// <summary>Rough estimate of the comparisons still needed for a full ranking.</summary>
        public readonly int ComparisonsLeft;
        public readonly bool Complete;

        public RankingProgress(int ranked, int total, int comparisonsLeft, bool complete)
        {
            Ranked = ranked;
            Total = total;
            ComparisonsLeft = comparisonsLeft;
            Complete = complete;
        }
    }

    public static class RankingEngine
    {
        /// <summary>
        /// Drops removed photos, keeps the order of the rest, and starts placing the next
        /// unranked photo. The first photo ever needs no comparison, so it is placed directly.
        /// </summary>
        public static void Sync(RankingState s, IEnumerable<ulong> live, Random rnd)
        {
            var liveList = live.Distinct().ToList();
            var liveSet = new HashSet<ulong>(liveList);

            s.Order.RemoveAll(x => !liveSet.Contains(x));
            if (s.HasInsertion && !liveSet.Contains(s.InsertionSig))
                s.HasInsertion = false;

            while (true)
            {
                if (s.HasInsertion && s.Lo >= s.Hi)
                    Finish(s);
                if (s.HasInsertion)
                    return;

                var ranked = new HashSet<ulong>(s.Order);
                var unranked = liveList.Where(x => !ranked.Contains(x)).ToList();
                if (unranked.Count == 0)
                    return;

                s.HasInsertion = true;
                s.InsertionSig = unranked[rnd.Next(unranked.Count)];
                s.Lo = 0;
                s.Hi = s.Order.Count;
            }
        }

        /// <summary>The comparison to show next: <paramref name="a"/> is placed against <paramref name="b"/>.</summary>
        public static bool TryCurrentPair(RankingState s, out ulong a, out ulong b, out int mid)
        {
            a = 0;
            b = 0;
            mid = 0;
            if (!s.HasInsertion)
                return false;

            mid = (s.Lo + s.Hi) / 2;
            if (mid >= s.Order.Count)
                return false;

            a = s.InsertionSig;
            b = s.Order[mid];
            return true;
        }

        /// <summary>Records the answer to the current pair and moves on.</summary>
        public static void ApplyResult(RankingState s, ulong winner, IEnumerable<ulong> live, Random rnd)
        {
            if (!TryCurrentPair(s, out var a, out var b, out var mid))
                return;

            if (winner == a)
                s.Hi = mid; // the placed photo ranks above b
            else if (winner == b)
                s.Lo = mid + 1; // the placed photo ranks below b
            else
                return;

            Sync(s, live, rnd);
        }

        /// <summary>Moves a photo to the bottom of the ranking without comparing it.</summary>
        public static void SendToBack(RankingState s, ulong sig, IEnumerable<ulong> live, Random rnd)
        {
            var liveList = live.Distinct().ToList();
            if (!liveList.Contains(sig))
                return;

            if (s.HasInsertion && s.InsertionSig == sig)
            {
                s.HasInsertion = false;
                s.Order.Add(sig);
                Sync(s, liveList, rnd);
                return;
            }

            // Removing a ranked photo shifts the positions after it, so the search window follows.
            int removeAt = s.Order.IndexOf(sig);
            if (s.HasInsertion && removeAt >= 0)
            {
                if (s.Lo > removeAt) s.Lo--;
                if (s.Hi > removeAt) s.Hi--;
            }

            s.Order.RemoveAll(x => x == sig);
            s.Order.Add(sig);
            Sync(s, liveList, rnd);
        }

        public static RankingProgress GetProgress(RankingState s, IEnumerable<ulong> live)
        {
            int total = live.Distinct().Count();
            int ranked = s.Order.Count;

            int left = 0;
            int size = s.Order.Count;
            if (s.HasInsertion)
            {
                left += (int)Math.Ceiling(Math.Log2(s.Hi - s.Lo + 1));
                size++;
            }

            int unranked = Math.Max(0, total - ranked - (s.HasInsertion ? 1 : 0));
            for (int i = 0; i < unranked; i++)
            {
                left += (int)Math.Ceiling(Math.Log2(size + 1));
                size++;
            }

            return new RankingProgress(ranked, total, left, total > 0 && ranked == total);
        }

        static void Finish(RankingState s)
        {
            s.Order.Insert(s.Lo, s.InsertionSig);
            s.HasInsertion = false;
        }
    }

    /// <summary>Loads and saves the ranking in the settings file.</summary>
    public static class RankingStore
    {
        public static RankingState Load(Storage db) => db.Settings?.Ranking?.Clone() ?? new RankingState();

        public static void Save(Storage db, RankingState state)
        {
            var settings = db.Settings;
            if (settings == null)
                return;

            settings.Ranking = state;
            db.Settings = settings;
        }
    }
}
