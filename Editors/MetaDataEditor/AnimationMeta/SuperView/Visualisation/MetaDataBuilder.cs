using System.Reflection;
using Editors.AnimationMeta.SuperView.Editing;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using Editors.AnimationMeta.SuperView.Visualisation.Rules;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.DB;

namespace Editors.AnimationMeta.SuperView.Visualisation
{
    public interface IMetaDataBuilder
    {
        // Off, an EFFECT tag draws its locator and no particles.
        bool ShowVfx { get; set; }
        // The battle entity's vfx_filter_id: an EFFECT tag whose id is neither 0 nor this is not drawn. Unset, the lowest id the metas carry.
        int? VfxFilterId { get; set; }
        // As a unit seen from far away: EFFECT tags marked Distance Culled are not drawn.
        bool HideDistanceCulled { get; set; }
        // A spell's wind-up vfx (special_ability_displays wind_up_aura_vfx / wind_up_weapon_vfx), which the game plays at the
        // placeholder EFFECT tags of type ability_aura (1) and ability_weapon (2) in cast animations, named as under vfx/.
        // Unset, those tags draw the vfx they name.
        string? AuraVfx { get; set; }
        string? WeaponVfx { get; set; }
        /// <summary>Lets go of what was loaded from these pack paths, so the next Create reads them again.</summary>
        void Forget(IEnumerable<string> paths);
        /// <summary>What the last Create could not draw, tag by tag.</summary>
        IReadOnlyList<MetaDataBuildProblem> Problems { get; }
        List<IMetaDataInstance> Create(ParsedMetadataFile? persistent, ParsedMetadataFile? metaData, ParsedMetadataAttribute? selectedMetaDataAttribute, SceneNode root, ISkeletonProvider skeleton, AnimationPlayer rootPlayer, IAnimationBinGenericFormat fragment);
        /// <summary>A bone's transform at a time on the main clock, as the markers riding it are drawn; -1 is the model's origin.</summary>
        Func<float, Matrix> BoneAt(int boneIndex, ISkeletonProvider skeleton, AnimationPlayer rootPlayer);
    }

    // IDisposable so the editor's scope frees the effects' textures when SuperView closes.
    public class MetaDataBuilder : IMetaDataBuilder, IDisposable
    {
        private readonly ILogger _logger = Logging.Create<MetaDataBuilder>();
        private readonly ComplexMeshLoader _complexMeshLoader;
        private readonly RenderEngineComponent _resourceLibrary;
        private readonly ResourceLibrary _shaders;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private readonly IPackFileService _packFileService;
        private readonly VfxTextureCache _vfxTextures;
        private readonly VfxMeshCache _vfxMeshes;

        // Every edit in the meta panel rebuilds every effect. What a tag's vfx file resolves to is kept until Forget names a
        // changed file; and as an effect re-simulates up to now it asks its bone for every step, each a sample of the whole
        // skeleton, which effects asking the same time share. 55 effects on a Bale Taurus took 1.3 s to rebuild.
        readonly Dictionary<(string Name, int Seed), VfxResolveResult> _resolved = [];
        readonly Dictionary<float, Matrix[]> _bones = [];
        // Each model's VFX_ALPHA keys from both metas, read again on every build, so a kept effect fades by the current keys.
        readonly Dictionary<SceneNode, List<VfxAlpha_v10>> _vfxAlpha = new(ReferenceEqualityComparer.Instance);
        (AnimationClip? Clip, GameSkeleton? Skeleton) _bonesOf;

        // A slider in the meta panel rebuilds on every step, and building every effect and prop again still took 220 ms of
        // each on that Taurus. One whose tag values, colour, vfx toggle, clip and skeleton are as they were, and which is still
        // in the scene, is kept as built; the caller cleans up only what Create no longer returns.
        record KeptAs(string Values, bool Picked, bool ShowVfx, string? AuraVfx, string? WeaponVfx, AnimationClip? Clip, GameSkeleton? Skeleton);
        record Kept(KeptAs As, ISceneNode Node, IMetaDataInstance Instance, MetaDataBuildProblem[] Problems);
        Dictionary<ParsedMetadataAttribute, Kept> _kept = new(ReferenceEqualityComparer.Instance);
        Dictionary<ParsedMetadataAttribute, Kept> _previous = new(ReferenceEqualityComparer.Instance);
        static readonly Dictionary<Type, PropertyInfo[]> s_tagValues = [];

        private static Color s_color = Color.Black;
        private static Color s_selectedColor = Color.Red;
        public static readonly Color FiringColour = Color.Yellow;

        public bool ShowVfx { get; set; } = true;
        public int? VfxFilterId { get; set; }
        public bool HideDistanceCulled { get; set; }
        public string? AuraVfx { get; set; }
        public string? WeaponVfx { get; set; }

        readonly List<MetaDataBuildProblem> _problems = [];
        public IReadOnlyList<MetaDataBuildProblem> Problems => _problems;

        // A broken effect would log again on every rebuild (every click in the tag list); once is enough.
        private static readonly HashSet<string> s_loggedVfxMessages = [];

        public MetaDataBuilder(ComplexMeshLoader complexMeshLoader,
            RenderEngineComponent resourceLibrary,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper,
            IPackFileService packFileService,
            ResourceLibrary shaders)
        {
            _shaders = shaders;
            _complexMeshLoader = complexMeshLoader;
            _resourceLibrary = resourceLibrary;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _packFileService = packFileService;
            SkeletonRow = ReadSkeletonRow;
            _vfxTextures = new VfxTextureCache(ParticleRenderItem.PackTextureLoader(packFileService, LogOnce));
            _vfxMeshes = new VfxMeshCache(LoadRigid, VfxMeshCache.PackAnimationLoader(packFileService, LogOnce), LogOnce);
        }

        public void Dispose()
        {
            _vfxTextures.Dispose();
            _vfxMeshes.Dispose();
        }

        // A rigid particle's model, through the scene's mesh loader; null (logged once) when it is missing or broken.
        SceneNode? LoadRigid(string path)
        {
            if (_packFileService.FindFile(path) is not { } file)
            {
                LogOnce($"rigid model {path} is not in the loaded packs");
                return null;
            }
            foreach (var shader in VfxMeshCache.CustomShaders(_packFileService, path))
                LogOnce($"rigid model {path} names {shader}, one of CA's own shaders the preview cannot run: drawn as a plain lit mesh");
            try
            {
                return _complexMeshLoader.Load(file, new AnimationPlayer(), onlyLoadRootNode: true, onlyLoadFirstMesh: false);
            }
            catch (Exception e)
            {
                LogOnce($"rigid model {path} did not load: {e.Message}");
                return null;
            }
        }

        public void Forget(IEnumerable<string> paths)
        {
            // An effect resolves through its file's references too, so any changed vfx file may change any of them.
            _resolved.Clear();
            _bones.Clear();
            _kept.Clear();
            foreach (var path in paths)
            {
                _vfxTextures.Forget(path);
                _vfxMeshes.Forget(path);
            }
        }

        public List<IMetaDataInstance> Create(ParsedMetadataFile? persistent, 
            ParsedMetadataFile? metaData, ParsedMetadataAttribute? selectedMetaDataAttribute,
            SceneNode root, ISkeletonProvider skeleton, AnimationPlayer rootPlayer, IAnimationBinGenericFormat fragment)
        {
            var output = new List<IMetaDataInstance>();
            (_previous, _kept) = (_kept, _previous);
            _kept.Clear();
            _problems.Clear();

            // Apply persistent meta data, if no disable is given.
            var usePersistent = metaData == null || metaData.GetItemsOfType<DisablePersistant_v10>().Count == 0;
            // DISABLE_PERSISTENT_VFX drops the persistent meta's effects and keeps the rest of it.
            var usePersistentVfx = metaData == null || metaData.GetItemsOfType<DisablePersistantVfx_v10>().Count == 0;
            var vfxFilter = VfxFilterId ?? VfxFilterIds(persistent, metaData).FirstOrDefault();
            _vfxAlpha[root] = Both<VfxAlpha_v10>(usePersistent ? persistent : null, metaData).OrderBy(x => x.StartTime).ToList();
            if (usePersistent)
            {
                var metaDataPersistent = ApplyMetaData(persistent, selectedMetaDataAttribute, root, skeleton, rootPlayer, fragment, vfxFilter, usePersistentVfx, persistent: true);
                output.AddRange(metaDataPersistent);
            }

            var metaDataInstances = ApplyMetaData(metaData, selectedMetaDataAttribute,root, skeleton, rootPlayer, fragment, vfxFilter);
            output.AddRange(metaDataInstances);

            // Hand poses are keys across both files, so they are built from both at once.
            // Splices run before every other rule: a splice sets its bones afresh, which would undo a TRANSFORM that ran first,
            // and a hand or face pose still has the last word on its bones.
            var splices = Both<Splice_v11>(usePersistent ? persistent : null, metaData).Where(x => x is not (SpliceOverride_v11 or SpliceOverride_v12))
                .Concat(OlderSplices(usePersistent ? persistent : null, metaData))
                .Select(x => CreateSplice(x, skeleton)).OfType<IAnimationChangeRule>();
            rootPlayer.AnimationRules.InsertRange(0, splices);
            CreateHandPoses(usePersistent ? persistent : null, metaData, fragment, skeleton, rootPlayer);
            CreateFacePoses(usePersistent ? persistent : null, metaData, fragment, skeleton, rootPlayer);

            // The model's own state is one answer from both files' tags at once. Only SuperView's asset has a model to change.
            if (skeleton is SceneObject scene)
            {
                var disable = Both<DisableModel>(usePersistent ? persistent : null, metaData);
                var alpha = Both<Alpha_v10>(usePersistent ? persistent : null, metaData).OrderBy(x => x.StartTime).ToList();
                if (disable.Count + alpha.Count != 0)
                    output.Add(new DisableModelInstance(scene, t => disable.Any(x => IsOn(x, t)) || AlphaIsZero(alpha, t)));
                var rescale = Both<ReScale>(usePersistent ? persistent : null, metaData);
                if (rescale.Count != 0)
                    output.Add(new RescaleInstance(scene, rescale));
            }
            return output;
        }

        // SPLICE versions 10, 6 and 5 (12, 3 and 3 vanilla uses) carry version 11's fields less Blend Out Time, and version 5
        // less Blend In Time too, so they are drawn as a version 11 with those times at 0. Version 5's bone depth is a float
        // in the file and is read as a whole number, as version 6 stores it.
        static IEnumerable<Splice_v11> OlderSplices(ParsedMetadataFile? persistent, ParsedMetadataFile? metaData) =>
        [
            .. Both<Splice_v10>(persistent, metaData).Select(x => AsSplice(x.StartTime, x.EndTime, x.Filter, x.Animation, x.ReferenceAnimation, x.GenericBoneIndex, x.GenericBoneDepth,
                x.GenericWeight, x.NeckWeight, x.TorsoWeight, x.LeftArmWeight, x.RightArmWeight, x.LegsWeight, x.BlendInTime, x.Additive, x.WeaponPose, x.Stretch, x.ExcludeStripped)),
            .. Both<Splice_v6>(persistent, metaData).Select(x => AsSplice(x.StartTime, x.EndTime, x.Filter, x.Animation, x.ReferenceAnimation, x.GenericBoneIndex, x.GenericBoneDepth,
                x.GenericWeight, x.NeckWeight, x.TorsoWeight, x.LeftArmWeight, x.RightArmWeight, x.LegsWeight, x.BlendInTime, x.Additive, x.WeaponPose, x.Stretch, x.ExcludeStripped)),
            .. Both<Splice_v5>(persistent, metaData).Select(x => AsSplice(x.StartTime, x.EndTime, x.Filter, x.Animation, x.ReferenceAnimation, x.GenericBoneIndex, (int)x.UnknownFloatOrGenericBoneDepth,
                x.GenericWeight, x.NeckWeight, x.TorsoWeight, x.LeftArmWeight, x.RightArmWeight, x.LegsWeight, 0, x.Additive, x.WeaponPose, x.Stretch, x.ExcludeStripped)),
        ];

        static Splice_v11 AsSplice(float start, float end, string filter, string animation, string reference, int bone, int depth, float generic, float neck, float torso,
            float leftArm, float rightArm, float legs, float blendIn, string additive, string weaponPose, string stretch, string excludeStripped) => new()
        {
            Name = "SPLICE", Version = 11, StartTime = start, EndTime = end, Filter = filter, Animation = animation, ReferenceAnimation = reference,
            GenericBoneIndex = bone, GenericBoneDepth = depth, GenericWeight = generic, NeckWeight = neck, TorsoWeight = torso, LeftArmWeight = leftArm,
            RightArmWeight = rightArm, LegsWeight = legs, BlendInTime = blendIn, Additive = additive, WeaponPose = weaponPose, Stretch = stretch, ExcludeStripped = excludeStripped,
        };

        static List<T> Both<T>(ParsedMetadataFile? persistent, ParsedMetadataFile? metaData) =>
            [.. persistent?.GetItemsOfType<T>() ?? [], .. metaData?.GetItemsOfType<T>() ?? []];

        // Whether a tag that changes the model is on at a time. Start < End is the window between them; Start = End (or End
        // before Start) is on from Start to the end of the clip, so 0/0 is the whole clip. That is how vanilla's DISABLE_MODEL
        // tags read: 0/0 on the engines' "invisible" stands, an instant where a matched-combat victim is swallowed.
        public static bool IsOn(DecodedMetaEntryBase tag, float time) => IsOn(tag.StartTime, tag.EndTime, time);
        public static bool IsOn(float startTime, float endTime, float time) =>
            endTime > startTime ? time >= startTime && time <= endTime : time >= startTime;

        // Whether an event tag is firing at a time: FIRE_POS spawns its projectile at Start Time, IMPACT_POS is the hit,
        // SOUND_TRIGGER plays, CAMERA_SHAKE_POS shakes. 99.6% of the vanilla ones are instants (End = Start; FIRE_POS 2,778
        // of 2,791, IMPACT_POS 13,921 of 13,923, every TARGET_POS), so the marker turns FiringColour from Start Time for
        // FlashSeconds, long enough to see at full speed. A tag with a longer window fires for the whole window.
        public const float FlashSeconds = 0.25f;
        public static bool IsFiring(float startTime, float endTime, float time) =>
            time >= startTime && time <= Math.Max(endTime, startTime + FlashSeconds);

        static string WithTime(string name, float startTime, float endTime) => endTime > startTime
            ? FormattableString.Invariant($"{name} {startTime:0.00}-{endTime:0.00}s")
            : FormattableString.Invariant($"{name} @{startTime:0.00}s");

        // ALPHA tags are keys: all 1,400 vanilla ones are instants, at 0 or 1, and a death keys 1 a moment before its 0,
        // which only means something if the game fades from one key to the next. Read as fades or as snaps, the alpha is 0
        // from a 0 key until the next key, unless that key is above 0, where a fade would already be under way. The preview
        // draws meshes opaque, so it hides the model only where both readings agree it is invisible. Keys sorted by time.
        // A VFX_ALPHA key's value from its Start Time, fading in a straight line to the next key's: 1 before the first key, the last
        // key's value after it. A reading of the tag as ALPHA's keys are read, not checked in game; all 10 vanilla uses are 1.
        public static float VfxAlphaAt(IReadOnlyList<VfxAlpha_v10> keys, float time)
        {
            var i = -1;
            for (var k = 0; k < keys.Count && keys[k].StartTime <= time; k++)
                i = k;
            if (i < 0)
                return 1;
            if (i + 1 == keys.Count || keys[i + 1].StartTime <= keys[i].StartTime)
                return keys[i].DesiredAlpha;
            return MathHelper.Lerp(keys[i].DesiredAlpha, keys[i + 1].DesiredAlpha, (time - keys[i].StartTime) / (keys[i + 1].StartTime - keys[i].StartTime));
        }

        static bool AlphaIsZero(List<Alpha_v10> keys, float time)
        {
            var i = keys.FindLastIndex(x => x.StartTime <= time);
            return i >= 0 && keys[i].DesiredAlpha <= 0 && (i + 1 == keys.Count || keys[i + 1].DesiredAlpha <= 0);
        }

        // The vfx filter ids the two metas' EFFECT tags carry, lowest first, 0 (every entity) left out.
        public static List<int> VfxFilterIds(ParsedMetadataFile? persistent, ParsedMetadataFile? metaData) =>
            [.. new[] { persistent, metaData }.SelectMany(x => x?.GetItemsOfType<IEffectMeta>() ?? []).Select(FilterOf).Where(x => x != 0).Distinct().Order()];

        // EFFECT v11 and v12 carry the id; the older versions have none and play on every entity.
        static int FilterOf(IEffectMeta effect) => effect is DecodedMetaEntryBase tag ? tag.Id : 0;

        // HandPoseId indexes these slots, in the order of the WH3 slot table.
        static readonly string[] s_handPoseSlots =
        [
            "HAND_POSE_RELAX", "HAND_POSE_FLAT", "HAND_POSE_CLENCH", "HAND_POSE_GRIP", "HAND_POSE_HALF_OPEN", "HAND_POSE_THUMB_GRIP",
            "HAND_POSE_CUSTOM_1", "HAND_POSE_CUSTOM_2", "HAND_POSE_CUSTOM_3", "HAND_POSE_CUSTOM_4", "HAND_POSE_CUSTOM_5",
            "HAND_POSE_CUSTOM_6", "HAND_POSE_CUSTOM_7", "HAND_POSE_CUSTOM_8", "HAND_POSE_CUSTOM_9", "HAND_POSE_CUSTOM_10",
        ];

        void CreateHandPoses(ParsedMetadataFile? persistent, ParsedMetadataFile? metaData, IAnimationBinGenericFormat? fragment, ISkeletonProvider skeleton, AnimationPlayer rootPlayer)
        {
            if (skeleton.Skeleton == null)
                return;

            // Persistent keys sit before time 0, so a key in the animation's own meta overrides them.
            var tags = (persistent?.GetItemsOfType<IHandPose>() ?? []).Select(x => (Time: -1f, Tag: x))
                .Concat((metaData?.GetItemsOfType<IHandPose>() ?? []).Select(x => (Time: x.StartTime, Tag: x)))
                .ToList();

            foreach (var (tagName, handBone) in new[] { ("LHAND_POSE", "hand_left"), ("RHAND_POSE", "hand_right") })
            {
                var keys = tags.Where(x => x.Tag.Name == tagName)
                    .Select(x => new HandPoseRule.Key(x.Time, LoadHandPose(x.Tag.HandPoseId, fragment, skeleton.Skeleton), x.Tag.Weight))
                    .ToList();
                if (keys.Count != 0)
                    rootPlayer.AnimationRules.Add(new HandPoseRule(skeleton.Skeleton, handBone, keys));
            }
        }

        AnimationFrame? LoadHandPose(int handPoseId, IAnimationBinGenericFormat? fragment, GameSkeleton skeleton)
        {
            var pose = LoadPose(s_handPoseSlots, handPoseId, fragment, skeleton);
            if (pose == null && handPoseId >= 0 && handPoseId < s_handPoseSlots.Length)
                _logger.Here().Warning($"Hand pose {s_handPoseSlots[handPoseId]} is not in the animation set, so the fingers keep the animation's pose");
            return pose;
        }

        /// <summary>The battle_skeletons row a SPLICE's body parts are read from, by skeleton name; null when no loaded pack has one.</summary>
        public Func<string, DbRow?> SkeletonRow { get; set; }
        List<DbRow>? _skeletonRows;

        DbRow? ReadSkeletonRow(string name)
        {
            if (_skeletonRows == null)
            {
                var problems = new List<string>();
                _skeletonRows = DbTables.Read(_packFileService, "battle_skeletons_tables", problems);
                problems.ForEach(LogOnce);
            }
            return _skeletonRows.FirstOrDefault(x => string.Equals(x.Text("name"), name, StringComparison.OrdinalIgnoreCase));
        }

        // SPLICE versions 11 and 12 (10,288 of vanilla's 10,306; SPLICE_OVERRIDE inherits the class and is left out). Which
        // bones it drives is SpliceRule.BoneWeights. An additive splice adds the difference between its animation and the first
        // frame of its reference animation, or the bind pose when it names none, as 4,347 of vanilla's 4,528 additive splices
        // do. Stretch, Exclude Stripped and Primary Persistent are not applied. Null, logged once, when there is nothing to draw.
        SpliceRule? CreateSplice(Splice_v11 tag, ISkeletonProvider skeleton)
        {
            if (skeleton.Skeleton is not { } gameSkeleton || LoadSpliced(tag, tag.Animation, gameSkeleton) is not { } animation)
                return null;
            AnimationFrame? reference = null;
            if (SpliceRule.On(tag.Additive) && tag.ReferenceAnimation.Length != 0)
            {
                if (LoadSpliced(tag, tag.ReferenceAnimation, gameSkeleton) is not { } referenceAnimation)
                    return null;
                reference = AnimationSampler.Sample(0, 0, gameSkeleton, new AnimationClip(referenceAnimation, gameSkeleton));
            }

            var row = SkeletonRow(gameSkeleton.SkeletonName);
            if (row == null && new[] { tag.NeckWeight, tag.TorsoWeight, tag.LeftArmWeight, tag.RightArmWeight, tag.LegsWeight }.Any(x => x > 0))
                LogOnce($"SPLICE {tag.Animation}: no battle_skeletons row in the loaded packs names {gameSkeleton.SkeletonName}, so its neck, torso, arm and leg weights are not drawn");
            var weights = SpliceRule.BoneWeights(tag, gameSkeleton, row);
            if (weights.All(x => x <= 0))
            {
                LogOnce($"SPLICE {tag.Animation}: its weights reach no bone of {gameSkeleton.SkeletonName}, so it is not drawn");
                return null;
            }
            return new SpliceRule(gameSkeleton, new AnimationClip(animation, gameSkeleton), tag, weights, reference);
        }

        // A splice's animation or reference animation; null, logged once, when it is missing or animates another skeleton.
        AnimationFile? LoadSpliced(Splice_v11 tag, string path, GameSkeleton skeleton)
        {
            if (path.Length == 0)
                return null;
            if (_packFileService.FindFile(path) is not { } file)
            {
                LogOnce($"SPLICE {tag.Animation}: {path} is not in the loaded packs, so it is not drawn");
                return null;
            }
            var animation = AnimationFile.Create(file);
            if (!string.Equals(animation.Header.SkeletonName, skeleton.SkeletonName, StringComparison.OrdinalIgnoreCase))
            {
                LogOnce($"SPLICE {tag.Animation}: {path} animates {animation.Header.SkeletonName}, not {skeleton.SkeletonName}, so it is not drawn");
                return null;
            }
            return animation;
        }

        // The first frame of the set's animation for slots[id]; null for an id outside the list or a slot the set lacks.
        AnimationFrame? LoadPose(string[] slots, int id, IAnimationBinGenericFormat? fragment, GameSkeleton skeleton)
        {
            if (id < 0 || id >= slots.Length)
                return null;
            var animPath = fragment?.Entries.FirstOrDefault(x => x.SlotName == slots[id])?.AnimationFile;
            var file = string.IsNullOrWhiteSpace(animPath) ? null : _packFileService.FindFile(animPath);
            return file == null ? null : AnimationSampler.Sample(0, 0, skeleton, new AnimationClip(AnimationFile.Create(file), skeleton));
        }

        // FacePoseId indexes these slots, in the order of the WH3 slot table (the tag's field help lists the same order).
        static readonly string[] s_facePoseSlots =
        [
            "FACE_POSE_RELAX", "FACE_POSE_RELAX_BLINK", "FACE_POSE_ANGRY", "FACE_POSE_ANGRY_BLINK", "FACE_POSE_ANGRY_SCREAM",
            "FACE_POSE_DEAD", "FACE_POSE_DEAD2", "FACE_POSE_DEATH_SCREAM", "FACE_POSE_WORRIED", "FACE_POSE_WORRIED_BLINK",
        ];

        // FACE_POSE keys pose the face the way hand-pose keys pose the fingers: from Start Time until the next key, -1
        // releasing it. Which bones the game counts as the face is not known, and CA's face-pose animations key the whole
        // skeleton, so the face is taken to be the bones the set's face poses disagree on. Across vanilla's 21 face-pose
        // folders that is the jaw, eyelids, eyebrows, eyes, lips, tongue and beard; one relax-blink pose also turns a leg and
        // a skirt bone by under a degree. The weapon bones are left out: a 64-frame blink that sits among the high elves' face
        // poses, in their folder and their campaign sets, holds them 1.6 m away. A set with fewer than two face poses has nothing to compare.
        void CreateFacePoses(ParsedMetadataFile? persistent, ParsedMetadataFile? metaData, IAnimationBinGenericFormat? fragment, ISkeletonProvider skeleton, AnimationPlayer rootPlayer)
        {
            var tags = (persistent?.GetItemsOfType<FacePose>() ?? []).Select(x => (Time: -1f, Tag: x))
                .Concat((metaData?.GetItemsOfType<FacePose>() ?? []).Select(x => (Time: x.StartTime, Tag: x)))
                .ToList();
            if (tags.Count == 0 || skeleton.Skeleton is not { } gameSkeleton)
                return;

            var poses = s_facePoseSlots.Select((_, id) => LoadPose(s_facePoseSlots, id, fragment, gameSkeleton)).ToList();
            var loaded = poses.OfType<AnimationFrame>().ToList();
            if (loaded.Count < 2)
            {
                LogOnce($"FACE_POSE: the animation set has {loaded.Count} of the {s_facePoseSlots.Length} FACE_POSE slots, too few to tell the face from the body, so faces are not posed");
                return;
            }

            static bool Differ(AnimationFrame.BoneKeyFrame a, AnimationFrame.BoneKeyFrame b) =>
                Math.Abs(Quaternion.Dot(a.Rotation, b.Rotation)) < 0.99999f || Vector3.Distance(a.Translation, b.Translation) > 1e-4f;
            var face = Enumerable.Range(0, gameSkeleton.BoneCount)
                .Where(bone => !gameSkeleton.BoneNames[bone].StartsWith("be_prop", StringComparison.OrdinalIgnoreCase))
                .Where(bone => loaded.Skip(1).Any(x => Differ(x.BoneTransforms[bone], loaded[0].BoneTransforms[bone])));
            var keys = tags.Select(x => new HandPoseRule.Key(x.Time, x.Tag.FacePoseId >= 0 && x.Tag.FacePoseId < poses.Count ? poses[x.Tag.FacePoseId] : null, x.Tag.Weight));
            rootPlayer.AnimationRules.Add(new HandPoseRule(face, keys));
        }

        // Distance Culled is on EFFECT v7 to v12.
        static bool IsDistanceCulled(IEffectMeta effect) => effect switch
        {
            Effect_v7 v => v.DistanceCulled,
            Effect_v10 v => v.DistanceCulled,
            Effect_v11 v => v.DistanceCulled,
            Effect_v12 v => v.DistanceCulled,
            _ => false,
        };

        // The vfx the tag plays: its own, or for a wind-up placeholder (EFFECT v11 and v12, type 1 or 2) the spell vfx set in its place.
        string VfxNameOf(IEffectMeta effect)
        {
            var type = effect switch { Effect_v11 v => v.EffectType, Effect_v12 v => v.EffectType, _ => 0 };
            return (type == 1 ? AuraVfx : type == 2 ? WeaponVfx : null) ?? effect.VfxName;
        }

        private IEnumerable<IMetaDataInstance> ApplyMetaData(ParsedMetadataFile? file, ParsedMetadataAttribute? selectedAttribute, SceneNode root, ISkeletonProvider skeleton, AnimationPlayer rootPlayer, IAnimationBinGenericFormat fragment, int vfxFilter, bool includeEffects = true, bool persistent = false)
        {
            var output = new List<IMetaDataInstance>();
            if (file == null)
                return output;

            output.AddRange(file.GetItemsOfType<IAnimatedPropMeta>().Select(x => Keep((ParsedMetadataAttribute)x, selectedAttribute, root, skeleton, rootPlayer, () => CreateAnimatedProp(x, root, skeleton, selectedAttribute, rootPlayer))).Where(x => x != null));

            // PROP: a model on a bone, drawn the way ANIMATED_PROP is.
            output.AddRange(file.Attributes.OfType<ParsedMetadataAttribute>().Where(x => x.Name == "PROP" && x is not IAnimatedPropMeta)
                .Select(x => Keep(x, selectedAttribute, root, skeleton, rootPlayer, () => CreateStaticProp(x, root, skeleton, selectedAttribute, rootPlayer))).Where(x => x != null));

            output.AddRange(file.GetItemsOfType<ImpactPosition_v10>().Select(meteDataItem => Guard(meteDataItem, root, () => CreateStaticLocator(meteDataItem, root, () => meteDataItem.Position, "ImpactPos", selectedAttribute))).Where(x => x != null));

            output.AddRange(file.GetItemsOfType<TargetPos_10>().Select(meteDataItem => Guard(meteDataItem, root, () => CreateStaticLocator(meteDataItem, root, () => meteDataItem.Position, "TargetPos", selectedAttribute))).Where(x => x != null));

            output.AddRange(file.GetItemsOfType<FirePos_v10>().Select(meteDataItem => Guard(meteDataItem, root, () => CreateStaticLocator(meteDataItem, root, () => meteDataItem.Position, "FirePos", selectedAttribute))).Where(x => x != null));

            output.AddRange(file.GetItemsOfType<CameraShakePos>().Select(x => Guard(x, root, () => CreateStaticLocator(x, root, () => x.Position, "CameraShake", selectedAttribute))).Where(x => x != null));

            output.AddRange(file.GetItemsOfType<SplashAttack_v10>().Select(meteDataItem => Guard(meteDataItem, root, () => CreateSplashAttack(meteDataItem, root, $"SplashAttack_{Math.Round(meteDataItem.EndTime, 2)}", 0.1f, selectedAttribute))).Where(x => x != null));

            // SOUND_TRIGGER, CREW_LOCATION and SOUND_BUILDING: a marker at the tag's position, on its bone where the tag names one.
            // BLOOD is drawn as an EFFECT, below, and CAMERA_SHAKE_POS as a locator, above. TRANSFORM is left out: it moves its bone,
            // which its animation rule already shows, so a marker on that bone would show the offset twice.
            output.AddRange(file.Attributes.OfType<ParsedMetadataAttribute>()
                .Select(x => SpatialMetaDataCatalog.TryCreate(x, out var binding) && binding.UsesGenericMarker
                    && binding.Kind is not (SpatialMetaDataKind.Prop or SpatialMetaDataKind.Blood or SpatialMetaDataKind.CameraShake or SpatialMetaDataKind.Transform) ? binding : null)
                .Where(x => x != null)
                .Select(x => Guard(x!.Source, root, () => CreateSpatialMarker(x!, root, skeleton, rootPlayer, selectedAttribute))).Where(x => x != null));

            if (includeEffects)
            {
                output.AddRange(file.GetItemsOfType<IEffectMeta>().Where(x => FilterOf(x) == 0 || FilterOf(x) == vfxFilter).Where(x => !(HideDistanceCulled && IsDistanceCulled(x)))
                    .Select(x => Keep((ParsedMetadataAttribute)x, selectedAttribute, root, skeleton, rootPlayer, () => CreateEffect(x, (ParsedMetadataAttribute)x, "Effect", root, skeleton, selectedAttribute, rootPlayer, persistent))).Where(x => x != null));
                output.AddRange(file.GetItemsOfType<Blood_v11>().Select(x => Keep(x, selectedAttribute, root, skeleton, rootPlayer, () => CreateEffect(new BloodEffect(x), x, "Blood", root, skeleton, selectedAttribute, rootPlayer, persistent))).Where(x => x != null));
            }

            // A prop docked to both hands from the same moment goes to the right hand, whichever tag comes first: in all
            // nine vanilla cases where the animation's own keys hold the prop rigid to one hand, it is the right one.
            var docks = file.GetItemsOfType<DockEquipment>();
            static bool Hand(DockEquipment x, string side) => x.AnimationSlotName.StartsWith($"DOCK_EQUIPMENT_{side}_HAND");
            foreach (var meteDataItem in docks.Where(l => !Hand(l, "LEFT") || !docks.Any(r => Hand(r, "RIGHT") && r.PropBoneId == l.PropBoneId && r.StartTime == l.StartTime)))
                Guard(meteDataItem, null, () => { CreateEquipmentDock(meteDataItem, fragment, skeleton, rootPlayer); return (object?)null; });

            foreach (var meteDataItem in file.GetItemsOfType<Transform_v10>())
                CreateTransform(meteDataItem, rootPlayer);

            return output;
        }

        void Problem(ParsedMetadataAttribute tag, string reasonKey, string detail = "") => _problems.Add(new MetaDataBuildProblem(tag, reasonKey, detail));

        // One tag that will not draw is a problem on that tag, and what it had added to the scene is taken out again; the
        // rest of the scene is still built.
        T? Guard<T>(ParsedMetadataAttribute tag, SceneNode? root, Func<T?> build) where T : class
        {
            var before = root?.Children.Count ?? 0;
            try
            {
                return build();
            }
            catch (Exception e)
            {
                while (root != null && root.Children.Count > before)
                    root.RemoveObject(root.Children[^1]);
                LogOnce($"{tag.DisplayName}: {e.Message}");
                Problem(tag, "SuperView.Problems.PreviewFailed", e.Message);
                return null;
            }
        }

        private void CreateTransform(Transform_v10 transform, AnimationPlayer rootPlayer)
        {
            var rule = new TransformBoneRule(transform);
            rootPlayer.AnimationRules.Add(rule);
        }

        private void CreateEquipmentDock(DockEquipment metaData, IAnimationBinGenericFormat fragment, ISkeletonProvider skeleton, AnimationPlayer rootPlayer)
        {
            if (skeleton.Skeleton == null)
                return;
            if (fragment == null)
            {
                Problem(metaData, "SuperView.Problems.NoAnimationBin");
                return;
            }
 
            // A _2 tag poses with the hand's second position. Either tag falls back to the other position when the set lacks its own.
            var slot = metaData.AnimationSlotName;
            var animPath = fragment.Entries.FirstOrDefault(x => x.SlotName == slot)?.AnimationFile ??
                           fragment.Entries.FirstOrDefault(x => x.SlotName == (slot.EndsWith("_2") ? slot[..^2] : slot + "_2"))?.AnimationFile;
            if (animPath == null)
            {
                Problem(metaData, "SuperView.Problems.MissingDockSlot", metaData.AnimationSlotName);
                return;
            }

            var finalBoneIndex = -1;
            foreach (var potentialBoneName in metaData.SkeletonNameAlternatives)
            {
                finalBoneIndex = skeleton.Skeleton.GetBoneIndexByName(potentialBoneName);
                if (finalBoneIndex != -1)
                    break;
            }

            if (finalBoneIndex == -1)
            {
                Problem(metaData, "SuperView.Problems.MissingDockBone", string.Join(", ", metaData.SkeletonNameAlternatives));
                return;
            }

            var pf = _packFileService.FindFile(animPath);
            if (pf == null)
            {
                Problem(metaData, "SuperView.Problems.MissingAnimation", animPath);
                return;
            }

            var animFile = AnimationFile.Create(pf);
            var clip = new AnimationClip(animFile, skeleton.Skeleton);

            var rule = new DockEquipmentRule(finalBoneIndex, metaData.PropBoneId, clip, skeleton, metaData.StartTime, metaData.EndTime, rootPlayer.AnimationRules);
            rootPlayer.AnimationRules.Add(rule);
        }

        private IMetaDataInstance? CreateAnimatedProp(IAnimatedPropMeta animatedPropMeta, SceneNode root, ISkeletonProvider rootSkeleton, ParsedMetadataAttribute? selectedMetaDataAttribute, AnimationPlayer rootPlayer) =>
            CreateProp((ParsedMetadataAttribute)animatedPropMeta, "Animated_prop", animatedPropMeta.ModelName, animatedPropMeta.AnimationName, animatedPropMeta.Scale, root, rootSkeleton, selectedMetaDataAttribute, rootPlayer);

        // PROP v2 to v15 name their model and animation alike; Scale is on v13 and later.
        private IMetaDataInstance? CreateStaticProp(ParsedMetadataAttribute prop, SceneNode root, ISkeletonProvider rootSkeleton, ParsedMetadataAttribute? selectedMetaDataAttribute, AnimationPlayer rootPlayer)
        {
            string? Text(string name) => prop.GetType().GetProperty(name)?.GetValue(prop) as string;
            var scale = prop.GetType().GetProperty("Scale")?.GetValue(prop) is float value ? value : 1;
            return CreateProp(prop, "Prop", Text("ModelName") ?? "", Text("AnimationName") ?? "", scale, root, rootSkeleton, selectedMetaDataAttribute, rootPlayer);
        }

        private IMetaDataInstance? CreateProp(ParsedMetadataAttribute tag, string propName, string modelName, string animationName, float scale, SceneNode root, ISkeletonProvider rootSkeleton, ParsedMetadataAttribute? selectedMetaDataAttribute, AnimationPlayer rootPlayer)
        {
            var color = selectedMetaDataAttribute == tag ? s_selectedColor : s_color;
            // Where the prop sits: its bone, and its position and orientation on it, read from the tag as it is edited.
            if (!SpatialMetaDataCatalog.TryCreate(tag, out var binding))
                return null;
            var (startTime, endTime) = ParsedMetadataTimeRange.TryCreate(tag, out var range) ? (range.StartTime, range.EndTime) : (0f, 0f);
            // PROP 4 and later, and every ANIMATED_PROP from 10, carry Override Prop.
            var overrideProp = tag.GetType().GetProperty("OverrideProp")?.GetValue(tag) is int value ? value : 0;

            var meshPath = _packFileService.FindFile(modelName);
            if (meshPath == null)
            {
                Problem(tag, "SuperView.Problems.MissingModel", modelName);
                return null;
            }

            // A prop whose animation or skeleton is missing is still drawn, at the tag, without its own animation.
            var animationPath = string.IsNullOrWhiteSpace(animationName) ? null : _packFileService.FindFile(animationName);
            if (animationPath == null && !string.IsNullOrWhiteSpace(animationName))
                Problem(tag, "SuperView.Problems.MissingPropAnimation", animationName);
            // Not registered with the animation container: the prop runs on the main animation's clock (AnimatedPropInstance).
            var propPlayer = new AnimationPlayer();

            // Configure the mesh
            var loadedNode = _complexMeshLoader.Load(meshPath, new GroupNode(propName), propPlayer, true, true); 

            // Configure animation
            var rootFollowsBone = false;
            Matrix? bindRoot = null;
            var animFile = animationPath == null ? null : AnimationFile.Create(animationPath);
            // A rigid model names no skeleton; its animation does.
            var skeletonName = animFile == null ? null : SceneNodeHelper.GetSkeletonName(loadedNode) is { Length: > 0 } fromMesh ? fromMesh : animFile.Header.SkeletonName;
            var skeletonFile = skeletonName == null ? null : _skeletonAnimationLookUpHelper.GetSkeletonFileFromName(skeletonName);
            if (skeletonName != null && skeletonFile == null)
            {
                Problem(tag, "SuperView.Problems.MissingPropSkeleton", skeletonName);
                animationPath = null;
            }

            if (animationPath != null && skeletonFile != null)
            {
                var skeleton = new GameSkeleton(skeletonFile, propPlayer);
                var clip = new AnimationClip(animFile!, skeleton);
                propPlayer.SetAnimation(clip, skeleton);
                // The prop's first frame against the bone when the tag starts, which is when that frame plays.
                rootFollowsBone = CopyRootTransform.RootFollowsBone(skeleton, clip, BoneAt(binding.BoneIndex ?? -1, rootSkeleton, rootPlayer)(startTime));
                if (!rootFollowsBone && CopyRootTransform.RootKeyIsFixedTurn(skeleton, clip))
                    bindRoot = skeleton.GetWorldTransform(0);

                // Add the prop skeleton
                var skeletonSceneNode = new SkeletonNode(skeleton)
                {
                    NodeColour = color
                };
                loadedNode.AddObject(skeletonSceneNode);
            }

            loadedNode.ForeachNodeRecursive((node) =>
            {
                if (node is ISelectable selectableNode)
                    selectableNode.IsSelectable = false;
            });

            // A prop with its own animation has its root bone put on the tag's bone by the player's rule, which applies the tag's
            // Scale in the bones; one without has no frames for a rule to act on, so the instance places its node.
            var placedByAnimation = animationPath != null;
            if (placedByAnimation)
            {
                var animationRule = new CopyRootTransform(rootSkeleton, binding.BoneIndex ?? -1, () => binding.Position, () => binding.Orientation ?? Quaternion.Identity, scale, rootFollowsBone, bindRoot);
                propPlayer.AnimationRules.Add(animationRule);
            }
            else
                loadedNode.ScaleMult = scale;

            // Add to scene
            root.AddObject(loadedNode);

            var instance = new AnimatedPropInstance(loadedNode, propPlayer, startTime, endTime, WeaponShown(rootSkeleton, overrideProp), binding, rootSkeleton, placedByAnimation);
            instance.Update(rootPlayer.GetTimeUs() / 1_000_000f);
            return instance;
        }

        // Override Prop n (1-7) names be_prop_(n-1), DOCK_EQPT's numbering: the game hides the whole tag while the animation set
        // shows a weapon there, and leaves that weapon drawn. Read live, since the set's weapon flags change without a rebuild
        // (BinAnimationViewModel sets the slots' IsVisible). "A weapon is showing" rather than "the flag is set": the game test
        // had a weapon on the bone, so it cannot tell the two apart, and an empty slot has nothing to give way to.
        public static Func<bool> WeaponShown(ISkeletonProvider skeleton, int overrideProp)
        {
            if (overrideProp < 1 || skeleton is not SceneObject scene)
                return () => false;
            var bone = $"be_prop_{overrideProp - 1}";
            // Walked each frame, so a model swapped under the props is still the one asked.
            return () => scene.ModelNode != null && SceneNodeHelper.GetChildrenOfType<SlotNode>(scene.ModelNode)
                .Any(x => x.IsVisible && x.Children.Count > 0 && string.Equals(x.AttachmentBoneName, bone, StringComparison.OrdinalIgnoreCase));
        }

        private IMetaDataInstance CreateStaticLocator(DecodedMetaEntryBase metaData, SceneNode root, Func<Vector3> positionOf, string displayName, ParsedMetadataAttribute? selectedMetaDataAttribute, float scale = 0.3f)
        {
            var color = selectedMetaDataAttribute == metaData ? s_selectedColor : s_color;
            var position = positionOf();

            var node = new SimpleDrawableNode(displayName);
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, WithTime(displayName, metaData.StartTime, metaData.EndTime), position, color));
            node.AddItem(LineHelper.AddCircle(position, scale, color));
            root.AddObject(node);

            return new FiringInstance(new DrawableMetaInstance(metaData, node, () => [new(positionOf(), MetaDataMarkerPoint.Default)]), node, metaData.StartTime, metaData.EndTime);
        }

        // SOUND_TRIGGER, CREW_LOCATION and SOUND_BUILDING: the tag's name and a cross at its position; one that can turn also gets
        // its X, Y and Z axes. Drawn in the tag's frame, and placed from its values on its bone on every update.
        // CREW_LOCATION is where an engine's crew member stands and which way it faces, in the model's space; its filter names the
        // crew member: crew1 to crew3 and reserve1 to reserve3 on every vanilla tag that has one. All 740 vanilla tags are 0/0, for
        // the whole animation. A SOUND_TRIGGER rides its bone and, like the other event locators, turns yellow when it fires.
        private IMetaDataInstance CreateSpatialMarker(SpatialMetaDataBinding binding, SceneNode root, ISkeletonProvider skeleton, AnimationPlayer rootPlayer, ParsedMetadataAttribute? selectedAttribute)
        {
            const float Scale = 0.2f;
            static string Crew(string filter) => "Crew:" + (string.IsNullOrWhiteSpace(filter) ? "(no filter)" : filter);
            var (label, fires) = binding.Source switch
            {
                SoundTrigger_v10 x => ("Sound:" + x.SoundEvent, true),
                SoundTrigger_v4 x => ("Sound:" + x.SoundEvent, true),
                CrewLocation_v10 x => (Crew(x.Filter), false),
                CrewLocation_v2 x => (Crew(x.Filter), false),
                _ => (binding.Source.DisplayName, false),
            };
            (float Start, float End)? times = fires && ParsedMetadataTimeRange.TryCreate(binding.Source, out var range) ? (range.StartTime, range.EndTime) : null;
            // The node keeps the bare name; only the text drawn beside the marker carries the times.
            var name = label;
            if (times is { } window)
                label = WithTime(label, window.Start, window.End);

            var color = selectedAttribute == binding.Source ? s_selectedColor : s_color;
            var node = new SimpleDrawableNode(name);
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, label, Vector3.Zero, color));
            node.AddItem(LineHelper.AddLocator(Vector3.Zero, Scale, color));
            if (binding.CanRotate)
            {
                node.AddItem(LineHelper.AddLine(Vector3.Zero, Vector3.UnitX * Scale, Color.Red));
                node.AddItem(LineHelper.AddLine(Vector3.Zero, Vector3.UnitY * Scale, Color.Green));
                node.AddItem(LineHelper.AddLine(Vector3.Zero, Vector3.UnitZ * Scale, Color.Blue));
            }
            root.AddObject(node);

            Matrix Local() => Matrix.CreateFromQuaternion(binding.Orientation ?? Quaternion.Identity) * Matrix.CreateTranslation(binding.Position);
            var marker = new DrawableMetaInstance(binding.Source, node, () => [new(binding.Position, MetaDataMarkerPoint.Default)], Local);
            var bone = binding.BoneIndex ?? -1;
            if (binding.AttachToBone && bone >= 0 && bone < (skeleton.Skeleton?.BoneCount ?? 0))
                marker.FollowBone(skeleton, bone);
            IMetaDataInstance instance = times is { } on ? new FiringInstance(marker, node, on.Start, on.End) : marker;
            instance.Update(rootPlayer.GetTimeUs() / 1_000_000f);
            return instance;
        }

        private IMetaDataInstance CreateSplashAttack(SplashAttack_v10 splashAttack, SceneNode root, string displayName, float scale, ParsedMetadataAttribute? selectedAttribute)
        {
            // A half-typed or brand-new tag (all zeros) gets its two ends drawn, which the gizmo can pick and drag apart, and a
            // problem saying why there is no area. Upstream throws instead, which leaves nothing to pick.
            var distance = Vector3.Distance(splashAttack.StartPosition, splashAttack.EndPosition);
            var color = selectedAttribute == splashAttack ? s_selectedColor : s_color;

            var node = new SimpleDrawableNode(displayName);
            var textPos = (splashAttack.EndPosition + splashAttack.StartPosition) / 2;
            
            node.AddItem( new WorldTextRenderItem(_resourceLibrary, "StartPos", splashAttack.StartPosition, color));
            node.AddItem(LineHelper.AddLocator(splashAttack.StartPosition, scale, color));
            
            node.AddItem( new WorldTextRenderItem(_resourceLibrary, "EndPos", splashAttack.EndPosition, color));
            node.AddItem(LineHelper.AddLocator(splashAttack.EndPosition, scale, color));
            
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, WithTime("SplashAttack", splashAttack.StartTime, splashAttack.EndTime), textPos, color));
            node.AddItem(LineHelper.AddLine(splashAttack.StartPosition, splashAttack.EndPosition, color));
            root.AddObject(node);
            var instance = new FiringInstance(new DrawableMetaInstance(splashAttack, node, () =>
            [
                new(splashAttack.StartPosition, MetaDataMarkerPoint.SplashStart, scale * 2),
                new(splashAttack.EndPosition, MetaDataMarkerPoint.SplashEnd, scale * 2),
            ]), node, splashAttack.StartTime, splashAttack.EndTime);

            if (MathUtil.CompareEqualFloats(distance))
            {
                Problem(splashAttack, "SuperView.Problems.PreviewFailed", "its start and end positions are the same point, so only they are drawn");
                return instance;
            }

            var normal = splashAttack.EndPosition - splashAttack.StartPosition;  // corresponds to Z
            normal.Normalize();
            var random = new Random(1);
            Func<Random, float> RandomFloat = r => (float)(2 * r.NextDouble() - 1);
            var vectorP = new Vector3(RandomFloat(random), RandomFloat(random), RandomFloat(random));
            vectorP.Normalize();

            var planeVectorP = Vector3.Cross(normal, Vector3.Cross(vectorP, normal)); // corresponds to X
            var planeVectorPN = Vector3.Cross(vectorP, normal); // corresponds to Y
            planeVectorP.Normalize();
            planeVectorPN.Normalize();

            var rotationM = MathUtil.CreateRotation(
            [
                planeVectorP,
                planeVectorPN,
                normal
            ]);

            if (splashAttack.AoeShape == 0) // Cone or Sphere
            {
                if (MathUtil.CompareEqualFloats(splashAttack.AngleForCone / 2, tolerance: 0.1f))
                {
                    Problem(splashAttack, "SuperView.Problems.PreviewFailed", "its cone's angle is 0, so only its ends are drawn");
                    return instance;
                }
                var transformationM = rotationM * Matrix.CreateScale(distance) * Matrix.CreateTranslation(splashAttack.StartPosition);
                node.AddItem(LineHelper.AddConeSplash(splashAttack.StartPosition, splashAttack.EndPosition, transformationM, splashAttack.AngleForCone, color));
            }
            if (splashAttack.AoeShape == 1) // Corridor
            {
                if (MathUtil.CompareEqualFloats(splashAttack.WidthForCorridor, tolerance: 0.001f))
                {
                    Problem(splashAttack, "SuperView.Problems.PreviewFailed", "its corridor's width is 0, so only its ends are drawn");
                    return instance;
                }
                var transformationM = rotationM * Matrix.CreateScale(splashAttack.WidthForCorridor / 2) * Matrix.CreateTranslation(splashAttack.StartPosition);
                node.AddItem(LineHelper.AddCorridorSplash(splashAttack.StartPosition, splashAttack.EndPosition, transformationM, color));
            }

            return instance;
        }

        // BLOOD carries an EFFECT's fields and is drawn as one. Version 12 declares Position, Orientation, NodeIndex and
        // Scale again with `new`, so they are read through the type the tag really is.
        sealed class BloodEffect(Blood_v11 tag) : IEffectMeta
        {
            public string VfxName { get; set; } = tag.VfxName;
            public bool Tracking { get; set; } = tag.Tracking;
            public Vector3 Position { get => tag is Blood_v12 v ? v.Position : tag.Position; set { } }
            public Vector4 Orientation { get => tag is Blood_v12 v ? v.Orientation : tag.Orientation; set { } }
            public int NodeIndex { get; set; } = tag is Blood_v12 v ? v.NodeIndex : tag.NodeIndex;
            public float Scale { get; } = tag is Blood_v12 v ? v.Scale : tag.Scale;
            public float EffectStartTime { get; set; } = tag.StartTime;
            public float EffectEndTime { get; set; } = tag.EndTime;
        }

        private IMetaDataInstance CreateEffect(IEffectMeta effect, ParsedMetadataAttribute tag, string kind, SceneNode root, ISkeletonProvider skeleton, ParsedMetadataAttribute? selectedAttribute, AnimationPlayer rootPlayer, bool persistent)
        {
            var vfxName = VfxNameOf(effect);
            var resolved = ShowVfx ? Resolve(effect, vfxName) : null;
            if (resolved is { Found: false })
            {
                LogOnce($"{tag.Name} {vfxName}: {VfxResolver.PathOf(vfxName)} is not in any loaded pack, so only its locator is drawn");
                Problem(tag, "SuperView.Problems.MissingEffect", VfxResolver.PathOf(vfxName));
            }
            foreach (var problem in resolved?.Problems ?? [])
                LogOnce($"{tag.Name} {vfxName}: {problem}");

            var color = selectedAttribute == tag ? s_selectedColor : s_color;
            var locatorScale = 0.3f;
            var textOffset = locatorScale * 1.5f + 0.01f;

            var rotMatrix = Matrix.CreateFromQuaternion(new Quaternion(effect.Orientation));

            // Drawn in the tag's own frame: the instance places it from the tag's Position and Orientation on the bone.
            var node = new SimpleDrawableNode($"{kind}:{vfxName}");
            node.AddItem(LineHelper.AddLine(Vector3.Zero, Vector3.UnitX * locatorScale, Color.Red));
            node.AddItem(LineHelper.AddLine(Vector3.Zero, Vector3.UnitY * locatorScale, Color.Green));
            node.AddItem(LineHelper.AddLine(Vector3.Zero, Vector3.UnitZ * locatorScale, Color.Blue));

            var label = resolved is { Found: false } ? vfxName + " (not found)" : vfxName;
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, label, Vector3.Zero, color));
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, "X", Vector3.UnitX * textOffset, Color.Red));
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, "Y", Vector3.UnitY * textOffset, Color.Green));
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, "Z", Vector3.UnitZ * textOffset, Color.Blue));

            root.AddObject(node);

            // An untracked effect stays where its bone was at the tag's Start Time.
            var startTime = effect.EffectStartTime;
            var boneAt = BoneAt(effect.NodeIndex, skeleton, rootPlayer);
            if (!effect.Tracking)
            {
                var atStart = boneAt(startTime);
                boneAt = _ => atStart;
            }

            if (resolved is not { Found: true })
            {
                var locatorOnly = new VfxEffectInstance(node, boneAt, startTime, effect: effect, source: tag);
                locatorOnly.Update(rootPlayer.GetTimeUs() / 1_000_000f);
                return locatorOnly;
            }

            var settings = resolved.Emitters.Select(EmitterSettings.From).ToList();
            foreach (var emitter in settings)
            {
                foreach (var gap in emitter.Skipped)
                    LogOnce($"{tag.Name} {vfxName}, emitter {emitter.Name}: {gap}");
            }

            var offset = Matrix.CreateScale(ScaleOf(effect)) * rotMatrix * Matrix.CreateTranslation(effect.Position);
            // A 0/0 tag (or one ending before it starts) spawns for the whole animation.
            var spawnEnd = effect.EffectEndTime > startTime ? effect.EffectEndTime - startTime : float.PositiveInfinity;
            // A persistent effect's clock runs past the clip's end; its bone is read where the looping clip is then.
            var loop = persistent ? rootPlayer.AnimationClip?.PlayTimeInSec ?? 0 : 0;
            float MainTime(float effectTime) => loop > 0 ? ((effectTime + startTime) % loop + loop) % loop : effectTime + startTime;
            var simulator = new ParticleSimulator(settings, StableSeed(effect), t => offset * boneAt(MainTime(t)), spawnEnd,
                message => LogOnce($"{tag.Name} {vfxName}, {message}"));
            var particles = root.AddObject(new VfxEffectNode("Vfx:" + vfxName,
                new ParticleRenderItem(simulator, _vfxTextures, () => _shaders.GetStaticEffect(ShaderTypes.Particle), _vfxMeshes)));

            var instance = new VfxEffectInstance(node, boneAt, startTime, particles, simulator, loop,
                t => _vfxAlpha.TryGetValue(root, out var keys) ? VfxAlphaAt(keys, t) : 1, effect, tag);
            instance.Update(rootPlayer.GetTimeUs() / 1_000_000f);
            return instance;
        }

        // The tag's bone at a time on the main clock, sampled from the main clip, so a re-simulation can ask
        // for any time. -1, or a bone the skeleton lacks, is the model's origin.
        // ponytail: sampled without the player's rules (hand poses, TRANSFORM); pass rootPlayer.AnimationRules
        // if an effect is ever placed on a bone a rule moves.
        public Func<float, Matrix> BoneAt(int boneIndex, ISkeletonProvider skeleton, AnimationPlayer rootPlayer) => mainTime =>
        {
            var gameSkeleton = skeleton.Skeleton;
            if (gameSkeleton == null || boneIndex < 0 || boneIndex >= gameSkeleton.BoneCount)
                return Matrix.Identity;
            var clip = rootPlayer.AnimationClip;
            if (clip == null || clip.PlayTimeInSec <= 0)
                return gameSkeleton.GetWorldTransform(boneIndex);
            return BonesAt(Math.Clamp(mainTime / clip.PlayTimeInSec, 0, 1), gameSkeleton, clip)[boneIndex];
        };

        // Every bone in the world at a time through the clip, sampled once for all the effects that ask.
        // ponytail: one entry per distinct time asked, dropped wholesale past 2048 or when the clip or skeleton changes.
        Matrix[] BonesAt(float t, GameSkeleton skeleton, AnimationClip clip)
        {
            if (_bonesOf != (clip, skeleton) || _bones.Count > 2048)
            {
                _bones.Clear();
                _bonesOf = (clip, skeleton);
            }
            if (_bones.TryGetValue(t, out var bones))
                return bones;
            var frame = AnimationSampler.Sample(t, skeleton, clip);
            bones = new Matrix[skeleton.BoneCount];
            for (var i = 0; i < bones.Length; i++)
                bones[i] = frame.GetSkeletonAnimatedWorld(skeleton, i);
            return _bones[t] = bones;
        }

        IMetaDataInstance? Keep(ParsedMetadataAttribute tag, ParsedMetadataAttribute? picked, SceneNode root, ISkeletonProvider skeleton, AnimationPlayer rootPlayer, Func<IMetaDataInstance?> build)
        {
            var type = tag.GetType();
            if (!s_tagValues.TryGetValue(type, out var values))
                s_tagValues[type] = values = type.GetProperties().Where(x => x.GetCustomAttribute<MetaDataTagAttribute>() != null).ToArray();
            var keptAs = new KeptAs(string.Join("\u001f", values.Select(x => x.GetValue(tag))), tag == picked, ShowVfx, AuraVfx, WeaponVfx, rootPlayer.AnimationClip, skeleton.Skeleton);
            if (_previous.TryGetValue(tag, out var kept) && kept.As == keptAs && root.Children.Contains(kept.Node))
            {
                _kept[tag] = kept;
                _problems.AddRange(kept.Problems);
                return kept.Instance;
            }

            // What a build adds to the scene starts with the node its instance cleans up; the problems it finds are kept
            // with it, as a kept tag is not built again to find them.
            var before = root.Children.Count;
            var problemsBefore = _problems.Count;
            var instance = Guard(tag, root, build);
            if (instance != null && root.Children.Count > before)
                _kept[tag] = new Kept(keptAs, root.Children[before], instance, [.. _problems.Skip(problemsBefore)]);
            return instance;
        }

        VfxResolveResult Resolve(IEffectMeta effect, string vfxName)
        {
            var key = (vfxName, StableSeed(effect));
            if (!_resolved.TryGetValue(key, out var resolved))
                _resolved[key] = resolved = new VfxResolver(_packFileService).Resolve(vfxName, new Random(key.Item2));
            return resolved;
        }

        // Scale is on EFFECT v3, v5, v7, v11 and v12, not on IEffectMeta; the other versions draw at 1.
        static float ScaleOf(IEffectMeta effect) => effect switch
        {
            Effect_v3 e => e.Scale,
            Effect_v7 e => e.Scale,
            Effect_v11 e => e.Scale,
            Effect_v12 e => e.Scale,
            BloodEffect e => e.Scale,
            _ => 1,
        };

        // The same tag gets the same particles on every rebuild; string.GetHashCode differs per process.
        static int StableSeed(IEffectMeta effect)
        {
            var hash = 17;
            foreach (var c in effect.VfxName)
                hash = hash * 31 + c;
            return hash ^ (effect.NodeIndex * 7919) ^ BitConverter.SingleToInt32Bits(effect.EffectStartTime);
        }

        void LogOnce(string message)
        {
            lock (s_loggedVfxMessages)
            {
                if (!s_loggedVfxMessages.Add(message))
                    return;
            }
            _logger.Here().Warning(message);
        }
    }
}
