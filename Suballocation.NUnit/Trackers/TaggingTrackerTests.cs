using NUnit.Framework;
using Suballocation.Trackers;
using System;

namespace Suballocation.NUnit
{
    public class TaggingTrackerTests
    {
        [Test]
        public unsafe void GetTest()
        {
            var tracker = new TaggingTracker<int>();

            for (int i = 1; i <= 1000; i++)
            {
                tracker.RegisterUpdate(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, i * -1);
            }

            for (int i = 1; i <= 1000; i++)
            {
                bool success = tracker.TryGetTag(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, out var tag);

                Assert.IsTrue(success);

                Assert.AreEqual(i * -1, tag);

                Assert.AreEqual(i * -1, tracker[new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }]);
            }
        }

        [Test]
        public unsafe void RemovalTest()
        {
            var tracker = new TaggingTracker<int>();

            for (int i = 0; i <= 1000; i++)
            {
                tracker.RegisterUpdate(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, i * -1);
            }

            for (int i = 0; i <= 1000; i+=3)
            {
                tracker.RegisterRemoval(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 });
            }

            for (int i = 0; i <= 1000; i++)
            {
                bool success = tracker.TryGetTag(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, out var tag);

                if (i % 3 == 0)
                {
                    Assert.IsFalse(success);
                }
                else
                {
                    Assert.IsTrue(success);

                    Assert.AreEqual(i * -1, tag);

                    Assert.AreEqual(i * -1, tracker[new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }]);
                }
            }
        }

        [Test]
        public unsafe void ClearTest()
        {
            var tracker = new TaggingTracker<int>();

            for (int i = 1; i <= 1000; i++)
            {
                tracker.RegisterUpdate(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, i * -1);
            }

            tracker.Clear();

            for (int i = 1; i <= 1000; i++)
            {
                bool success = tracker.TryGetTag(new NativeMemorySegment<int>() { PElems = (int*)i, Length = 1 }, out var tag);

                Assert.IsFalse(success);
            }
        }
    }
}