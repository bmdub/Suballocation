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
            var allocator = new DirectionalBlockSuballocator<int>(1024, 1);

            Assert.AreEqual(1024, allocator.FreeLength);
        }

        public unsafe void Constructor2Test()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            var allocator = new DirectionalBlockSuballocator<int>(pElems, 1024, 1);

            Assert.AreEqual(1024, allocator.FreeLength);
        }

        public void Constructor3Test()
        {
            var mem = new Memory<int>(new int[1024]);

            var allocator = new DirectionalBlockSuballocator<int>(mem, 1);

            Assert.AreEqual(1024, allocator.FreeLength);
        }

        [Test]
        public void FillTest()
        {
            var allocator = new DirectionalBlockSuballocator<int>(32640, 1);

            for (int i = 1; i <= 255; i++)
            {
                var segment = allocator.Rent(i);
                Assert.AreEqual(i, segment.Length);
            }

            Assert.AreEqual(0, allocator.FreeBytes);
            Assert.AreEqual(0, allocator.FreeLength);
        }

        [Test]
        public void NonOverlapTest()
        {
            var allocator = new DirectionalBlockSuballocator<int>(32640, 1);

            List<NativeMemorySegment<int>> segments = new();

            for (int i = 1; i <= 255; i++)
            {
                var segment = allocator.Rent(i);
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

        [Test]
        public void MinBlockLengthTest()
        {
            var allocator = new DirectionalBlockSuballocator<int>(65536, 32);

            List<NativeMemorySegment<int>> segments = new();

            for (int i = 0; i < 65536 / 32; i++)
            {
                segments.Add(allocator.Rent(1));
            }

            Assert.Throws<OutOfMemoryException>(() => allocator.Rent(1));

            Assert.AreEqual(0, allocator.FreeBytes);
            Assert.AreEqual(0, allocator.FreeLength);

            foreach (var segment in segments)
            {
                allocator.Return(segment);
            }

            Assert.AreEqual(65536 * sizeof(int), allocator.FreeBytes);
            Assert.AreEqual(65536, allocator.FreeLength);
        }

        [Test]
        public void ReturnSegmentsTest()
        {
            var allocator = new DirectionalBlockSuballocator<int>(32640, 1);

            List<NativeMemorySegment<int>> segments = new();

            for (int i = 1; i <= 255; i++)
            {
                var segment = allocator.Rent(i);
                segments.Add(segment);
            }

            foreach (var segment in segments)
            {
                allocator.Return(segment);
            }

            Assert.AreEqual(32640 * sizeof(int), allocator.FreeBytes);
            Assert.AreEqual(32640, allocator.FreeLength);
        }

        [Test]
        public void ReturnSegmentsInReverseTest()
        {
            var allocator = new DirectionalBlockSuballocator<int>(32640, 1);

            List<NativeMemorySegment<int>> segments = new();

            for (int i = 1; i <= 255; i++)
            {
                var segment = allocator.Rent(i);
                segments.Add(segment);
            }

            segments.Reverse();

            foreach (var segment in segments)
            {
                allocator.Return(segment);
            }

            Assert.AreEqual(32640 * sizeof(int), allocator.FreeBytes);
            Assert.AreEqual(32640, allocator.FreeLength);
        }

        [Test]
        public void ReturnSegmentResourcesTest()
        {
            var allocator = new DirectionalBlockSuballocator<int>(32640, 1);

            List<NativeMemorySegmentResource<int>> segments = new();

            for (int i = 1; i <= 255; i++)
            {
                var segment = allocator.RentResource(i);
                segments.Add(segment);
            }

            segments.Reverse();

            foreach (var segment in segments)
            {
                segment.Dispose();
            }

            Assert.AreEqual(32640 * sizeof(int), allocator.FreeBytes);
            Assert.AreEqual(32640, allocator.FreeLength);
        }

        [Test]
        public void BoundaryTest()
        {
            long length = 100;

            var allocator = new DirectionalBlockSuballocator<int>(length, 1);

            Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Rent(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Rent(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => allocator.RentResource(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => allocator.RentResource(-1));

            var segment = allocator.Rent(100);

            allocator.Return(segment);

            Assert.Throws<OutOfMemoryException>(() => allocator.Rent(101));

            for (int i = 0; i < length; i++)
            {
                allocator.Rent(1);
            }

            Assert.Throws<OutOfMemoryException>(() => allocator.Rent(1));
        }

        [Test]
        public void ClearTest()
        {
            long length = 100;

            var allocator = new DirectionalBlockSuballocator<int>(length, 1);

            allocator.Rent(100);

            allocator.Clear();

            allocator.Rent(100);
        }

        [Test]
        public void GetEnumeratorTest()
        {
            var allocator = new SequentialBlockSuballocator<int>(32640 * 2, 2);

            HashSet<NativeMemorySegment<int>> segments = new();

            for (int i = 1; i <= 255; i++)
            {
                var segment = allocator.Rent(i);
                segments.Add(segment);
            }

            int returnCount = 0;
            var setList = segments.ToList();
            for (int i = 0; i < setList.Count; i += 3)
            {
                allocator.Return(setList[i]);
                returnCount++;
            }

            var found = allocator.ToList();

            Assert.AreEqual(segments.Count - returnCount, found.Count);

            foreach (var segment in found)
            {
                Assert.IsTrue(segments.Contains(segment));
            }
        }
    }
}