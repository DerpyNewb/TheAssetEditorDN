using Shared.Core.PackFiles;
using Shared.GameFormats.DB;

namespace Editors.Audio.SoundTriggers
{
    /// <summary>The Wwise events a sound trigger names, by the sound the game plays in a battle and on the campaign map.</summary>
    public sealed record AudioTagEvents(string BattleStart, string CampaignStart);

    /// <summary>The tag-to-event lookup out of audio_metadata_tags_tables: what a SOUND_TRIGGER tag in an animation's meta file plays.
    /// A tag is matched by its exact spelling, and the first row for a key is the one the game keeps (see DbTables).</summary>
    public sealed class AudioMetadataTags
    {
        readonly Dictionary<string, AudioTagEvents> _tags;

        AudioMetadataTags(Dictionary<string, AudioTagEvents> tags) => _tags = tags;

        public static AudioMetadataTags From(IEnumerable<DbRow> rows)
        {
            var tags = new Dictionary<string, AudioTagEvents>(StringComparer.Ordinal);
            foreach (var row in rows)
                tags.TryAdd(row.Text("key"), new AudioTagEvents(row.Text("sound_event_battle_start"), row.Text("sound_event_campaign_start")));
            return new AudioMetadataTags(tags);
        }

        /// <param name="problems">As for DbTables.Read.</param>
        public static AudioMetadataTags Read(IPackFileService pfs, List<string>? problems = null) =>
            From(DbTables.Read(pfs, "audio_metadata_tags_tables", problems));

        public AudioTagEvents? Find(string tag) => _tags.GetValueOrDefault(tag);

        public int Count => _tags.Count;
    }
}
