using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Suballocation.Suballocators;
using System.Linq;

namespace Suballocation.NUnit
{
    public class DirectionalBlockSuballocatorTests
    {
        public void Constructor1Test()
        {
            using (var allocator = new DirectionalBlockSuballocator<int>(1024, 1))
            {
                Assert.AreEqual(1024, allocator.Free);
            }
        }

        public unsafe void Constructor2Test()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            using (var allocator = new DirectionalBlockSuballocator<int>(pElems, 1024, 1))
            {
                Assert.AreEqual(1024, allocator.Free);
            }
        }

        public void Constructor3Test()
        {
            var mem = new Memory<int>(new int[1024]);

            using (var allocator = new DirectionalBlockSuballocator<int>(mem, 1))
            {
                Assert.AreEqual(1024, allocator.Free);
            }
        }

        [Test]
        public void FillTest()
        {
            using (var allocator = new DirectionalBlockSuballocator<int>(32640, 1))
            {
                for (int i = 1; i <= 255; i++)
                {
                    var segment = allocator.RentSegment(i);
                    Assert.AreEqual(i, segment.Length);
                }

                Assert.AreEqual(0, allocator.FreeBytes);
                Assert.AreEqual(0, allocator.Free);
            }
        }

        [Test]
        public void CloneTest()
        {
            using (var allocator = new DirectionalBlockSuballocator<int>(32640 * 2, 1))
            {
                for (int i = 1; i <= 255; i++)
                {
                    var segment = allocator.RentSegment(i);
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
            using (var allocator = new DirectionalBlockSuballocator<int>(32640, 1))
            {
                List<Segment<int>> segments = new();

                for (int i = 1; i <= 255; i++)
                {
                    var segment = allocator.RentSegment(i);
                    segment.AsSpan().Fill(i);
                    segments.Add(segment);
                }

                for (int i = 1; i <= 255; i++)
                {
                    var segment = segments[i - 1];

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
            using (var suballocator = new DirectionalBlockSuballocator<int>(65536, 32))
            {
                List<Segment<int>> segments = new();

                for (int i = 0; i < 65536 / 32; i++)
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

                Assert.AreEqual(65536 * sizeof(int), suballocator.FreeBytes);
                Assert.AreEqual(65536, suballocator.Free);
            }
        }

        [Test]
        public void ReturnSegmentsTest()
        {
            using (var allocator = new DirectionalBlockSuballocator<int>(32640, 1))
            {
                List<Segment<int>> segments = new();

                for (int i = 1; i <= 255; i++)
                {
                    var segment = allocator.RentSegment(i);
                    segments.Add(segment);
                }

                foreach (var segment in segments)
                {
                    segment.Dispose();
                }

                Assert.AreEqual(32640 * sizeof(int), allocator.FreeBytes);
                Assert.AreEqual(32640, allocator.Free);
            }
        }

        [Test]
        public void ReturnSegmentsInReverseTest()
        {
            using (var allocator = new DirectionalBlockSuballocator<int>(32640, 1))
            {
                List<Segment<int>> segments = new();

                for (int i = 1; i <= 255; i++)
                {
                    var segment = allocator.RentSegment(i);
                    segments.Add(segment);
                }

                segments.Reverse();

                foreach (var segment in segments)
                {
                    segment.Dispose();
                }

                Assert.AreEqual(32640 * sizeof(int), allocator.FreeBytes);
                Assert.AreEqual(32640, allocator.Free);
            }
        }

        [Test]
        public void BoundaryTest()
        {
            long length = 100;

            using (var suballocator = new DirectionalBlockSuballocator<int>(length, 1))
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => suballocator.RentSegment(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => suballocator.RentSegment(-1));

                var segment = suballocator.RentSegment(100);

                segment.Dispose();

                Assert.Throws<OutOfMemoryException>(() => suballocator.RentSegment(101));

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

            using (var allocator = new DirectionalBlockSuballocator<int>(length, 1))
            {
                allocator.RentSegment(100);

                allocator.Clear();

                allocator.RentSegment(100);
            }
        }

        [Test]
        public unsafe void GetEnumeratorTest()
        {
            using (var suballocator = new SequentialBlockSuballocator<int>(32640 * 2, 2))
            {
                HashSet<(IntPtr SegmentPtr, long Length)> segments = new();

                for (int i = 1; i <= 255; i++)
                {
                    suballocator.TryRent(i, out var ptr, out var len);
                    segments.Add(((IntPtr)ptr, len));
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
    }
}