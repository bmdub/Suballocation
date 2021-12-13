﻿using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Suballocation.Suballocators;
using System.Linq;

namespace Suballocation.NUnit
{
    public class SequentialBlockSuballocator
    {
        public void Constructor1Test()
        {
            using (var allocator = new SequentialBlockSuballocator<int>(1024, 1))
            {
                Assert.AreEqual(1024, allocator.Free);
            }
        }

        public unsafe void Constructor2Test()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            using (var allocator = new SequentialBlockSuballocator<int>(pElems, 1024, 1))
            {
                Assert.AreEqual(1024, allocator.Free);
            }
        }

        public void Constructor3Test()
        {
            var mem = new Memory<int>(new int[1024]);

            using (var allocator = new SequentialBlockSuballocator<int>(mem, 1))
            {
                Assert.AreEqual(1024, allocator.Free);
            }
        }

        [Test]
        public void FillTest()
        {
            using (var allocator = new SequentialBlockSuballocator<int>(32640, 1))
            {
                for (int i = 1; i <= 255; i++)
                {
                    var segment = allocator.Rent(i);
                    Assert.AreEqual(i, segment.Length);
                }

                Assert.AreEqual(0, allocator.FreeBytes);
                Assert.AreEqual(0, allocator.Free);
            }
        }

        [Test]
        public void NonOverlapTest()
        {
            using (var allocator = new SequentialBlockSuballocator<int>(32640, 1))
            {
                List<NativeMemorySegment<int, EmptyStruct>> segments = new();

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
        }

        [Test]
        public void MinBlockLengthTest()
        {
            using (var suballocator = new SequentialBlockSuballocator<int>(65536, 32))
            {
                List<NativeMemorySegment<int, EmptyStruct>> segments = new();

                for (int i = 0; i < 65536 / 32; i++)
                {
                    segments.Add(suballocator.Rent(1));
                }

                Assert.Throws<OutOfMemoryException>(() => suballocator.Rent(1));

                Assert.AreEqual(0, suballocator.FreeBytes);
                Assert.AreEqual(0, suballocator.Free);

                foreach (var segment in segments)
                {
                    suballocator.Return(segment);
                }

                Assert.AreEqual(65536 * sizeof(int), suballocator.FreeBytes);
                Assert.AreEqual(65536, suballocator.Free);
            }
        }

        [Test]
        public void ReturnSegmentsTest()
        {
            using (var allocator = new SequentialBlockSuballocator<int>(32640, 1))
            {
                List<NativeMemorySegment<int, EmptyStruct>> segments = new();

                for (int i = 1; i <= 255; i++)
                {
                    var segment = allocator.Rent(i);
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
            using (var allocator = new SequentialBlockSuballocator<int>(32640, 1))
            {
                List<NativeMemorySegment<int, EmptyStruct>> segments = new();

                for (int i = 1; i <= 255; i++)
                {
                    var segment = allocator.Rent(i);
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

            using (var suballocator = new SequentialBlockSuballocator<int>(length, 1))
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => suballocator.Rent(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => suballocator.Rent(-1));

                var segment = suballocator.Rent(100);

                suballocator.Return(segment);

                Assert.Throws<OutOfMemoryException>(() => suballocator.Rent(101));

                for (int i = 0; i < length; i++)
                {
                    suballocator.Rent(1);
                }

                Assert.Throws<OutOfMemoryException>(() => suballocator.Rent(1));
            }
        }

        [Test]
        public void ClearTest()
        {
            long length = 100;

            using (var allocator = new SequentialBlockSuballocator<int>(length, 1))
            {
                allocator.Rent(100);

                allocator.Clear();

                allocator.Rent(100);
            }
        }

        [Test]
        public void GetEnumeratorTest()
        {
            using (var suballocator = new SequentialBlockSuballocator<int>(32640 * 2, 2))
            {
                HashSet<NativeMemorySegment<int, EmptyStruct>> segments = new();

                for (int i = 1; i <= 255; i++)
                {
                    var segment = suballocator.Rent(i);
                    segments.Add(segment);
                }

                int returnCount = 0;
                var setList = segments.ToList();
                for (int i = 0; i < setList.Count; i += 3)
                {
                    suballocator.Return(setList[i]);
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