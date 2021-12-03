using NUnit.Framework;
using Suballocation.Collections;
using System;

namespace Suballocation.NUnit
{
    public class NativeBitArrayTests
    {
        [Test]
        [TestCase(1)]
        [TestCase(64)]
        [TestCase(65)]
        public void StateMutationTest(long length)
        {
            var arr = new NativeBitArray(length);

            for (int i = 0; i < arr.Length; i++)
            {
                Assert.AreEqual(false, arr[i]);

                if (i % 3 == 0)
                {
                    arr[i] = true;
                }
            }

            for (int i = 0; i < arr.Length; i++)
            {
                if (i % 3 == 0)
                {
                    Assert.AreEqual(true, arr[i]);
                }
                else
                {
                    Assert.AreEqual(false, arr[i]);
                }
            }
        }

        [Test]
        [TestCase(1L << 33)]
        public void LargeLengthTest(long length)
        {
            var arr = new NativeBitArray(length);

            for (long i = arr.Length - 1; i > 0; i >>= 1)
            {
                Assert.AreEqual(false, arr[i]);

                arr[i] = true;
            }

            for (long i = arr.Length - 1; i > 0; i >>= 1)
            {
                Assert.AreEqual(true, arr[i]);
            }
        }

        [Test]
        [TestCase(1)]
        [TestCase(64)]
        [TestCase(65)]
        public void BoundaryTest(long length)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NativeBitArray(-1));

            var arr = new NativeBitArray(length);

            Assert.AreEqual(length, arr.Length);

            Assert.DoesNotThrow(() => arr[0] = true);
            Assert.DoesNotThrow(() => arr[length - 1] = true);

            Assert.Throws<ArgumentOutOfRangeException>(() => arr[-1] = true);
            Assert.Throws<ArgumentOutOfRangeException>(() => arr[length] = true);
        }

        [Test]
        [TestCase(1, 2)]
        [TestCase(1, 0)]
        [TestCase(64, 128)]
        [TestCase(65, 65)]
        [TestCase(65, 32)]
        public void ResizeTest(long length, long toLength)
        {
            var arr = new NativeBitArray(length);

            for (int i = 0; i < arr.Length; i++)
            {
                if (i % 3 == 0)
                {
                    arr[i] = true;
                }
            }

            arr.Resize(toLength);

            for (int i = 0; i < arr.Length; i++)
            {
                if (i % 3 == 0 && i < length)
                {
                    Assert.AreEqual(true, arr[i]);
                }
                else
                {
                    Assert.AreEqual(false, arr[i]);
                }
            }
        }

        [Test]
        [TestCase(1L << 33, 1L << 34)]
        public void ResizeLargeTest(long length, long toLength)
        {
            var arr = new NativeBitArray(length);

            arr[arr.Length - 1] = true;

            arr.Resize(toLength);

            Assert.AreEqual(true, arr[length - 1]);
            Assert.AreEqual(false, arr[arr.Length - 1]);
            Assert.AreEqual(false, arr[length]);
            Assert.AreEqual(false, arr[length - 2]);
        }

        [Test]
        [TestCase(65537)]
        public void ClearTest(long length)
        {
            var arr = new NativeBitArray(length);

            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = true;
            }

            arr.Clear();

            for (int i = 0; i < arr.Length; i++)
            {
                Assert.AreEqual(false, arr[i]);
            }
        }
    }
}