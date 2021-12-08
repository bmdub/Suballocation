using NUnit.Framework;
using Suballocation.Trackers;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Suballocation.NUnit
{
    public class FragmentationTrackerTests
    {
        [Test]
        public unsafe void FragmentationLength1Test()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            var tracker = new FragmentationTracker<int, int>(1024, 10);

            for (int i = 100; i < 1000; i++)
            {
                tracker.TrackRental(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + i, Length = 1, Tag = i * -1 });
            }

            Assert.IsEmpty(tracker.GetFragmentedSegments(.1));

            tracker.TrackReturn(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + 500, Length = 1 });

            Assert.IsEmpty(tracker.GetFragmentedSegments(.1));

            tracker.TrackReturn(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + 510, Length = 1 });

            Assert.AreEqual(18, tracker.GetFragmentedSegments(.1).Count());

            tracker.TrackReturn(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + 110, Length = 1 });
            tracker.TrackReturn(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + 120, Length = 1 });

            Assert.AreEqual(36, tracker.GetFragmentedSegments(.1).Count());
        }

        [Test]
        public unsafe void FragmentationLength5Test()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            var tracker = new FragmentationTracker<int, int>(1024, 10);

            for (int i = 100; i < 1000; i+=5)
            {
                tracker.TrackRental(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + i, Length = 5, Tag = i * -1 });
            }

            Assert.IsEmpty(tracker.GetFragmentedSegments(.1));

            tracker.TrackReturn(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + 500, Length = 5 });

            Assert.IsEmpty(tracker.GetFragmentedSegments(.1));

            tracker.TrackReturn(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + 510, Length = 5 });

            Assert.AreEqual(2, tracker.GetFragmentedSegments(.50).Count());

            tracker.TrackReturn(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + 110, Length = 5 });
            tracker.TrackReturn(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + 120, Length = 5 });

            Assert.AreEqual(4, tracker.GetFragmentedSegments(.50).Count());

            Assert.IsEmpty(tracker.GetFragmentedSegments(.51));
        }

        /*[Test]
        public unsafe void GetSegTest()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            var tracker = new FragmentationTracker<int, int>(1024, 10);

            for (int i = 100; i < 1000; i++)
            {
                tracker.TrackAddition(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + i, Length = 1, Tag = i * -1 });
            }

            for (int i = 100; i < 1000; i++)
            {
                bool success = tracker.tryg(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + i, Length = 1 }, out var tag);

                Assert.IsTrue(success);

                Assert.AreEqual(i * -1, tag);
            }
        }

        [Test]
        public unsafe void RemovalSegTest()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            var tracker = new FragmentationTracker<int, int>(1024, 10);

            for (int i = 100; i < 1000; i++)
            {
                tracker.TrackAddition(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + i, Length = 1, Tag = i * -1 });
            }

            for (int i = 100; i < 1000; i += 3)
            {
                tracker.TrackRemoval(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + i, Length = 1 });
            }

            for (int i = 100; i < 1000; i++)
            {
                bool success = tracker.TryGetTag(new NativeMemorySegment<int, int>() { PBuffer = pElems, PSegment = pElems + i, Length = 1 }, out var tag);

                if ((i - 100) % 3 == 0)
                {
                    Assert.IsFalse(success);
                }
                else
                {
                    Assert.IsTrue(success);

                    Assert.AreEqual(i * -1, tag);
                }
            }
        }*/

        [Test]
        public unsafe void ClearTest()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            var tracker = new FragmentationTracker<int, int>(1024, 10);

            for (int i = 100; i < 1000; i++)
            {
                tracker.TrackRental(new NativeMemorySegment<int, int>(pElems, pElems + i, 1));
            }

            tracker.Clear();

            Assert.IsEmpty(tracker.GetFragmentedSegments(0));
        }
    }
}