using System.Globalization;

namespace Editors.VfxEditor.Document
{
    /// <summary>A curve property's text, split so keys can be changed, added or removed and the rest rebuilt as written:
    /// "channel|channel:mapping|mapping", a channel "name,colour,flag;key;key", a key nine fields (docs/VFX.md section 2).</summary>
    public sealed class CurveText
    {
        public const int KeyFields = 9;
        public static readonly string[] Interpolations = ["linear", "bezier_c", "log3", "sine", "exp3", "sine_recip", "log1", "exp1", "s", "step", "bezier_q", "s_inv", "step_next"];
        public static readonly string[] FieldNames = ["time", "value", "in interpolation", "out interpolation", "in tangent x", "in tangent y", "out tangent x", "out tangent y", "flag"];

        public sealed class Channel(string header)
        {
            public string Header { get; } = header;
            public string Name => Header.Split(',')[0];
            public List<string[]> Keys { get; } = [];
        }

        string _leading = "", _trailing = "", _mapping = "";

        public List<Channel> Channels { get; } = [];
        public string Mapping => _mapping;

        /// <summary>Null for text that is not a curve: no mapping, or a key without nine fields. A channel may have no keys.</summary>
        public static CurveText? Parse(string text)
        {
            var body = text.Trim();
            var split = body.LastIndexOf(':');
            if (split < 0)
                return null;
            var curve = new CurveText
            {
                _leading = text[..(text.Length - text.TrimStart().Length)],
                _trailing = text[text.TrimEnd().Length..],
                _mapping = body[(split + 1)..],
            };
            foreach (var part in body[..split].Split('|'))
            {
                var pieces = part.Split(';');
                var channel = new Channel(pieces[0]);
                foreach (var key in pieces.Skip(1))
                {
                    var fields = key.Split(',');
                    if (fields.Length != KeyFields)
                        return null;
                    channel.Keys.Add(fields);
                }
                curve.Channels.Add(channel);
            }
            return curve;
        }

        public override string ToString() =>
            _leading + string.Join('|', Channels.Select(c => string.Join(';', c.Keys.Select(k => string.Join(',', k)).Prepend(c.Header)))) + ':' + _mapping + _trailing;

        /// <summary>A copy of key `index` at a time halfway to the next later key, or to 1 when there is none. A channel with no
        /// keys (vanilla writes them) takes a first key at 0 with the tangents vanilla uses.</summary>
        public void InsertKeyAfter(Channel channel, int index)
        {
            if (channel.Keys.Count == 0)
            {
                channel.Keys.Add(["0.0", "0.0", "linear", "linear", "-0.05", "0.0", "0.05", "0.0", "0"]);
                return;
            }
            var key = (string[])channel.Keys[index].Clone();
            var time = Number(key[0]);
            var later = channel.Keys.Select(k => Number(k[0])).Where(t => t > time).ToList();
            var next = later.Count > 0 ? later.Min() : 1f;
            key[0] = ((time + next) / 2).ToString("0.0###", CultureInfo.InvariantCulture);
            channel.Keys.Insert(index + 1, key);
        }

        /// <summary>Interpolations must be one of the 13 vanilla uses; every other field a finite number.</summary>
        public static bool IsValidField(int field, string value) => field is 2 or 3
            ? Interpolations.Contains(value)
            : float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && float.IsFinite(number);

        static float Number(string text) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
}
