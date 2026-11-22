#nullable enable
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;

namespace Editors.Audio.SoundTriggers
{
    /// <summary>What one firing of a sound trigger can play, as a tree of Wwise containers cut down to the parts that matter.</summary>
    public abstract record SoundNode;

    /// <summary>One streamed WEM, by source id.</summary>
    public sealed record SoundLeaf(uint SourceId) : SoundNode;

    /// <summary>Everything under it plays together: a blend container, an actor mixer, or several Play actions on one event.</summary>
    public sealed record SoundAll(IReadOnlyList<SoundNode> Children) : SoundNode;

    /// <summary>One child plays per firing: a random or sequence container, by its Wwise playback rules. Id is the container's, which a
    /// SoundPicker keeps its place by. Weights are the playlist's, one per child (Wwise's 50000 is a weight of 50), or null for equal
    /// chances. AvoidRepeat is how many of the last children a random pick skips, Shuffle plays each child once before any again, and
    /// Sequence plays them in playlist order, starting over at the end; a sequence ignores the other three.</summary>
    public sealed record SoundOneOf(IReadOnlyList<SoundNode> Children, uint Id = 0, IReadOnlyList<int>? Weights = null, int AvoidRepeat = 0,
        bool Shuffle = false, bool Sequence = false) : SoundNode;

    public static class SoundNodes
    {
        /// <summary>Every source id under the node, for decoding ahead of time. An id under two parents comes twice, so a caller that
        /// wants each once takes Distinct, as the session does.</summary>
        public static IEnumerable<uint> Leaves(SoundNode node) => node switch
        {
            SoundLeaf leaf => [leaf.SourceId],
            SoundAll all => all.Children.SelectMany(Leaves),
            SoundOneOf oneOf => oneOf.Children.SelectMany(Leaves),
            _ => [],
        };

        /// <summary>The source ids one firing plays, with no memory of earlier firings: see SoundPicker.</summary>
        public static IReadOnlyList<uint> Pick(SoundNode node, Random random) => new SoundPicker(random).Pick(node);
    }

    /// <summary>Picks what each firing plays: every child of a SoundAll, one child of a SoundOneOf by its rules. It keeps each container's
    /// place across firings, as Wwise keeps it per game object: a sequence's next child, a random container's last picks, and the children a
    /// shuffle has played this round.</summary>
    public sealed class SoundPicker(Random random)
    {
        // By container id: the children picked, oldest first (random), or the next child's index (sequence).
        readonly Dictionary<uint, List<int>> _played = [];
        readonly Dictionary<uint, int> _next = [];

        public IReadOnlyList<uint> Pick(SoundNode node) => node switch
        {
            SoundLeaf leaf => [leaf.SourceId],
            SoundAll all => all.Children.SelectMany(Pick).ToList(),
            SoundOneOf { Children.Count: > 0 } oneOf => Pick(oneOf.Children[Choose(oneOf)]),
            _ => [],
        };

        int Choose(SoundOneOf container)
        {
            var count = container.Children.Count;
            if (container.Sequence)
            {
                var index = _next.GetValueOrDefault(container.Id) % count;
                _next[container.Id] = index + 1;
                return index;
            }

            if (!_played.TryGetValue(container.Id, out var played))
                _played[container.Id] = played = [];
            // Wwise skips the last AvoidRepeat picks, at most all but one child. A shuffle also skips what this round has played, and a
            // round ends once every child has played. Some child is always left: fewer of the avoided than of the unplayed are unplayed.
            var avoided = played.TakeLast(Math.Min(container.AvoidRepeat, count - 1)).ToHashSet();
            if (container.Shuffle)
                avoided.UnionWith(played.TakeLast(played.Count % count));
            var allowed = Enumerable.Range(0, count).Where(x => !avoided.Contains(x)).ToList();
            var chosen = Weighted(allowed, container.Weights);
            // ponytail: the history grows by one int a firing, a few thousand in an hour of looping; trim it if a session ever runs for days.
            played.Add(chosen);
            return chosen;
        }

        // A child by its weight; equal chances without weights, or when the allowed ones weigh nothing.
        int Weighted(List<int> allowed, IReadOnlyList<int>? weights)
        {
            var total = weights == null ? 0 : allowed.Sum(x => (double)Math.Max(0, weights[x]));
            if (total <= 0)
                return allowed[random.Next(allowed.Count)];
            var at = random.NextDouble() * total;
            foreach (var index in allowed)
            {
                at -= Math.Max(0, weights![index]);
                if (at < 0)
                    return index;
            }
            return allowed[^1];
        }
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
                    case { } other when MayStartASound(other):
                        unsupported ??= Describe(other, actionEvent);
                        break;
                }
            }

            // When nothing plays, the reason is the first of: an action SuperView does not play that may start a sound, which may be the
            // very thing that sounds in game; what went wrong under a Play action (a type SuperView does not play, or a missing WEM); an
            // action no loaded bank has; Play actions that lead to nothing loaded. An event with none of these has no Play action.
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

        /// <summary>Whether an action other than Play could be what sounds: one that starts a sound or posts an event (PlayAndContinue,
        /// PlayEvent, the Triggers), or one of a type the enum lacks, which could be anything. Stop, Pause, Resume, Set, Reset, Mute and
        /// the rest cannot start a sound. An id that names no action at all is odd enough to count.</summary>
        static bool MayStartASound(HircItem item) => item is not ICAkAction action || action.GetActionType() is var type && (!Enum.IsDefined(type)
            || type is AkActionType.PlayAndContinue or AkActionType.PlayEvent or AkActionType.Trigger or AkActionType.Trigger_O or AkActionType.Trigger_E or AkActionType.Trigger_E_O);

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
                            return VisitRanSeq(container);
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

            // The playlist's rules, for the children that can play. CAkRanSeqCntr's mode is 0 random, 1 sequence, and its random mode 0
            // standard, 1 shuffle. The weights are always taken: wwiser's bIsUsingWeight, bit 0 of the bit vector, is clear on all 90,624
            // of CA's containers, 564 of which weigh their children unequally. Left out: continuous play, where one Play runs the whole
            // list (3,880 of CA's), plays one child a firing here, and restarting backward (29) starts over.
            SoundNode? VisitRanSeq(ICAkRanSeqCntr container)
            {
                var kept = container.GetChildren().Select((id, index) => (Node: Visit(id), Index: index)).Where(x => x.Node != null).ToList();
                if (kept.Count < 2 || container is not CAkRanSeqCntr_V136 rules)
                    return Combine(kept.Select(x => x.Node!).ToList(), oneOf: true);
                var sequence = rules.Mode == 1;
                return new SoundOneOf(kept.Select(x => x.Node!).ToList(), rules.Id, kept.Select(x => rules.CAkPlayList.Playlist[x.Index].Weight).ToList(),
                    rules.AvoidRepeatCount, Shuffle: rules.RandomMode == 1, Sequence: sequence);
            }

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
