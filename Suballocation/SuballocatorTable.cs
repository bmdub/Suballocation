using Suballocation.Suballocators;
using System.Collections.Concurrent;

namespace Suballocation
{
    /// <summary>
    /// Collection used to match up allocated segments with their corresponding suballocator, by ID.
    /// The purpose is to minimize size and avoid any GC overhead otherwise caused by references to instances.
    /// This smells a bit, but I haven't found a better solution.
    /// </summary>
    /// <typeparam name="T">A blittable element type that defines the units to allocate.</typeparam>
    public static class SuballocatorTable<T> where T : unmanaged
    {
        private static uint _counter = 0;
        private static ConcurrentDictionary<uint, ISuballocator<T>> _suballocatorsById = new();

        /// <summary>Registers a suballocator such that it is accessible by an ID from allocated segments.</summary>
        /// <param name="suballocator">The suballocator to register.</param>
        /// <returns>The ID which can be used to reference the given suballocator.</returns>
        public static uint Register(ISuballocator<T> suballocator)
        {
            uint id;

            for(; ;)
            {
                id = Interlocked.Increment(ref _counter);

                if(_suballocatorsById.TryAdd(id, suballocator) == true)
                {
                    break;
                }
            }

            return id;
        }

        /// <summary>Removes the suballocator with the given ID from the collection.</summary>
        /// <param name="id">The ID associated with the suballocator.</param>
        public static void Deregister(uint id)
        {
            _suballocatorsById.Remove(id, out _);
        }

        /// <summary>Attempts to retrieve the suballocator associated with the given id.</summary>
        /// <param name="id">The ID associated with the suballocator.</param>
        /// <param name="suballocator">The suballocator with the given ID.</param>
        /// <returns>True if found.</returns>
        public static bool TryGetByID(uint id, out ISuballocator<T> suballocator)
        {
            return _suballocatorsById.TryGetValue(id, out suballocator!);
        }
    }
}
