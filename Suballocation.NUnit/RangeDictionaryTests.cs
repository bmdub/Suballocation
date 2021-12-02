using NUnit.Framework;
using Suballocation.Collections;
using System;
using System.Collections.Generic;

namespace Suballocation.NUnit
{
    public class RangeDictionaryTests
    {
        [Test]
        public void FillTest()
        {
            var dict = new RangeDictionary<int>(1000, 10000);

            for (int i = 1000; i < 10000; i++)
            {
                bool success = dict.TryAdd(i, i + 1);

                Assert.IsTrue(success);
            }

            Assert.AreEqual(9000, dict.Count);
        }

        [Test]
        public void RemoveTest()
        {
            var dict = new RangeDictionary<int>(1000, 10000);

            for (int i = 1000; i < 10000; i++)
            {
                dict.TryAdd(i, i + 1);
            }

            for (int i = 1000; i < 10000; i++)
            {
                bool success = dict.Remove(i, out var value);

                Assert.IsTrue(success);
                Assert.AreEqual(i + 1, value);
            }

            Assert.AreEqual(0, dict.Count);
        }

        [Test]
        public void BoundaryTest()
        {
            var dict = new RangeDictionary<int>(1000, 10000);

            Assert.Throws<ArgumentOutOfRangeException>(() => dict.TryAdd(-1, 2000));

            Assert.Throws<ArgumentOutOfRangeException>(() => dict.TryAdd(999, 2000));

            Assert.Throws<ArgumentOutOfRangeException>(() => dict.TryAdd(10001, 2000));

            var success = dict.TryAdd(1000, 2000);
            Assert.IsTrue(success);

            success = dict.TryAdd(10000, 2000);
            Assert.IsTrue(success);
        }

        [Test]
        public void DuplicateAddRemoveTest()
        {
            var dict = new RangeDictionary<int>(1000, 10000);

            var success = dict.TryAdd(3000, 2000);
            Assert.IsTrue(success);

            success = dict.TryAdd(3000, 2000);
            Assert.IsFalse(success);

            success = dict.Remove(3000, out var value);
            Assert.IsTrue(success);

            success = dict.TryAdd(3000, 2000);
            Assert.IsTrue(success);
        }

        [Test]
        public void GetSetTest()
        {
            var dict = new RangeDictionary<int>(1000, 10000);

            for (int i = 1000; i < 10000; i++)
            {
                bool success = dict.TryAdd(i, i + 1);
                Assert.IsTrue(success);

                success = dict.TryGetValue(i, out var value);
                Assert.IsTrue(success);
                Assert.AreEqual(i + 1, value);

                value = dict[i];
                Assert.AreEqual(i + 1, value);
            }

            for (int i = 1000; i < 10000; i++)
            {
                dict[i] = i + 2;

                bool success = dict.TryGetValue(i, out var value);
                Assert.IsTrue(success);
                Assert.AreEqual(i + 2, value);
            }
        }

        [Test]
        public void GetNearestTest()
        {
            var dict = new RangeDictionary<int>(1000, 10000);

            for (int i = 1000; i < 10000; i++)
            {
                dict.Add(i, i + 1);
            }

            int count = 0;
            long lastDistance = long.MaxValue;
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
            var dict = new RangeDictionary<int>(1000, 10000);

            for (int i = 1000; i < 10000; i++)
            {
                dict.Add(i, i + 1);
            }

            int count = 0;
            long lastKey = long.MaxValue;
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
            var dict = new RangeDictionary<int>(1000, 10000);

            for (int i = 1000; i < 10000; i++)
            {
                dict.Add(i, i + 1);
            }

            int count = 0;
            long lastKey = long.MinValue;
            foreach (var kvp in dict)
            {
                count++;

                Assert.GreaterOrEqual(kvp.Key, lastKey);
                lastKey = kvp.Key;
            }

            Assert.AreEqual(9000, count);
        }

        [Test]
        public void ClearTest()
        {
            var dict = new RangeDictionary<int>(1000, 10000);

            for (int i = 1000; i < 10000; i++)
            {
                dict.Add(i, i + 1);
            }

            dict.Clear();

            Assert.AreEqual(0, dict.Count);
        }
    }
}