using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.SoundTriggers
{
    /// <summary>What one firing of a sound trigger can play, as a tree of Wwise containers cut down to the parts that matter.</summary>
    public abstract record SoundNode;

    /// <summary>One streamed WEM, by source id.</summary>
    public sealed record SoundLeaf(uint SourceId) : SoundNode;

    /// <summary>Everything under it plays together: a blend container, an actor mixer, or several Play actions on one event.</summary>
    public sealed record SoundAll(IReadOnlyList<SoundNode> Children) : SoundNode;

    /// <summary>One child plays per firing: a random or sequence container.</summary>
    public sealed record SoundOneOf(IReadOnlyList<SoundNode> Children) : SoundNode;

    public static class SoundNodes
    {
        /// <summary>Every source id under the node, for decoding ahead of time.</summary>
        public static IEnumerable<uint> Leaves(SoundNode node) => node switch
        {
            SoundLeaf leaf => [leaf.SourceId],
            SoundAll all => all.Children.SelectMany(Leaves),
            SoundOneOf oneOf => oneOf.Children.SelectMany(Leaves),
            _ => [],
        };

        /// <summary>The source ids one firing plays: every child of a SoundAll, one random child of a SoundOneOf.</summary>
        public static IReadOnlyList<uint> Pick(SoundNode node, Random random) => node switch
        {
            SoundLeaf leaf => [leaf.SourceId],
            SoundAll all => all.Children.SelectMany(child => Pick(child, random)).ToList(),
            SoundOneOf { Children.Count: > 0 } oneOf => Pick(oneOf.Children[random.Next(oneOf.Children.Count)], random),
            _ => [],
        };
    }

    /// <summary>A switch group the resolved sound listens to: its case ids in the container's own order and the one it plays when nothing is chosen.</summary>
    public sealed record SwitchGroupSeen(uint GroupId, uint DefaultSwitch, IReadOnlyList<uint> Values);

    /// <param name="Silent">Why nothing plays, in words fit for a status line; null when Sound is set.</param>
    public sealed record SoundTriggerResult(string Tag, SoundNode? Sound, string? Silent, IReadOnlyList<SwitchGroupSeen> Switches);

    /// <summary>Turns a SOUND_TRIGGER tag into the sound it plays: the tag's row names an event, the event's Play actions name
    /// containers, and the containers lead down to WEM files. Pure lookups through delegates, so it needs no audio repository.</summary>
    /// <param name="tagRow">The audio_metadata_tags_tables row for a tag, or null when it has none.</param>
    /// <param name="hirc">The Wwise object with an id, or null when no loaded bank has it.</param>
    /// <param name="wemExists">Whether the WEM for a source id can be found. WH3's WEMs are all streamed, so this is the only source check.</param>
    public sealed class SoundTriggerResolver(Func<string, AudioTagEvents?> tagRow, Func<uint, HircItem?> hirc, Func<uint, bool> wemExists)
    {
        /// <param name="switchChoices">The switch case to play, by group id. A group not in it plays its default.</param>
        public SoundTriggerResult Resolve(string tag, bool campaign, IReadOnlyDictionary<uint, uint> switchChoices)
        {
            var row = tagRow(tag);
            if (row == null)
                return Silent(tag, "no audio_metadata_tags_tables row");

            var eventName = campaign ? row.CampaignStart : row.BattleStart;
            if (string.IsNullOrWhiteSpace(eventName))
                return Silent(tag, campaign ? "the row names no campaign event" : "the row names no battle event");

            var item = hirc(WwiseHash.Compute(eventName));
            if (item == null)
                return Silent(tag, $"event {eventName} is in no loaded bank");
            if (item is not ICAkEvent actionEvent)
                return Silent(tag, $"not supported: {item.HircType}");

            var walk = new Walk(hirc, wemExists, switchChoices);
            var plays = new List<SoundNode>();
            var playActions = 0;
            foreach (var actionId in actionEvent.GetActionIds())
            {
                if (hirc(actionId) is not ICAkAction action || action.GetActionType() != AkActionType.Play)
                    continue;

                playActions++;
                if (walk.Visit(action.GetChildId()) is { } node)
                    plays.Add(node);
            }

            // When nothing is left, the first thing that went wrong (a type we do not play, or a missing WEM) is the reason.
            // With nothing wrong either, the event has no Play action, or its Play actions lead to nothing that is loaded.
            var sound = Combine(plays, oneOf: false);
            var silent = sound != null ? null : walk.Problem ?? (playActions == 0
                ? $"event {eventName} has no Play action"
                : $"event {eventName} plays nothing found in the loaded banks");
            return new SoundTriggerResult(tag, sound, silent, walk.Switches);
        }

        static SoundTriggerResult Silent(string tag, string reason) => new(tag, null, reason, []);

        /// <summary>One child stands for itself, none leaves nothing, and several become a node of the given kind.</summary>
        static SoundNode? Combine(List<SoundNode> children, bool oneOf) => children.Count switch
        {
            0 => null,
            1 => children[0],
            _ => oneOf ? new SoundOneOf(children) : new SoundAll(children),
        };

        sealed class Walk(Func<uint, HircItem?> hirc, Func<uint, bool> wemExists, IReadOnlyDictionary<uint, uint> switchChoices)
        {
            readonly HashSet<uint> _path = [];

            public List<SwitchGroupSeen> Switches { get; } = [];
            public string? Problem { get; private set; }

            /// <summary>The sound under an id, or null when none of it can play. A cycle in the bank data would otherwise never end, so an id already on the path is skipped.</summary>
            public SoundNode? Visit(uint id)
            {
                if (hirc(id) is not { } item || !_path.Add(id))
                    return null;

                try
                {
                    switch (item)
                    {
                        case ICAkSound sound:
                            var sourceId = sound.GetSourceId();
                            if (wemExists(sourceId))
                                return new SoundLeaf(sourceId);
                            Problem ??= $"WEM not found: {sourceId}.wem";
                            return null;
                        case ICAkSwitchCntr container:
                            return VisitSwitch(container);
                        case ICAkRanSeqCntr container:
                            return Combine(VisitAll(container.GetChildren()), oneOf: true);
                        case ICAkLayerCntr container:
                            return Combine(VisitAll(container.GetChildren()), oneOf: false);
                        case ICAkActorMixer container:
                            return Combine(VisitAll(container.GetChildren()), oneOf: false);
                        default:
                            Problem ??= $"not supported: {item.HircType}";
                            return null;
                    }
                }
                finally
                {
                    _path.Remove(id);
                }
            }

            List<SoundNode> VisitAll(IEnumerable<uint> ids) => ids.Select(Visit).OfType<SoundNode>().ToList();

            SoundNode? VisitSwitch(ICAkSwitchCntr container)
            {
                var cases = container.SwitchList;
                if (Switches.All(x => x.GroupId != container.GroupId))
                    Switches.Add(new SwitchGroupSeen(container.GroupId, container.DefaultSwitch, cases.Select(x => x.SwitchId).ToList()));

                // The chosen case, else the default, else the first.
                var chosen = switchChoices.TryGetValue(container.GroupId, out var choice) ? cases.FirstOrDefault(x => x.SwitchId == choice) : null;
                chosen ??= cases.FirstOrDefault(x => x.SwitchId == container.DefaultSwitch) ?? cases.FirstOrDefault();
                return chosen == null ? null : Combine(VisitAll(chosen.NodeIdList), oneOf: false);
            }
        }
    }
}
