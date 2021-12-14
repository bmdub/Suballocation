# Suballocation

Suballocation contains a set of .NET classes that can be used to manage and suballocate or 'allocate' smaller parts of a contiguous buffer.  Each suballocator will allow you to rent and return variable-sized segments of a large, fixed block of unmanaged memory (for example.)

## Uses

The use cases for these suballocators are fairly narrow; it's worth looking at the standard .NET collections (as well as ArrayPool and MemoryPool) to see if they would suffice for your usage scenario before going down this road.  

With that out of the way, here are some reasons why these suballocators might be useful:

- You are dealing with a large, _dynamic_ collection of elements, and want to avoid pressuring the GC.
- You are restricted to using a handful of large and/or contiguous buffers for storing elements.
- The buffers are rooted in native/unmanaged memory.
- A buffer is shared with another device, requiring good locality of reference within (send fewer, smaller updates vs. many updates or large updates.)
- Your elements are not necessarily fixed in size.

The original purpose was to help manage graphics buffers for shader use; there might be uses elsewhere.

## Suballocators

- SequentialBlockSuballocator - Looks for the next available segment of memory of sufficient size head of its current location, wrapping around to the beginning of the buffer once it reaches the end.  Fast and probably good for many scenarios, but does not offer the best locality.
- BuddySuballocator - Buddy allocation algorithm or '[Buddy System](https://en.wikipedia.org/wiki/Buddy_memory_allocation).'  Good for minimizing internal fragmentation, at the cost of locality in the bad cases.
- DirectionalBlockSuballocator - Conceptually similar to the SequentialBlockSuballocator.  However, instead of searching forward, this will choose either forward or backward directions to search based on some configurable heuristic.  Tends to offer the best locality of the group, at the cost of some speed.

The suballocators will allocate pinned unmanaged memory for you.  Optionally, you can inject an externally-created buffer instead.
```CSharp
long length = 17_179_869_184; // Size in elems.
long blockLength = 32; // Larger block sizes may improve performance and footprint at the cost of internal fragmentation.
var suballocator = new SequentialBlockSuballocator<TElem>(length, blockLength);
``` 
<br/>

At the lowest level, you can request and return a segment from the suballocator like so:
```CSharp
suballocator.TryRent(length, out var segmentPtr, out var actualLength);

//... Later, you MUST return the segment explicitly when you are finished with it:

suballocator.Return(segment.SegmentPtr);
``` 

<br/>

For more flexibility, you can also request the structure representation:
```CSharp
suballocator.TryRentSegment(length, out var segment);

//... Later, you MUST return the segment explicitly when you are finished with it:

segment.Dispose();
``` 
The _Segment_ structure provides a more convenient return method (by Dispose()), as well as convenience functions for accessing the contents of the segment itself.

<br/>

When you are finished with a suballocator, you should Dispose() of it to clean up any unmanaged resources:
```CSharp
suballocator.Dispose();
``` 
There also exists a Clear() method which will allow you to reuse the suballocator.

<br/>

Some results from the PerfTest app, for renting and returning segments of variable length.  Lower is better:
```
                        Name |                                      Tag | OOM | Duration (ms) | Updates Length (avg) | Updates Spread (avg) | Updates Spread (max) | Updates (avg) |
SequentialBlockSuballocator  | Random Large                             | no  | 18.913        | 501,296              | 501,296              | 8,176,288            | 1             |
BuddySuballocator            | Random Large                             | no  | 10.184        | 3,929,603            | 3,929,603            | 6,921,216            | 1             |
DirectionalBlockSuballocator | Random Large                             | no  | 15.024        | 329,469              | 329,469              | 7,348,224            | 1             |
SequentialBlockSuballocator  | Random Large - Window Coalesce           | no  | 9.199         | 188,137              | 501,296              | 8,176,288            | 1             |
BuddySuballocator            | Random Large - Window Coalesce           | no  | 7.191         | 262,202              | 3,875,773            | 6,921,216            | 6             |
DirectionalBlockSuballocator | Random Large - Window Coalesce           | no  | 12.386        | 189,355              | 329,469              | 7,348,224            | 1             |
SequentialBlockSuballocator  | Random Larger - Window Coalesce - Defrag | no  | 14.597        | 774,230              | 4,364,197            | 8,388,608            | 3             |
BuddySuballocator            | Random Larger - Window Coalesce - Defrag | no  | 9.145         | 463,737              | 5,043,484            | 8,297,472            | 5             |
DirectionalBlockSuballocator | Random Larger - Window Coalesce - Defrag | no  | 14.277        | 601,680              | 1,092,068            | 7,519,426            | 1             |
```
_Updates Length_ is the sum of length of the update windows for a given set (in this case, we are doing 10 Rents() per set before resetting), no matter how far apart the windows are.
_Updates Spread_ is the length between the start of the lowest-addressed window to the end of the highest-addressed window for a given set.


## Trackers

There are also a couple of helper _Tracker_ classes that are useful for tracking rented segments for various purposes:
- UpdateWindowTracker - Added stuff to the suballocator, and want to know which parts of the underlying buffer changed or need to be synced?  This will figure that out for you.
- FragmentationTracker - Running into space limitations within a suballocator?  This can tell you which items would best be removed and reallocated as a means of defragmentation.

The UpdateWindowTracker is mainly useful for summarizing updates into fewer, larger windows:
```CSharp
double updateWindowFillPercentage = .2; // Any 2 segments that are 20% full when combined into 1 segment will be combined, recursively.
var windowTracker = new UpdateWindowTracker<TElem, Segment<TElem>>(updateWindowFillPercentage);

//... Later register each new segment (or updated segment, if you update the contents!)
suballocator.TryRentSegment(length, out var segment);
windowTracker.TrackRental(segment);

//... And tell the tracker whenever you return a segment:
windowTracker.TrackReturn(segment);
suballocator.Dispose();
                    
// Finally, when you are ready to process the update windows:
var updateWindows = windowTracker.BuildUpdateWindows();
//...
windowTracker.Clear(); // Reset the tracker, so that we look at future updates only.
``` 

For tracking potentially fragmented elements:
```CSharp
long fragmentBucketLength = 65536; // Divides the tracker into buckets of this length. Larger is better (but less performant when searching.)
var fragTracker = new FragmentationTracker<TElem, Segment<TElem>>(suballocator.Length, fragmentBucketLength);

//... Later register each new segment (or updated segment, if you update the contents!)
suballocator.TryRentSegment(length, out var segment);
fragTracker.TrackRental(segment);

//... And tell the tracker whenever you return a segment:
fragTracker.TrackReturn(segment);
suballocator.Dispose();
                    
// Finally, when you are ready to handle fragmented segments:
var minimumFragmentationPct = .1; // Items from buckets that are 10% empty will be returned to you.
var fragmentedSegments = fragTracker.GetFragmentedSegments(minimumFragmentationPct).ToList();
// From here, iterate over fragmentedSegments, return them, iterate again, and re-rent them (and tell all trackers of course when doing these operations.)
// This tracker should not be Clear()'d, unless you are clearing the suballocator as well.

``` 


## Gallery of Allocation Patterns

Each row of pixels, from the bottom upward, depicts buffer usage at each update window:

### SequentialBlockSuballocator
<img src="https://github.com/bmdub/Suballocation/blob/main/PerfTest/GeneratedImages/Random Large.SequentialBlockSuballocator.png" width="49%"></img>

### BuddySuballocator
<img src="https://github.com/bmdub/Suballocation/blob/main/PerfTest/GeneratedImages/Random Large.BuddySuballocator.png" width="49%"></img>

### DirectionalBlockSuballocator
<img src="https://github.com/bmdub/Suballocation/blob/main/PerfTest/GeneratedImages/Random Large.DirectionalBlockSuballocator.png" width="49%"></img>


## Todo

- BuddySuballocator: Improve performance by implementing _superblocks_. (See: Fast Allocation and Deallocation with an Improved Buddy System by Brodal, Demaine, Munro)
- SequentialBlockSuballocator and DirectionalBlockSuballocator: Currently sift through occupied segments to find the unoccupied ones; perf could be improved by avoiding those.
- Other algorithms?
