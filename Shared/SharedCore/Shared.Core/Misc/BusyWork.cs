namespace Shared.Core.Misc
{
    /// <summary>What the app is busy with, for the busy popup to say while the UI thread is held. Each name stands until it is
    /// disposed, in any order; the last one standing is said.</summary>
    public static class BusyWork
    {
        static readonly List<Name> _names = [];

        public static string? Current
        {
            get
            {
                lock (_names)
                    return _names.Count == 0 ? null : _names[^1].Text;
            }
        }

        public static IDisposable Doing(string text)
        {
            var name = new Name(text);
            lock (_names)
                _names.Add(name);
            return name;
        }

        sealed class Name(string text) : IDisposable
        {
            public string Text { get; } = text;

            public void Dispose()
            {
                lock (_names)
                    _names.Remove(this);
            }
        }
    }
}
