using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace FauFau.Util
{
    // Some span reading helpers
    public ref struct Spanner<T> where T : IEquatable<T>
    {
        private ReadOnlySpan<T> Buffer;
        private ReadOnlySpan<T> CurrentView;
        private int             Offset;

        // Get a slice of what is left unread in the view
        public ReadOnlySpan<T> Remaining => CurrentView;

        public Spanner(ReadOnlySpan<T> Source)
        {
            Buffer      = Source;
            CurrentView = Source;
            Offset      = 0;
        }

        // Get a span untill the value is matches, if the value isn't matched will return until the end of the buffer span
        public ReadOnlySpan<T> ReadUntil(T Value)
        {
            var len = CurrentView.IndexOf(Value);
            len = len <= -1 ? CurrentView.Length : len;
            var data = CurrentView.Slice(0, len);

            // Set the view and move forward one to get rid of the splitter
            var moveForwardValue = len == CurrentView.Length ? len : len + 1;
            UpdateCurrentView(moveForwardValue);

            return data;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateCurrentView(int MoveForwardAmount)
        {
            CurrentView =  CurrentView.Slice(MoveForwardAmount);
            Offset      += MoveForwardAmount;
        }


        // Static helpers
        public ref struct KeyValuePair
        {
            public ReadOnlySpan<T> Key;
            public ReadOnlySpan<T> Value;
        }

        // Split on the given value and return the left and right values as key and value
        public static KeyValuePair SplitKVP(ReadOnlySpan<T> Source, T SplitOn)
        {
            var pos = Source.IndexOf(SplitOn);
            var kvp = new KeyValuePair
            {
                Key   = Source.Slice(0, pos),
                Value = Source.Slice(pos + 1)
            };

            return kvp;
        }

        // Get a stack allocated span of the given type if the length is below the set max stack length
        // otherwise will rent from a pool
        /*public static Span<T> AllocStackSpanOrRent(int length, int maxStackLen = 500)
        {
            if (length <= maxStackLen) {
                Span<T> span = stackalloc T[maxStackLen];
                return span;
            }
            else {
                
            }
        }*/
    }
}