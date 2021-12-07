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

            var tracker = new FragmentationTracker<int, int>(pElems, 1024, 10);

            for (int i = 100; i < 1000; i++)
            {
                tracker.TrackAddition(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, i * -1);
            }

            Assert.IsEmpty(tracker.GetFragmentedSegments(.99));

            tracker.TrackRemoval(new NativeMemorySegment<int>() { PElems = (int*)500, Length = 1 });

            Assert.IsEmpty(tracker.GetFragmentedSegments(.99));

            tracker.TrackRemoval(new NativeMemorySegment<int>() { PElems = (int*)510, Length = 1 });

            Assert.AreEqual(18, tracker.GetFragmentedSegments(.99).Count());

            tracker.TrackRemoval(new NativeMemorySegment<int>() { PElems = (int*)110, Length = 1 });
            tracker.TrackRemoval(new NativeMemorySegment<int>() { PElems = (int*)120, Length = 1 });

            Assert.AreEqual(36, tracker.GetFragmentedSegments(.99).Count());
        }

        [Test]
        public unsafe void FragmentationLength5Test()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            var tracker = new FragmentationTracker<int, int>(pElems, 1024, 10);

            for (int i = 100; i < 1000; i+=5)
            {
                tracker.TrackAddition(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 5 }, i * -1);
            }

            Assert.IsEmpty(tracker.GetFragmentedSegments(.99));

            tracker.TrackRemoval(new NativeMemorySegment<int>() { PElems = (int*)500, Length = 5 });

            Assert.IsEmpty(tracker.GetFragmentedSegments(.99));

            tracker.TrackRemoval(new NativeMemorySegment<int>() { PElems = (int*)510, Length = 5 });

            Assert.AreEqual(2, tracker.GetFragmentedSegments(.50).Count());

            tracker.TrackRemoval(new NativeMemorySegment<int>() { PElems = (int*)110, Length = 5 });
            tracker.TrackRemoval(new NativeMemorySegment<int>() { PElems = (int*)120, Length = 5 });

            Assert.AreEqual(4, tracker.GetFragmentedSegments(.50).Count());

            Assert.IsEmpty(tracker.GetFragmentedSegments(.49));
        }

        [Test]
        public unsafe void GetTagTest()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            var tracker = new FragmentationTracker<int, int>(pElems, 1024, 10);

            for (int i = 100; i < 1000; i++)
            {
                tracker.TrackAddition(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, i * -1);
            }

            for (int i = 100; i < 1000; i++)
            {
                bool success = tracker.TryGetTag(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, out var tag);

                Assert.IsTrue(success);

                Assert.AreEqual(i * -1, tag);
            }
        }

        [Test]
        public unsafe void RemovalTagTest()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            var tracker = new FragmentationTracker<int, int>(pElems, 1024, 10);

            for (int i = 100; i < 1000; i++)
            {
                tracker.TrackAddition(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, i * -1);
            }

            for (int i = 100; i < 1000; i += 3)
            {
                tracker.TrackRemoval(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 });
            }

            for (int i = 100; i < 1000; i++)
            {
                bool success = tracker.TryGetTag(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, out var tag);

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
        }

        [Test]
        public unsafe void ClearTest()
        {
            var pElems = (int*)NativeMemory.Alloc(1024, sizeof(int));

            var tracker = new FragmentationTracker<int, int>(pElems, 1024, 10);

            for (int i = 100; i < 1000; i++)
            {
                tracker.TrackAddition(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, i * -1);
            }

            tracker.Clear();

            for (int i = 100; i < 1000; i++)
            {
                bool success = tracker.TryGetTag(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, out _);

                Assert.IsFalse(success);
            }
        }
    }
}