using Suballocation.Suballocators;
using System.Collections.Concurrent;

namespace Suballocation
{
    /// <summary>
    /// Collection used to match up allocated segments with their corresponding suballocator, by ID.
    /// The purpose is to minimize size and avoid any GC overhead otherwise caused by references to instances.
    /// This smells a bit, but I haven't found a better solution.
    /// </summary>
    /// <typeparam name="TSeg">A blittable element type that defines the units to allocate.</typeparam>
    /// <typeparam name="TTag">Type to be tied to each segment, as a separate entity from the segment contents. Use 'EmptyStruct' if none is desired.</typeparam>
    public static class SuballocatorTable<TSeg, TTag> where TSeg : unmanaged
    {
        private static ConcurrentDictionary<IntPtr, ISuballocator<TSeg, TTag>> _suballocatorsById = new();

        /// <summary>Registers a suballocator such that it is accessible by an ID from allocated segments.</summary>
        /// <param name="suballocator">The suballocator to register.</param>
        /// <returns>The ID which can be used to reference the given suballocator.</returns>
        public static unsafe void Register(ISuballocator<TSeg, TTag> suballocator)
        {
            if(suballocator.PElems == null)
            {
                throw new ArgumentException($"Suballocator must have an allocated buffer pointer.");
            }

            var ptr = (IntPtr)suballocator.PElems;

            if (_suballocatorsById.TryAdd(ptr, suballocator) == false)
            {
                throw new ArgumentException($"A suballocator with the given buffer location has already been registered.");
            }
        }

        /// <summary>Removes the suballocator with the given ID from the collection.</summary>
        /// <param name="id">The ID associated with the suballocator.</param>
        public static unsafe void Deregister(ISuballocator<TSeg, TTag> suballocator)
        {
            var ptr = (IntPtr)suballocator.PElems;

            if(_suballocatorsById.Remove(ptr, out _) == false)
            {
                throw new ArgumentException($"Cannot deregister an unregistered suballocator.");
            }
        }

        /// <summary>Attempts to retrieve the suballocator associated with the given buffer pointer.</summary>
        /// <param name="ptr">The pointer to the beginning of a registered suballocator's buffer.</param>
        /// <param name="suballocator">The suballocator with the given buffer.</param>
        /// <returns>True if found.</returns>
        public static bool TryGetByBufferAddress(IntPtr ptr, out ISuballocator<TSeg, TTag> suballocator)
        {
            return _suballocatorsById.TryGetValue(ptr, out suballocator!);
        }
    }
}
