using CommunityToolkit.Mvvm.ComponentModel;
using Editors.Shared.Core.Common.ReferenceModel;
using Shared.GameFormats.DB;

namespace Editors.UnitSimulator.Personalities
{
    /// <summary>One attached personality: its scene part and its two rows as edited, battle_personalities and the junction.</summary>
    public partial class PersonalityPart : ObservableObject
    {
        public SceneObjectViewModel Part { get; }
        /// <summary>The battle_personalities row it was added from; the columns not shown here are written back as they were.</summary>
        public DbRow Source { get; }
        /// <summary>The junction row it was added with, if any.</summary>
        public DbRow? SourceJunction { get; }

        [ObservableProperty] string _key;
        [ObservableProperty] string _table;
        [ObservableProperty] string _variant;
        [ObservableProperty] string _landUnit;
        [ObservableProperty] string _attach;
        [ObservableProperty] string _bone;
        [ObservableProperty] int _engineArticulationIndex;
        [ObservableProperty] bool _jettisonRider;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(Status))] string _loadStatus = "";
        [ObservableProperty, NotifyPropertyChangedFor(nameof(Status))] string _boneStatus = "";
        [ObservableProperty] string _copies = "";

        /// <summary>What of its rows was not found, or empty.</summary>
        public string Status => string.Join(" ", new[] { LoadStatus, BoneStatus }.Where(x => x != ""));

        public string BattleEntity => Source.Text("battle_entity");
        public string BattleEntityStats => Source.Text("battle_entity_stats");
        public bool AutonomousRiderHero => Source.Flag("autonomous_rider_hero");
        public bool AutonomousRiderCanShootInMelee => Source.Flag("autonomous_rider_can_shoot_in_melee");
        public bool CanOnlyBeUsedByDraught => Source.Flag("can_only_be_used_by_draught");

        // With no junction row: attached as an autonomous rider on the mount's first bone, jettisoned, as 1,466 and 1,442 of CA's
        // 1,507 rows are.
        public PersonalityPart(SceneObjectViewModel part, DbRow source, DbRow? junction, string firstBone)
        {
            Part = part;
            Source = source;
            SourceJunction = junction;
            _key = source.Text("key");
            _table = source.Text("man_animations_table");
            _variant = source.Text("variant");
            _landUnit = junction?.Text("land_unit") ?? "";
            _attach = junction?.Text("attach") is { Length: > 0 } attach ? attach : "autonomous_rider";
            _bone = junction != null ? junction.Text("riders_attachment_point") : firstBone;
            _engineArticulationIndex = junction?.Number("engine_articulation_index", -1) ?? -1;
            _jettisonRider = junction?.Flag("jettison_rider", true) ?? true;
        }
    }
}
