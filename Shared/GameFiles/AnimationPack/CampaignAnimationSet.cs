namespace Shared.GameFormats.AnimationPack
{
    /// <summary>A campaign animation set (animations/campaign/database/bin) read as an animation set, so a skeleton that no
    /// battle animation table names still has one to pick; Glottkin's only set is cam_lord_glt01_dlc29_ghurk.bin. A slot is
    /// its status and kind ("status_normal/idle", "status_battle/battle_fatality"). The global status's persistent meta, hand
    /// poses and docks take the battle slot names, which is what the meta's own tags look up.</summary>
    public class CampaignAnimationSet(string fullPath, CampaignAnimationBin bin) : IAnimationBinGenericFormat
    {
        public static readonly (string BattleSlot, string CampaignDock)[] DockSlots =
        [
            ("DOCK_EQUIPMENT_RIGHT_HAND", "DOCK_EQPT_RHAND"),
            ("DOCK_EQUIPMENT_LEFT_HAND", "DOCK_EQPT_LHAND"),
            ("DOCK_EQUIPMENT_LEFT_WAIST", "DOCK_EQPT_LWAIST"),
            ("DOCK_EQUIPMENT_RIGHT_WAIST", "DOCK_EQPT_RWAIST"),
            ("DOCK_EQUIPMENT_BACK", "DOCK_EQPT_BACK"),
        ];

        public AnimationPackFileDatabase? PackFileReference => null;
        public string Name => bin.Reference;
        public string FullPath => fullPath;
        public string SkeletonName => bin.SkeletonName;
        public string MountBin => "";
        public CampaignAnimationBin Bin => bin;

        public List<AnimationBinEntryGenericFormat> Entries => Rows().Select(x => x.Slot).ToList();

        /// <summary>Writes a slot's three files into the row it was read from: the row with its name and index that names the
        /// files the slot does. Throws when the set has no such row, as when it was edited since the slot was read.</summary>
        public void SetFiles(AnimationBinEntryGenericFormat slot, string animation, string meta, string sound)
        {
            var row = Rows().FirstOrDefault(x => x.Slot.SlotName == slot.SlotName && x.Slot.Index == slot.Index
                && x.Slot.AnimationFile == slot.AnimationFile && x.Slot.MetaFile == slot.MetaFile && x.Slot.SoundFile == slot.SoundFile);
            if (row.Set == null)
                throw new ArgumentException($"{fullPath} has no {slot.DisplayName} naming {slot.AnimationFile}");
            row.Set(animation, meta, sound);
        }

        // Every row as a slot, with what writes the slot's three files back into that row. Entries and SetFiles both walk this,
        // which is what ties a slot to its row.
        List<(AnimationBinEntryGenericFormat Slot, Action<string, string, string> Set)> Rows()
        {
            var rows = new List<(AnimationBinEntryGenericFormat, Action<string, string, string>)>();
            void Add(string slot, string animation, string meta, string sound, float blend, Action<string, string, string> set) =>
                rows.Add((new AnimationBinEntryGenericFormat { SlotName = slot, AnimationFile = animation, MetaFile = meta, SoundFile = sound, BlendInTime = blend }, set));

            foreach (var status in bin.Status)
            {
                var s = status.Name;
                foreach (var x in status.PersitantMetaData ?? [])
                    Add("PERSISTENT_METADATA_ALIVE", x.Animation, x.AnimationMeta, x.SoundMeta, x.BlendTime, (a, m, o) => (x.Animation, x.AnimationMeta, x.SoundMeta) = (a, m, o));
                foreach (var x in status.Poses ?? [])
                    Add(Enum.IsDefined(typeof(KnownPoseId), x.PoseId) ? ((KnownPoseId)x.PoseId).ToString() : $"POSE_{x.PoseId}", x.Animation, x.AnimationMeta, x.SoundMeta, x.BlendTime, (a, m, o) => (x.Animation, x.AnimationMeta, x.SoundMeta) = (a, m, o));
                foreach (var x in status.Docks ?? [])
                    Add(BattleDock(x.Dock), x.Animation, x.AnimationMeta, x.SoundMeta, x.BlendTime, (a, m, o) => (x.Animation, x.AnimationMeta, x.SoundMeta) = (a, m, o));
                foreach (var x in status.Idle ?? [])
                    Add($"{s}/idle", x.Animation, x.MetaFile, x.SoundMeta, x.BlendTime, (a, m, o) => (x.Animation, x.MetaFile, x.SoundMeta) = (a, m, o));
                foreach (var x in status.Porthole ?? [])
                    Add($"{s}/porthole", x.Animation, x.AnimationMeta, x.SoundMeta, x.BlendTime, (a, m, o) => (x.Animation, x.AnimationMeta, x.SoundMeta) = (a, m, o));
                foreach (var x in status.Selection ?? [])
                    Add($"{s}/selection", x.Animation, x.MetaFile, x.SoundMeta, x.BlendTime, (a, m, o) => (x.Animation, x.MetaFile, x.SoundMeta) = (a, m, o));
                foreach (var x in status.Transitions ?? [])
                    Add($"{s}/transition {x.TransitionTo}", x.Animation, x.AnimationMeta, x.SoundMeta, x.BlendTime, (a, m, o) => (x.Animation, x.AnimationMeta, x.SoundMeta) = (a, m, o));
                foreach (var x in status.Action ?? [])
                    Add($"{s}/{x.ActionType}", x.Animation, x.Meta, x.SoundMeta, x.BlendTime, (a, m, o) => (x.Animation, x.Meta, x.SoundMeta) = (a, m, o));
                foreach (var x in status.Unknown ?? [])
                    Add($"{s}/unknown", x.Animation, x.AnimationMeta, x.SoundMeta, x.BlendTime, (a, m, o) => (x.Animation, x.AnimationMeta, x.SoundMeta) = (a, m, o));
                foreach (var x in status.Locomotion ?? [])
                    Add($"{s}/locomotion", x.Animation, x.AnimationMeta, x.SoundMeta, x.BlendTime, (a, m, o) => (x.Animation, x.AnimationMeta, x.SoundMeta) = (a, m, o));
            }

            // Three idles in one status read [0], [1] and [2], as a battle slot with several animations does.
            foreach (var slot in rows.Select(x => x.Item1).GroupBy(x => x.SlotName).Where(x => x.Count() > 1))
                foreach (var (entry, index) in slot.Select((x, i) => (x, i)))
                    entry.Index = index;
            return rows;
        }

        // DOCK_EQPT_RHAND_2 is DOCK_EQUIPMENT_RIGHT_HAND_2, the slot the meta's second right hand dock reads.
        static string BattleDock(string dock)
        {
            foreach (var (battle, campaign) in DockSlots)
                if (dock.StartsWith(campaign, StringComparison.OrdinalIgnoreCase))
                    return battle + dock[campaign.Length..];
            return dock;
        }
    }
}
