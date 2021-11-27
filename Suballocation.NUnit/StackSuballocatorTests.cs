using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Suballocation.Suballocators;

namespace Suballocation.NUnit
{
    public class StackSuballocatorTests
    {
        public void Constructor1Test()
        {
            var allocator = new StackSuballocator<int>(1024);

            Assert.AreEqual(1024, allocator.FreeLength);
        }

        public unsafe void Constructor2Test()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            var allocator = new StackSuballocator<int>(pElems, 1024);

            Assert.AreEqual(1024, allocator.FreeLength);
        }

        public void Constructor3Test()
        {
            var mem = new Memory<int>(new int[1024]);

            var allocator = new StackSuballocator<int>(mem);

            Assert.AreEqual(1024, allocator.FreeLength);
        }

        [Test]
        public void FillTest()
        {
            var allocator = new StackSuballocator<int>(32640);

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
            var allocator = new StackSuballocator<int>(32640);

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
        public void ReturnSegmentsInReverseTest()
        {
            var allocator = new StackSuballocator<int>(32640);

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
            var allocator = new StackSuballocator<int>(32640);

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

            var allocator = new StackSuballocator<int>(length);

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

            var allocator = new StackSuballocator<int>(length);

            allocator.Rent(100);

            allocator.Clear();

            allocator.Rent(100);
        }
    }
}