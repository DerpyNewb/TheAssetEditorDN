#nullable enable
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
    /// <param name="hircs">The Wwise objects with an id, in the loaded banks' order; none when no loaded bank has it. A lookup takes
    /// the first, but one id can name two objects: CA's melee hit events share theirs with a dialogue event.</param>
    /// <param name="wemExists">Whether the WEM for a source id can be found. WH3's WEMs are all streamed, so this is the only source check.</param>
    public sealed class SoundTriggerResolver(Func<string, AudioTagEvents?> tagRow, Func<uint, IEnumerable<HircItem>> hircs, Func<uint, bool> wemExists)
    {
        /// <summary>How a reason starts when the sound goes through something SuperView does not play, which may well sound in game.</summary>
        public const string NotSupported = "not supported: ";

        HircItem? Hirc(uint id) => hircs(id).FirstOrDefault();

        /// <param name="switchChoices">The switch case to play, by group id. A group not in it plays its default.</param>
        public SoundTriggerResult Resolve(string tag, bool campaign, IReadOnlyDictionary<uint, uint> switchChoices)
        {
            var row = tagRow(tag);
            if (row == null)
                return Silent(tag, "no audio_metadata_tags_tables row");

            var eventName = campaign ? row.CampaignStart : row.BattleStart;
            if (string.IsNullOrWhiteSpace(eventName))
                return Silent(tag, campaign ? "the row names no campaign event" : "the row names no battle event");

            var item = Hirc(WwiseHash.Compute(eventName));
            if (item == null)
                return Silent(tag, $"event {eventName} is in no loaded bank");
            if (item is not ICAkEvent actionEvent)
                return Silent(tag, NotSupported + item.HircType);

            var walk = new Walk(Hirc, wemExists, switchChoices);
            var plays = new List<SoundNode>();
            var playActions = 0;
            string? unsupported = null;
            uint? missing = null;
            foreach (var actionId in actionEvent.GetActionIds())
            {
                switch (Hirc(actionId))
                {
                    case null:
                        missing ??= actionId;
                        break;
                    case ICAkAction action when action.GetActionType() == AkActionType.Play:
                        playActions++;
                        if (walk.Visit(action.GetChildId()) is { } node)
                            plays.Add(node);
                        break;
                    case { } other:
                        unsupported ??= Describe(other, actionEvent);
                        break;
                }
            }

            // When nothing plays, the reason is the first of: an action of a type SuperView does not play, which may be the very thing
            // that sounds in game; what went wrong under a Play action (a type SuperView does not play, or a missing WEM); an action no
            // loaded bank has; Play actions that lead to nothing loaded. Only an event with no actions at all has no Play action.
            var sound = Combine(plays, oneOf: false);
            var silent = sound != null ? null
                : unsupported != null ? NotSupported + unsupported
                : walk.Problem
                    ?? (missing != null ? $"action {missing} of event {eventName} is in no loaded bank"
                    : playActions != 0 ? $"event {eventName} plays nothing found in the loaded banks"
                    : $"event {eventName} has no Play action");
            return new SoundTriggerResult(tag, sound, silent, walk.Switches);
        }

        static SoundTriggerResult Silent(string tag, string reason) => new(tag, null, reason, []);

        /// <summary>An action's type, in hex when Wwise's list as the editor knows it has no name for it, then the type of what it
        /// targets, other than the event it belongs to. An action that targets another event is followed one level down, to say what
        /// that event's first action reaches: CA's melee hit events reach a dialogue event with the inner event's own id that way.</summary>
        string Describe(HircItem item, ICAkEvent owner, bool follow = true)
        {
            if (item is not ICAkAction action)
                return item.HircType.ToString();

            var type = action.GetActionType();
            var text = (Enum.IsDefined(type) ? type.ToString() : $"0x{(ushort)type:X4}") + " action";
            if (hircs(action.GetChildId()).FirstOrDefault(x => !ReferenceEquals(x, owner)) is not { } target)
                return text;
            text += " -> " + target.HircType;
            if (follow && target is ICAkEvent inner && inner.GetActionIds().Select(Hirc).OfType<HircItem>().FirstOrDefault() is { } first)
                text += " -> " + Describe(first, inner, follow: false);
            return text;
        }

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
                            Problem ??= NotSupported + item.HircType;
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
