using NUnit.Framework;
using Suballocation.Collections;
using System;

namespace Suballocation.NUnit
{
    public class BigArrayTests
    {
        [Test]
        public void GetSetTest()
        {
            var arr = new BigArray<long>(12);

            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = i + 1;
            }

            for (int i = 0; i < arr.Length; i++)
            {
                Assert.AreEqual(i + 1, arr[i]);
            }
        }

        [Test]
        public void GetSetLargeTest()
        {
            var arr = new BigArray<byte>(1L << 32);

            for (int i = 0; i < arr.Length; i+=65536)
            {
                arr[i] = 2;
            }

            for (int i = 0; i < arr.Length; i += 65536)
            {
                Assert.AreEqual(2, arr[i]);
            }
        }

        [Test]
        public void BoundaryTest()
        {
            var arr = new BigArray<sbyte>((1L << 32) + 1);

            Assert.Throws<IndexOutOfRangeException>(() => arr[(1L << 32) + 1] = 123);
            Assert.DoesNotThrow(() => arr[(1L << 32) + 0] = 123);
            Assert.AreEqual(123, arr[1L << 32]);
            Assert.Throws<IndexOutOfRangeException>(() => arr[-1] = 123);
            Assert.DoesNotThrow(() => arr[0] = 121);
            Assert.AreEqual(121, arr[0]);

            arr[268_435_456 - 1] = -5;
            arr[268_435_456 + 0] = 5;
            arr[268_435_456 + 1] = 55;
        }

        [Test]
        public void ClearTest()
        {
            var arr = new BigArray<byte>((1L << 32) + 1);

            arr[0] = 1;
            arr[1] = 1;
            arr[268_435_456 - 1] = 1;
            arr[268_435_456 + 0] = 1;
            arr[268_435_456 + 1] = 1;
            arr[1L << 32] = 1;

            arr.Clear();

            Assert.AreEqual(0, arr[0]);
            Assert.AreEqual(0, arr[1]);
            Assert.AreEqual(0, arr[268_435_456 - 1]);
            Assert.AreEqual(0, arr[268_435_456 + 0]);
            Assert.AreEqual(0, arr[268_435_456 + 1]);
            Assert.AreEqual(0, arr[1L << 32]);
        }
    }
}