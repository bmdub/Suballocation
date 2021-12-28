using NUnit.Framework;
using Suballocation.Suballocators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Suballocation.NUnit
{
    public class BuddySuballocatorTests
    {
        public void Constructor1Test()
        {
            using (var allocator = new BuddySuballocator<int>(1024, 1))
            {
                Assert.AreEqual(1024, allocator.Free);
            }
        }

        public unsafe void Constructor2Test()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            using (var allocator = new BuddySuballocator<int>(pElems, 1024, 1))
            {
                Assert.AreEqual(1024, allocator.Free);
            }

            NativeMemory.Free(pElems);
        }

        public void Constructor3Test()
        {
            var mem = new Memory<int>(new int[1024]);

            using (var allocator = new BuddySuballocator<int>(mem, 1))
            {
                Assert.AreEqual(1024, allocator.Free);
            }
        }

        [Test]
        public void FillTest()
        {
            int power = 24;

            long length = 1L << power;

            using (var allocator = new BuddySuballocator<int>(length - 1, 1))
            {
                for (int i = 0; i < power; i++)
                {
                    var segment = allocator.RentSegment(1L << i);
                    Assert.AreEqual(1L << i, segment.Length);
                }

                Assert.AreEqual(0, allocator.FreeBytes);
                Assert.AreEqual(0, allocator.Free);
            }
        }

        [Test]
        public unsafe void FillTestNonGeneric()
        {
            int power = 24;

            long length = 1L << power;

            using (var allocator = (ISuballocator)new BuddySuballocator<int>(length - 1, 1))
            {
                for (int i = 0; i < power; i++)
                {
                    var ptr = allocator.Rent(1L << (i + 2));
                    var len = allocator.GetSegmentLengthBytes(ptr);
                    Assert.AreEqual(1L << (i + 2), len);
                }

                Assert.AreEqual(0, allocator.FreeBytes);
            }
        }

        [Test]
        public void CloneTest()
        {
            int power = 24;

            long length = 1L << (power + 1);

            using (var allocator = new BuddySuballocator<int>(length - 1, 1))
            {
                for (int i = 0; i < power; i++)
                {
                    var segment = allocator.RentSegment(1L << i);
                    segment.AsSpan().Fill(i);

                    var clone = allocator.CloneSegment(segment);

                    Assert.AreEqual(segment.Length, clone.Length);
                    Assert.IsTrue(segment.AsSpan().SequenceEqual(clone.AsSpan()));
                }
            }
        }

        [Test]
        public void NonOverlapTest()
        {
            int power = 21;

            long length = 1L << power;

            using (var allocator = new BuddySuballocator<int>(length - 1, 1))
            {
                List<Segment<int>> segments = new();

                for (int i = 0; i < power; i++)
                {
                    var segment = allocator.RentSegment(1L << i);
                    segment.AsSpan().Fill(i);
                    segments.Add(segment);
                }

                for (int i = 0; i < power; i++)
                {
                    var segment = segments[i];

                    for (int j = 0; j < segment.Length; j++)
                    {
                        Assert.AreEqual(i, segment[j]);
                    }
                }
            }
        }

        [Test]
        public void MinBlockLengthTest()
        {
            int power = 24;

            long length = 1L << power;

            using (var suballocator = new BuddySuballocator<int>(length, 32))
            {
                List<Segment<int>> segments = new();

                for (int i = 0; i < length / 32; i++)
                {
                    segments.Add(suballocator.RentSegment(1));
                }

                Assert.Throws<OutOfMemoryException>(() => suballocator.RentSegment(1));

                Assert.AreEqual(0, suballocator.FreeBytes);
                Assert.AreEqual(0, suballocator.Free);

                foreach (var segment in segments)
                {
                    segment.Dispose();
                }

                Assert.AreEqual(length * sizeof(int), suballocator.FreeBytes);
                Assert.AreEqual(length, suballocator.Free);
            }
        }

        [Test]
        public void ReturnSegmentsTest()
        {
            int power = 21;

            long length = 1L << power;

            using (var allocator = new BuddySuballocator<int>(length - 1, 1))
            {
                List<Segment<int>> segments = new();

                for (int i = 0; i < power; i++)
                {
                    segments.Add(allocator.RentSegment(1L << i));
                }

                foreach (var segment in segments)
                {
                    segment.Dispose();
                }

                Assert.AreEqual((length - 1) * sizeof(int), allocator.FreeBytes);
                Assert.AreEqual((length - 1), allocator.Free);
            }
        }

        [Test]
        public void ReturnSegmentsInReverseTest()
        {
            int power = 21;

            long length = 1L << power;

            using (var allocator = new BuddySuballocator<int>(length - 1, 1))
            {
                List<Segment<int>> segments = new();

                for (int i = 0; i < power; i++)
                {
                    segments.Add(allocator.RentSegment(1L << i));
                }

                segments.Reverse();

                foreach (var segment in segments)
                {
                    segment.Dispose();
                }

                Assert.AreEqual((length - 1) * sizeof(int), allocator.FreeBytes);
                Assert.AreEqual((length - 1), allocator.Free);
            }
        }

        [Test]
        public void BoundaryTest()
        {
            long length = 100;

            using (var suballocator = new BuddySuballocator<int>(length, 1))
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => suballocator.RentSegment(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => suballocator.RentSegment(-1));

                var segment = suballocator.RentSegment(64);

                segment.Dispose();

                Assert.Throws<OutOfMemoryException>(() => suballocator.RentSegment(65));

                for (int i = 0; i < length; i++)
                {
                    suballocator.RentSegment(1);
                }

                Assert.Throws<OutOfMemoryException>(() => suballocator.RentSegment(1));
            }
        }

        [Test]
        public void ClearTest()
        {
            long length = 100;

            using (var allocator = new BuddySuballocator<int>(length, 1))
            {
                allocator.RentSegment(64);

                allocator.Clear();

                allocator.RentSegment(64);
            }
        }

        [Test]
        public unsafe void GetEnumeratorTest()
        {
            int power = 24;

            long length = 1L << power;

            using (var suballocator = new BuddySuballocator<int>(length - 1, 1))
            {
                HashSet<(IntPtr SegmentPtr, long Length)> segments = new();

                for (int i = 0; i < power; i++)
                {
                    suballocator.TryRent(1L << i, out var ptr, out _);
                    segments.Add(((IntPtr)ptr, 1L << i));
                }

                int returnCount = 0;
                var setList = segments.ToList();
                for (int i = 0; i < setList.Count; i += 3)
                {
                    Assert.AreEqual(setList[i].Length, suballocator.GetSegmentLength((int*)setList[i].SegmentPtr));

                    long segmentLength = suballocator.Return((int*)setList[i].SegmentPtr);
                    Assert.AreEqual(setList[i].Length, segmentLength);
                    returnCount++;
                }

                var found = suballocator.ToList();

                Assert.AreEqual(segments.Count - returnCount, found.Count);

                foreach (var segment in found)
                {
                    Assert.IsTrue(segments.Contains(segment));
                }
            }
        }

        [Test]
        public void GetBuddyAllocatorLengthForTest()
        {
            var length = BuddySuballocator.GetBuddyAllocatorLengthFor(128);

            Assert.AreEqual(512, length);
        }
    }
}