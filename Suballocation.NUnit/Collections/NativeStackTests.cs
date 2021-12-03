using NUnit.Framework;
using Suballocation.Collections;
using System;

namespace Suballocation.NUnit
{
    public class NativeStackTests
    {
        [Test]
        public void PushPopTest()
        {
            var stack = new NativeStack<long>();

            for (int i = 0; i < 1000; i++)
            {
                stack.Push(i);
            }

            for (int i = 999; i >= 0; i--)
            {
                Assert.IsTrue(stack.TryPeek(out var value));
                Assert.AreEqual(i, value);
                Assert.AreEqual(i, stack.Pop());
            }
        }

        [Test]
        public void BoundaryTest()
        {
            var stack = new NativeStack<long>();

            Assert.AreEqual(0, stack.Count);
            Assert.Throws<InvalidOperationException>(() => stack.Pop());
            Assert.IsFalse(stack.TryPop(out _));
            Assert.IsFalse(stack.TryPeek(out _));

            stack.Push(1);
            stack.Push(2);
            stack.Push(3);

            Assert.AreEqual(3, stack.Count);

            stack.Pop();
            stack.Pop();
            stack.Pop();

            Assert.AreEqual(0, stack.Count);
            Assert.Throws<InvalidOperationException>(() => stack.Pop());
            Assert.IsFalse(stack.TryPop(out _));
            Assert.IsFalse(stack.TryPeek(out _));
        }

        [Test]
        public void ClearTest()
        {
            var stack = new NativeStack<long>();

            stack.Push(1);
            stack.Push(2);
            stack.Push(3);

            stack.Clear();

            Assert.AreEqual(0, stack.Count);
            Assert.Throws<InvalidOperationException>(() => stack.Pop());
            Assert.IsFalse(stack.TryPop(out _));
            Assert.IsFalse(stack.TryPeek(out _));

            stack.Push(5);
            Assert.AreEqual(5, stack.Pop());
        }
    }
}