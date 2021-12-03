using NUnit.Framework;
using Suballocation.Collections;
using System;
using System.Collections.Generic;

namespace Suballocation.NUnit
{
    public class NativeQueueTests
    {
        [Test]
        public void EnqueueDequeueTest()
        {
            var queue = new NativeQueue<long>();

            for (int i = 0; i < 1000; i++)
            {
                queue.Enqueue(i);
            }

            for (int i = 0; i < 1000; i++)
            {
                Assert.IsTrue(queue.TryPeek(out var value));
                Assert.AreEqual(i, value);
                Assert.AreEqual(i, queue.Dequeue());
            }
        }

        [Test]
        public void EnqueueHeadTest()
        {
            var queue = new NativeQueue<long>();

            for (int i = 0; i < 1000; i++)
            {
                queue.EnqueueHead(i);
            }

            for (int i = 999; i >= 0; i--)
            {
                Assert.IsTrue(queue.TryPeek(out var value));
                Assert.AreEqual(i, value);
                Assert.AreEqual(i, queue.Dequeue());
            }
        }

        [Test]
        public void BoundaryTest()
        {
            var queue = new NativeQueue<long>();

            Assert.AreEqual(0, queue.Count);
            Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
            Assert.IsFalse(queue.TryDequeue(out _));
            Assert.IsFalse(queue.TryPeek(out _));

            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);

            Assert.AreEqual(3, queue.Count);

            queue.Dequeue();
            queue.Dequeue();
            queue.Dequeue();

            Assert.AreEqual(0, queue.Count);
            Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
            Assert.IsFalse(queue.TryDequeue(out _));
            Assert.IsFalse(queue.TryPeek(out _));
        }

        [Test]
        public void ClearTest()
        {
            var queue = new NativeQueue<long>();

            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);

            queue.Clear();

            Assert.AreEqual(0, queue.Count);
            Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
            Assert.IsFalse(queue.TryDequeue(out _));
            Assert.IsFalse(queue.TryPeek(out _));

            queue.Enqueue(5);
            Assert.AreEqual(5, queue.Dequeue());
        }
    }
}