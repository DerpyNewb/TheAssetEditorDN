using GameWorld.Core.Vfx;
using Shared.Core.PackFiles;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.MetaEditor.Check
{
    /// <summary>A tag the check found fault with, why, in a sentence, and whether vanilla ever does the same.</summary>
    public record MetaProblem(ParsedMetadataAttribute Tag, int Index, bool IsWarning, string Message)
    {
        public string TagName => $"{Tag.Name}_{Tag.Version}";
        public int Number => Index + 1;
        public string Severity => IsWarning ? "Warning" : "Error";
    }

    /// <summary>What SuperView knows about the meta's scene: the length of the clip it plays under, when it plays under one clip,
    /// and the skeleton's bone names. The standalone editor knows neither.</summary>
    public record MetaCheckScene(float? ClipSeconds, IReadOnlyList<string>? BoneNames);

    /// <summary>Problems, and a sentence for each check that could not run here and why.</summary>
    public record MetaCheckResult(List<MetaProblem> Problems, List<string> Skipped);

    public static class MetaChecker
    {
        // Times closer than this are the same moment. Three of the four vanilla tags that end before they start do so by under
        // 0.00000001 s, from 0 or -0.0000000037 to -0.0000000075, which is float noise and not a tag running backwards.
        const float Tolerance = 0.001f;

        /// <param name="packs">The loaded packs, where vfx files are looked for. Null skips that check.</param>
        public static MetaCheckResult Check(ParsedMetadataFile file, IMetaDataDatabase database, MetaCheckScene? scene, IPackFileService? packs)
        {
            var problems = new List<MetaProblem>();
            var skipped = new List<string>();
            var gamePacksLoaded = packs?.GetAllPackfileContainers().Any(x => x.IsCaPackFile) == true;
            if (scene?.ClipSeconds == null)
                skipped.Add("No single clip is known here, so start times weren't checked against a clip's length. SuperView's Animation tab knows it.");
            if (scene?.BoneNames == null)
                skipped.Add("No skeleton is known here, so prop bone ids weren't checked. SuperView knows the skeleton.");
            if (!gamePacksLoaded)
                skipped.Add("No game packs are loaded, so vfx files weren't checked: every vanilla effect would look missing.");

            for (var i = 0; i < file.Attributes.Count; i++)
            {
                var tag = file.Attributes[i];
                void Add(bool isWarning, string message) => problems.Add(new MetaProblem(tag, i, isWarning, message));

                if (Times(tag) is (float start, float end))
                {
                    if (end < start - Tolerance)
                        Add(true, $"It ends at {end:0.###} s, before it starts at {start:0.###} s. Only one of vanilla WH3's 421,684 timed tags does this, a SPLICE_12.");
                    if (scene?.ClipSeconds is float length && start > length + Tolerance)
                        Add(false, $"It starts at {start:0.###} s, but the clip is only {length:0.###} s long, so the animation ends before the tag starts.");
                }

                if (UnusedVersion(tag, database) is { } versionMessage)
                    Add(false, versionMessage);

                if (scene?.BoneNames is { } bones && PropBoneId(tag) is int id && !HasBone(bones, id) && !HasBone(bones, id - 1))
                    Add(false, $"Its prop bone id is {id}, but this skeleton has neither be_prop_{id} nor be_prop_{id - 1}. Whether the game counts these ids from 0 or from 1 isn't settled, so it's flagged only because neither reading names a bone the skeleton has.");

                if (gamePacksLoaded && VfxName(tag) is { } vfx)
                {
                    if (string.IsNullOrWhiteSpace(vfx))
                        Add(true, "It names no vfx file, so it has nothing to play. Only 9 of vanilla WH3's 22,817 EFFECT and BLOOD tags do this.");
                    else if (packs!.FindFile(VfxResolver.PathOf(vfx)) == null)
                        Add(false, $"Its vfx file, {VfxResolver.PathOf(vfx)}, isn't in any loaded pack. Unless another mod adds it, the effect won't play.");
                }
            }

            return new MetaCheckResult(problems, skipped);
        }

        static (float Start, float End)? Times(ParsedMetadataAttribute tag) => tag switch
        {
            DecodedMetaEntryBase x => (x.StartTime, x.EndTime),
            DecodedMetaEntryBase_v2 x => (x.StartTime, x.EndTime),
            _ => null,
        };

        // A use count of 0: the version is in no vanilla WH3 animation. The version vanilla uses most is offered instead.
        static string? UnusedVersion(ParsedMetadataAttribute tag, IMetaDataDatabase database)
        {
            if (database.GetWarhammer3UseCount($"{tag.Name}_{tag.Version}") != 0)
                return null;
            var used = database.GetSupportedTypes()
                .Where(key => key.LastIndexOf('_') is var at and > 0 && key[..at] == tag.Name)
                .Select(key => (Key: key, Uses: database.GetWarhammer3UseCount(key)))
                .Where(x => x.Uses > 0)
                .OrderByDescending(x => x.Uses)
                .FirstOrDefault();
            var instead = used.Key == null ? $"Vanilla doesn't use {tag.Name} in any version." : $"Vanilla uses {used.Key} instead, {used.Uses:N0} times.";
            return $"Vanilla WH3 never uses version {tag.Version} of {tag.Name}. Versions it never uses are mostly Troy and Three Kingdoms formats, and modders have found that metas in them don't take effect in WH3. {instead}";
        }

        // Every DOCK_EQPT_* and WEAPON_* tag, in all four layouts, carries the prop bone it docks as PropBoneId.
        static int? PropBoneId(ParsedMetadataAttribute tag) => tag switch
        {
            DockEquipment x => x.PropBoneId,
            DockEquipment_v2 x => x.PropBoneId,
            DockEquipment_v1 x => x.PropBoneId,
            DockEquipment_v0 x => x.PropBoneId,
            _ => null,
        };

        static bool HasBone(IReadOnlyList<string> bones, int propIndex) =>
            propIndex >= 0 && bones.Contains($"be_prop_{propIndex}", StringComparer.OrdinalIgnoreCase);

        static string? VfxName(ParsedMetadataAttribute tag) => tag switch
        {
            IEffectMeta x => x.VfxName,
            Blood_v11 x => x.VfxName,
            Blood_v5 x => x.VfxName,
            _ => null,
        };
    }
}
