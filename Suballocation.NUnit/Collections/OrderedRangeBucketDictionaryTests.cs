using NUnit.Framework;
using Suballocation.Collections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Suballocation.NUnit
{
    public class OrderedRangeBucketDictionaryTests
    {
        [Test]
        public void FillTest()
        {
            var dict = new OrderedRangeBucketDictionary<int>(1000, 10000, 16);

            int c = 0;
            for (int i = 1000; i < 10000; )
            {
                int len = Random.Shared.Next(1, 51);

                bool success = dict.TryAdd(i, len, i + 1);

                Assert.IsTrue(success);

                i += len;
                c++;
            }

            Assert.AreEqual(c, dict.Count);
        }

        [Test]
        public void RemoveTest()
        {
            var dict = new OrderedRangeBucketDictionary<int>(1000, 10000, 32);

            List<int> lengths = new List<int>();

            for (int i = 1000; i < 10000;)
            {
                int len = Random.Shared.Next(1, 51);

                bool success = dict.TryAdd(i, len, i + 1);

                Assert.IsTrue(success);

                i += len;
                lengths.Add(len);
            }

            int index = 1000;
            foreach(int len in lengths)
            {
                bool success = dict.Remove(index, out var entry);

                Assert.IsTrue(success);
                Assert.AreEqual(index, entry.Key);
                Assert.AreEqual(index + 1, entry.Value);

                index += len;
            }

            Assert.AreEqual(0, dict.Count);
        }

        [Test]
        public void BoundaryTest()
        {
            var dict = new OrderedRangeBucketDictionary<int>(1000, 10000, 1);

            Assert.Throws<ArgumentOutOfRangeException>(() => dict.TryAdd(-1, 1, 2000));

            Assert.Throws<ArgumentOutOfRangeException>(() => dict.TryAdd(999, 1, 2000));

            Assert.Throws<ArgumentOutOfRangeException>(() => dict.TryAdd(10001, 1, 2000));

            var success = dict.TryAdd(1000, 1, 2000);
            Assert.IsTrue(success);

            success = dict.TryAdd(10000, 1, 2000);
            Assert.IsTrue(success);
        }

        [Test]
        public void DuplicateAddRemoveTest()
        {
            var dict = new OrderedRangeBucketDictionary<int>(1000, 10000, 32);

            var success = dict.TryAdd(3000, 1, 2000);
            Assert.IsTrue(success);

            success = dict.TryAdd(3000, 1, 2000);
            Assert.IsFalse(success);

            success = dict.Remove(3000, out _);
            Assert.IsTrue(success);

            success = dict.TryAdd(3000, 1, 2000);
            Assert.IsTrue(success);
        }

        [Test]
        public void GetSetTest()
        {
            var dict = new OrderedRangeBucketDictionary<int>(1000, 10000, 32);

            for (int i = 1000; i < 10000; i++)
            {
                bool success = dict.TryAdd(i, 1, i + 1);
                Assert.IsTrue(success);

                success = dict.TryGetValue(i, out var entry);
                Assert.IsTrue(success);
                Assert.AreEqual(i + 1, entry.Value);
                entry = dict[i];
                Assert.AreEqual(i + 1, entry.Value);
            }

            for (int i = 1000; i < 10000; i++)
            {
                dict[i] = new OrderedRangeBucketDictionary<int>.RangeEntry(i, 1, i + 2);

                bool success = dict.TryGetValue(i, out var entry);
                Assert.IsTrue(success);
                Assert.AreEqual(i + 2, entry.Value);
            }
        }

        [Test]
        public void GetNearestTest()
        {
            var dict = new OrderedRangeBucketDictionary<int>(1000, 10000, 32);

            for (int i = 1000; i < 10000; i++)
            {
                dict.Add(i, 1, i + 1);
            }

            int count = 0;
            long lastDistance = long.MinValue;
            foreach (var kvp in dict.GetNearest(5500))
            {
                count++;
                var distance = Math.Abs(5500 - kvp.Key);

                Assert.GreaterOrEqual(distance, lastDistance);
                lastDistance = distance;
            }

            Assert.AreEqual(9000, count);
        }

        [Test]
        public void GetRangeTest()
        {
            var dict = new OrderedRangeBucketDictionary<int>(1000, 10000, 1);

            for (int i = 1000; i < 10000; i++)
            {
                dict.Add(i, 1, i + 1);
            }

            int count = 0;
            long lastKey = 5499;
            foreach (var kvp in dict.GetRange(5500, 6500))
            {
                count++;

                Assert.GreaterOrEqual(kvp.Key, lastKey);
                lastKey = kvp.Key;
            }

            Assert.AreEqual(1001, count);
        }

        [Test]
        public void EnumerateTest()
        {
            var dict = new OrderedRangeBucketDictionary<int>(1000, 10000, 32);

            int c = 0;
            for (int i = 1000; i < 10000;)
            {
                int len = Random.Shared.Next(1, 51);

                bool success = dict.TryAdd(i, len, i + 1);

                Assert.IsTrue(success);

                i += len;
                c++;
            }

            int count = 0;
            long lastKey = long.MinValue;
            foreach (var kvp in dict)
            {
                count++;

                Assert.GreaterOrEqual(kvp.Key, lastKey);
                lastKey = kvp.Key;
            }

            Assert.AreEqual(c, count);
        }

        [Test]
        public void BucketsTest()
        {
            var dict = new OrderedRangeBucketDictionary<int>(1000, 9999, 4);

            for (int i = 1000; i < 10000; i+=2)
            {
                bool success = dict.TryAdd(i, 2, i + 1);

                Assert.IsTrue(success);
            }

            foreach(var bucket in dict.GetBuckets())
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
            var dict = new OrderedRangeBucketDictionary<int>(1000, 10000, 32);

            for (int i = 1000; i < 10000; i++)
            {
                dict.Add(i, 1, i + 1);
            }

            dict.Clear();

            Assert.AreEqual(0, dict.Count);
        }
    }
}