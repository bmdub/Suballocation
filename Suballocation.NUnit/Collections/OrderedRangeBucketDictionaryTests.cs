﻿using NUnit.Framework;
using Suballocation.Collections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Suballocation.NUnit
{
    public class OrderedRangeBucketDictionaryTests
    {
        private class Range : IRangedEntry
        {
            public long RangeOffset { get; init; }
            public long RangeLength { get; init; }

            public Range(long rangeOffset, long rangeLength)
            {
                RangeOffset = rangeOffset;
                RangeLength = rangeLength;
            }
        }

        [Test]
        public void FillTest()
        {
            var dict = new OrderedRangeBucketDictionary<Range>(1000, 10000, 16);

            int c = 0;
            for (int i = 1000; i < 10000;)
            {
                int len = Random.Shared.Next(1, 51);

                bool success = dict.TryAdd(new Range(i, len));

                Assert.IsTrue(success);

                i += len;
                c++;
            }

            Assert.AreEqual(c, dict.Count);
        }

        [Test]
        public void RemoveTest()
        {
            var dict = new OrderedRangeBucketDictionary<Range>(1000, 10000, 32);

            List<int> lengths = new List<int>();

            for (int i = 1000; i < 10000;)
            {
                int len = Random.Shared.Next(1, 51);

                bool success = dict.TryAdd(new Range(i, len));

                Assert.IsTrue(success);

                i += len;
                lengths.Add(len);
            }

            int index = 1000;
            foreach (int len in lengths)
            {
                bool success = dict.Remove(index, out var entry);

                Assert.IsTrue(success);
                Assert.AreEqual(index, entry.RangeOffset);

                index += len;
            }

            Assert.AreEqual(0, dict.Count);
        }

        [Test]
        public void BoundaryTest()
        {
            var dict = new OrderedRangeBucketDictionary<Range>(1000, 10000, 1);

            Assert.Throws<ArgumentOutOfRangeException>(() => dict.TryAdd(new Range(-1, 1)));

            Assert.Throws<ArgumentOutOfRangeException>(() => dict.TryAdd(new Range(999, 1)));

            Assert.Throws<ArgumentOutOfRangeException>(() => dict.TryAdd(new Range(10001, 1)));

            var success = dict.TryAdd(new Range(1000, 1));
            Assert.IsTrue(success);

            success = dict.TryAdd(new Range(10000, 1));
            Assert.IsTrue(success);
        }

        [Test]
        public void DuplicateAddRemoveTest()
        {
            var dict = new OrderedRangeBucketDictionary<Range>(1000, 10000, 32);

            var success = dict.TryAdd(new Range(3000, 1));
            Assert.IsTrue(success);

            success = dict.TryAdd(new Range(3000, 1));
            Assert.IsFalse(success);

            success = dict.Remove(3000, out _);
            Assert.IsTrue(success);

            success = dict.TryAdd(new Range(3000, 1));
            Assert.IsTrue(success);
        }

        [Test]
        public void GetSetTest()
        {
            var dict = new OrderedRangeBucketDictionary<Range>(1000, 10000, 32);

            for (int i = 1000; i < 10000; i++)
            {
                bool success = dict.TryAdd(new Range(i, 1));
                Assert.IsTrue(success);

                success = dict.TryGetValue(i, out var entry);
                Assert.IsTrue(success);
                Assert.AreEqual(i, entry.RangeOffset);
                entry = dict[i];
                Assert.AreEqual(i, entry.RangeOffset);
            }

            for (int i = 1000; i < 10000; i++)
            {
                dict[i] = new Range(i, 1);

                bool success = dict.TryGetValue(i, out var entry);
                Assert.IsTrue(success);
                Assert.AreEqual(i, entry.RangeOffset);
            }
        }

        [Test]
        public void GetNearestTest()
        {
            var dict = new OrderedRangeBucketDictionary<Range>(1000, 10000, 32);

            for (int i = 1000; i < 10000; i++)
            {
                dict.Add(new Range(i, 1));
            }

            int count = 0;
            long lastDistance = long.MinValue;
            foreach (var kvp in dict.GetNearest(5500))
            {
                count++;
                var distance = Math.Abs(5500 - kvp.RangeOffset);

                Assert.GreaterOrEqual(distance, lastDistance);
                lastDistance = distance;
            }

            Assert.AreEqual(9000, count);
        }

        [Test]
        public void GetRangeTest()
        {
            var dict = new OrderedRangeBucketDictionary<Range>(1000, 10000, 1);

            for (int i = 1000; i < 10000; i++)
            {
                dict.Add(new Range(i, 1));
            }

            int count = 0;
            long lastKey = 5499;
            foreach (var kvp in dict.GetRange(5500, 6500))
            {
                count++;

                Assert.GreaterOrEqual(kvp.RangeOffset, lastKey);
                lastKey = kvp.RangeOffset;
            }

            Assert.AreEqual(1001, count);
        }

        [Test]
        public void EnumerateTest()
        {
            var dict = new OrderedRangeBucketDictionary<Range>(1000, 10000, 32);

            int c = 0;
            for (int i = 1000; i < 10000;)
            {
                int len = Random.Shared.Next(1, 51);

                bool success = dict.TryAdd(new Range(i, len));

                Assert.IsTrue(success);

                i += len;
                c++;
            }

            int count = 0;
            long lastKey = long.MinValue;
            foreach (var kvp in dict)
            {
                count++;

                Assert.GreaterOrEqual(kvp.RangeOffset, lastKey);
                lastKey = kvp.RangeOffset;
            }

            Assert.AreEqual(c, count);
        }

        [Test]
        public void BucketsTest()
        {
            var dict = new OrderedRangeBucketDictionary<Range>(1000, 9999, 4);

            for (int i = 1000; i < 10000; i += 2)
            {
                bool success = dict.TryAdd(new Range(i, 2));

                Assert.IsTrue(success);
            }

            foreach (var bucket in dict.GetBuckets())
            {
                Assert.AreEqual(1.0, bucket.FillPct);
            }

            dict.Remove(2000, out _);

            var halfCount = dict.GetBuckets().Where(bucket => bucket.FillPct == .5).Count();

            Assert.AreEqual(1, halfCount);
        }

        [Test]
        public void ClearTest()
        {
            var dict = new OrderedRangeBucketDictionary<Range>(1000, 10000, 32);

            for (int i = 1000; i < 10000; i++)
            {
                dict.Add(new Range(i, 1));
            }

            dict.Clear();

            Assert.AreEqual(0, dict.Count);
        }
    }
}