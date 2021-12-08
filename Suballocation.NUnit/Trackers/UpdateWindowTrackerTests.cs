using NUnit.Framework;
using Suballocation.Trackers;
using System;

namespace Suballocation.NUnit
{
    public class UpdateWindowTrackerTests
    {
        [Test]
        public unsafe void CombinedTest()
        {
            var tracker = new UpdateWindowTracker<int>(.51);

            long offset = 0;
            for (int i = 1; i <= 255; i++)
            {
                tracker.TrackAdditionOrUpdate(new NativeMemorySegment<int, EmptyStruct>() { PBuffer = (int*)1234, PSegment = (int*)offset, Length = i });
                offset += (long)(i * sizeof(int) * 1.5); // Add padding of half segment length.
            }

            var windows = tracker.BuildUpdateWindows();

            Assert.AreEqual(1, windows.Count);
        }

        [Test]
        public unsafe void NonCombinedTest()
        {
            var tracker = new UpdateWindowTracker<int>(.51);

            long offset = 0;
            for (int i = 1; i <= 255; i++)
            {
                tracker.TrackAdditionOrUpdate(new NativeMemorySegment<int, EmptyStruct>() { PBuffer = (int*)1234, PSegment = (int*)offset, Length = i });
                offset += i * sizeof(int) * 4; // Add the same length * 3 as padding between this and the next update window.
            }

            var windows = tracker.BuildUpdateWindows();

            Assert.AreEqual(255, windows.Count);
        }

        [Test]
        public unsafe void ClearTest()
        {
            var tracker = new UpdateWindowTracker<int>(.51);

            for (int i = 1; i <= 255; i++)
            {
                tracker.TrackAdditionOrUpdate(new NativeMemorySegment<int, EmptyStruct>() { PBuffer = (int*)1234, PSegment = (int*)(i * 2), Length = i });
            }

            tracker.Clear();

            var windows = tracker.BuildUpdateWindows();

            Assert.AreEqual(0, windows.Count);
        }
    }
}