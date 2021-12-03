using NUnit.Framework;
using Suballocation.Collections;
using System;
using System.Collections.Generic;

namespace Suballocation.NUnit
{
    public class NativeHeapTests
    {
        [Test]
        public void OrderingTest()
        {
            var heap = new NativeHeap<long>();

            List<long> randomUniquevalues = new List<long>();

            for (int i = 0; i < 1000; i++)
            {
                randomUniquevalues.Add(i);
            }

            for (int i = 0; i < randomUniquevalues.Count - 1; i++)
            {
                int swapIndex = Random.Shared.Next(i + 1, randomUniquevalues.Count);

                long temp = randomUniquevalues[i];
                randomUniquevalues[i] = randomUniquevalues[swapIndex];
                randomUniquevalues[swapIndex] = temp;
            }

            for (int i = 0; i < randomUniquevalues.Count; i++)
            {
                heap.Enqueue(randomUniquevalues[i]);
            }

            long lastValue = long.MinValue;

            while(heap.TryPeek(out var value))
            {
                Assert.Less(lastValue, value);

                Assert.IsTrue(heap.TryDequeue(out value));

                Assert.Less(lastValue, value);

                lastValue = value;
            }
        }

        [Test]
        public void BoundaryTest()
        {
            var heap = new NativeHeap<long>();

            Assert.AreEqual(0, heap.Count);
            Assert.Throws<InvalidOperationException>(() => heap.Dequeue());
            Assert.Throws<InvalidOperationException>(() => heap.Peek());
            Assert.IsFalse(heap.TryDequeue(out _));
            Assert.IsFalse(heap.TryPeek(out _));

            heap.Enqueue(1);
            heap.Enqueue(2);
            heap.Enqueue(3);

            Assert.AreEqual(3, heap.Count);

            heap.Dequeue();
            heap.Dequeue();
            heap.Dequeue();

            Assert.AreEqual(0, heap.Count);
            Assert.Throws<InvalidOperationException>(() => heap.Dequeue());
            Assert.Throws<InvalidOperationException>(() => heap.Peek());
            Assert.IsFalse(heap.TryDequeue(out _));
            Assert.IsFalse(heap.TryPeek(out _));
        }

        [Test]
        public void ClearTest()
        {
            var heap = new NativeHeap<long>();

            heap.Enqueue(1);
            heap.Enqueue(2);
            heap.Enqueue(3);

            heap.Clear();

            Assert.AreEqual(0, heap.Count);
            Assert.Throws<InvalidOperationException>(() => heap.Dequeue());
            Assert.Throws<InvalidOperationException>(() => heap.Peek());
            Assert.IsFalse(heap.TryDequeue(out _));
            Assert.IsFalse(heap.TryPeek(out _));

            heap.Enqueue(6);
            heap.Enqueue(4);
            heap.Enqueue(5);

            Assert.AreEqual(4, heap.Dequeue());
            Assert.AreEqual(5, heap.Dequeue());
            Assert.AreEqual(6, heap.Dequeue());
        }
    }
}