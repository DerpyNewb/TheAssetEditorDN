using System.Reflection;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using Editors.AnimationMeta.SuperView.Visualisation.Rules;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
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

namespace Editors.AnimationMeta.SuperView.Visualisation
{
    public interface IMetaDataBuilder
    {
        // Off, an EFFECT tag draws its locator and no particles.
        bool ShowVfx { get; set; }
        // The battle entity's vfx_filter_id: an EFFECT tag whose id is neither 0 nor this is not drawn. Unset, the lowest id the metas carry.
        int? VfxFilterId { get; set; }
        /// <summary>Lets go of what was loaded from these pack paths, so the next Create reads them again.</summary>
        void Forget(IEnumerable<string> paths);
        List<IMetaDataInstance> Create(ParsedMetadataFile? persistent, ParsedMetadataFile? metaData, ParsedMetadataAttribute? selectedMetaDataAttribute, SceneNode root, ISkeletonProvider skeleton, AnimationPlayer rootPlayer, IAnimationBinGenericFormat fragment);
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
        (AnimationClip? Clip, GameSkeleton? Skeleton) _bonesOf;

        // A slider in the meta panel rebuilds on every step, and building every effect and prop again still took 220 ms of
        // each on that Taurus. One whose tag values, colour, vfx toggle, clip and skeleton are as they were, and which is still
        // in the scene, is kept as built; the caller cleans up only what Create no longer returns.
        record KeptAs(string Values, bool Picked, bool ShowVfx, AnimationClip? Clip, GameSkeleton? Skeleton);
        record Kept(KeptAs As, ISceneNode Node, IMetaDataInstance Instance);
        Dictionary<ParsedMetadataAttribute, Kept> _kept = new(ReferenceEqualityComparer.Instance);
        Dictionary<ParsedMetadataAttribute, Kept> _previous = new(ReferenceEqualityComparer.Instance);
        static readonly Dictionary<Type, PropertyInfo[]> s_tagValues = [];

        private static Color s_color = Color.Black;
        private static Color s_selectedColor = Color.Red;

        public bool ShowVfx { get; set; } = true;
        public int? VfxFilterId { get; set; }

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

            // Apply persistent meta data, if no disable is given.
            var usePersistent = metaData == null || metaData.GetItemsOfType<DisablePersistant_v10>().Count == 0;
            // DISABLE_PERSISTENT_VFX drops the persistent meta's effects and keeps the rest of it.
            var usePersistentVfx = metaData == null || metaData.GetItemsOfType<DisablePersistantVfx_v10>().Count == 0;
            var vfxFilter = VfxFilterId ?? VfxFilterIds(persistent, metaData).FirstOrDefault();
            if (usePersistent)
            {
                var metaDataPersistent = ApplyMetaData(persistent, selectedMetaDataAttribute, root, skeleton, rootPlayer, fragment, vfxFilter, usePersistentVfx, persistent: true);
                output.AddRange(metaDataPersistent);
            }

            var metaDataInstances = ApplyMetaData(metaData, selectedMetaDataAttribute,root, skeleton, rootPlayer, fragment, vfxFilter);
            output.AddRange(metaDataInstances);

            // Hand poses are keys across both files, so they are built from both at once.
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

        static List<T> Both<T>(ParsedMetadataFile? persistent, ParsedMetadataFile? metaData) =>
            [.. persistent?.GetItemsOfType<T>() ?? [], .. metaData?.GetItemsOfType<T>() ?? []];

        // Whether a tag that changes the model is on at a time. Start < End is the window between them; Start = End (or End
        // before Start) is on from Start to the end of the clip, so 0/0 is the whole clip. That is how vanilla's DISABLE_MODEL
        // tags read: 0/0 on the engines' "invisible" stands, an instant where a matched-combat victim is swallowed.
        public static bool IsOn(DecodedMetaEntryBase tag, float time) =>
            tag.EndTime > tag.StartTime ? time >= tag.StartTime && time <= tag.EndTime : time >= tag.StartTime;

        // ALPHA tags are keys: all 1,400 vanilla ones are instants, at 0 or 1, and a death keys 1 a moment before its 0,
        // which only means something if the game fades from one key to the next. Read as fades or as snaps, the alpha is 0
        // from a 0 key until the next key, unless that key is above 0, where a fade would already be under way. The preview
        // draws meshes opaque, so it hides the model only where both readings agree it is invisible. Keys sorted by time.
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
            var tags = (persistent?.GetItemsOfType<HandPose>() ?? []).Select(x => (Time: -1f, Tag: x))
                .Concat((metaData?.GetItemsOfType<HandPose>() ?? []).Select(x => (Time: x.StartTime, Tag: x)))
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

        private IEnumerable<IMetaDataInstance> ApplyMetaData(ParsedMetadataFile? file, ParsedMetadataAttribute? selectedAttribute, SceneNode root, ISkeletonProvider skeleton, AnimationPlayer rootPlayer, IAnimationBinGenericFormat fragment, int vfxFilter, bool includeEffects = true, bool persistent = false)
        {
            var output = new List<IMetaDataInstance>();
            if (file == null)
                return output;

            output.AddRange(file.GetItemsOfType<IAnimatedPropMeta>().Select(x => Keep((ParsedMetadataAttribute)x, selectedAttribute, root, skeleton, rootPlayer, () => CreateAnimatedProp(x, root, skeleton, selectedAttribute, rootPlayer))).Where(x => x != null));

            output.AddRange(file.GetItemsOfType<ImpactPosition_v10>().Select(meteDataItem => CreateStaticLocator(meteDataItem, root, meteDataItem.Position, "ImpactPos", selectedAttribute)));

            output.AddRange(file.GetItemsOfType<TargetPos_10>().Select(meteDataItem => CreateStaticLocator(meteDataItem, root, meteDataItem.Position, "TargetPos", selectedAttribute)));

            output.AddRange(file.GetItemsOfType<FirePos_v10>().Select(meteDataItem => CreateStaticLocator(meteDataItem, root, meteDataItem.Position, "FirePos", selectedAttribute)));

            output.AddRange(file.GetItemsOfType<CameraShakePos>().Select(x => CreateStaticLocator(x, root, x.Position, "CameraShake", selectedAttribute)));

            output.AddRange(file.GetItemsOfType<SplashAttack_v10>().Select(meteDataItem => CreateSplashAttack(meteDataItem, root, $"SplashAttack_{Math.Round(meteDataItem.EndTime, 2)}", 0.1f, selectedAttribute)));

            output.AddRange(file.GetItemsOfType<SoundTrigger_v10>().Select(x => CreateBoneLocator(x, "Sound:" + x.SoundEvent, x.BoneIndex, x.Position, root, skeleton, rootPlayer, selectedAttribute)));
            output.AddRange(file.GetItemsOfType<SoundTrigger_v4>().Select(x => CreateBoneLocator(x, "Sound:" + x.SoundEvent, x.BoneIndex, x.Position, root, skeleton, rootPlayer, selectedAttribute)));

            output.AddRange(file.GetItemsOfType<CrewLocation_v10>().Select(x => CreateCrewLocation(x, x.Filter, x.Position, x.Orientation, root, selectedAttribute)));
            output.AddRange(file.GetItemsOfType<CrewLocation_v2>().Select(x => CreateCrewLocation(x, x.Filter, x.Position, x.Orientation, root, selectedAttribute)));

            if (includeEffects)
            {
                output.AddRange(file.GetItemsOfType<IEffectMeta>().Where(x => FilterOf(x) == 0 || FilterOf(x) == vfxFilter).Select(x => Keep((ParsedMetadataAttribute)x, selectedAttribute, root, skeleton, rootPlayer, () => CreateEffect(x, (ParsedMetadataAttribute)x, "Effect", root, skeleton, selectedAttribute, rootPlayer, persistent))));
                output.AddRange(file.GetItemsOfType<Blood_v11>().Select(x => Keep(x, selectedAttribute, root, skeleton, rootPlayer, () => CreateEffect(new BloodEffect(x), x, "Blood", root, skeleton, selectedAttribute, rootPlayer, persistent))));
            }

            // A prop docked to both hands from the same moment goes to the right hand, whichever tag comes first: in all
            // nine vanilla cases where the animation's own keys hold the prop rigid to one hand, it is the right one.
            var docks = file.GetItemsOfType<DockEquipment>();
            static bool Hand(DockEquipment x, string side) => x.AnimationSlotName == $"DOCK_EQUIPMENT_{side}_HAND";
            foreach (var meteDataItem in docks.Where(l => !Hand(l, "LEFT") || !docks.Any(r => Hand(r, "RIGHT") && r.PropBoneId == l.PropBoneId && r.StartTime == l.StartTime)))
                CreateEquipmentDock(meteDataItem, fragment, skeleton, rootPlayer);

            foreach (var meteDataItem in file.GetItemsOfType<Transform_v10>())
                CreateTransform(meteDataItem, rootPlayer);

            return output;
        }

        private void CreateTransform(Transform_v10 transform, AnimationPlayer rootPlayer)
        {
            var rule = new TransformBoneRule(transform);
            rootPlayer.AnimationRules.Add(rule);
        }

        private void CreateEquipmentDock(DockEquipment metaData, IAnimationBinGenericFormat fragment, ISkeletonProvider skeleton, AnimationPlayer rootPlayer)
        {
            if (fragment == null)
            {
                _logger.Here().Error($"Unable to create docking, as animation is missing - select an animation");
                return;
            }
 
            var animPath = fragment.Entries.FirstOrDefault(x => x.SlotName == metaData.AnimationSlotName)?.AnimationFile ??
                           fragment.Entries.FirstOrDefault(x => x.SlotName == metaData.AnimationSlotName + "_2")?.AnimationFile;
            if (animPath == null)
            {
                _logger.Here().Error($"Unable to create docking, as {metaData.AnimationSlotName} animation is missing");
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
                var boneNames = string.Join(", ", metaData.SkeletonNameAlternatives);
                _logger.Here().Error($"Unable to create docking, as {boneNames} bone is missing");
                return;
            }

            var pf = _packFileService.FindFile(animPath);
            if(pf == null)
                throw new Exception($"Unable to find animation for docking. Searched for {animPath} and failed to find it in the pack file service. This is required to visualise the docked equipment.");

            var animFile = AnimationFile.Create(pf);
            var clip = new AnimationClip(animFile, skeleton.Skeleton);

            var rule = new DockEquipmentRule(finalBoneIndex, metaData.PropBoneId, clip, skeleton, metaData.StartTime, metaData.EndTime);
            rootPlayer.AnimationRules.Add(rule);
        }

        private IMetaDataInstance CreateAnimatedProp(IAnimatedPropMeta animatedPropMeta, SceneNode root, ISkeletonProvider rootSkeleton, ParsedMetadataAttribute? selectedMetaDataAttribute, AnimationPlayer rootPlayer)
        {
            var propName = "Animated_prop";
            var color = selectedMetaDataAttribute == animatedPropMeta ? s_selectedColor : s_color;

            var meshPath = _packFileService.FindFile(animatedPropMeta.ModelName);
            if (meshPath == null)
                return null;
                //throw new Exception($"Unable to find model for animated prop. Searched for {animatedPropMeta.ModelName} and failed to find it in the pack file service. This is required to visualise the animated prop.");

            var animationPath = _packFileService.FindFile(animatedPropMeta.AnimationName);
            // Not registered with the animation container: the prop runs on the main animation's clock (AnimatedPropInstance).
            var propPlayer = new AnimationPlayer();

            // Configure the mesh
            var loadedNode = _complexMeshLoader.Load(meshPath, new GroupNode(propName), propPlayer, true, true); 

            // Configure animation
            var rootFollowsBone = false;
            Matrix? bindRoot = null;
            if (animationPath != null)
            {
                var animFile = AnimationFile.Create(animationPath);
                // A rigid model names no skeleton; its animation does.
                var skeletonName = SceneNodeHelper.GetSkeletonName(loadedNode) is { Length: > 0 } fromMesh ? fromMesh : animFile.Header.SkeletonName;
                var skeletonFile = _skeletonAnimationLookUpHelper.GetSkeletonFileFromName(skeletonName);
                if (skeletonFile == null)
                    throw new Exception($"Unable to find skeleton for animated prop. Searched for {skeletonName} and failed to find it in the skeleton animation look up helper. This is required to play the animation of the prop.");

                var skeleton = new GameSkeleton(skeletonFile, propPlayer);
                var clip = new AnimationClip(animFile, skeleton);
                propPlayer.SetAnimation(clip, skeleton);
                // The prop's first frame against the bone when the tag starts, which is when that frame plays.
                rootFollowsBone = CopyRootTransform.RootFollowsBone(skeleton, clip, BoneAt(animatedPropMeta.BoneId, rootSkeleton, rootPlayer)(animatedPropMeta.StartTime));
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

            // Add the animation rules; the tag's Scale is applied there, in the bones
            var animationRule = new CopyRootTransform(rootSkeleton, animatedPropMeta.BoneId, animatedPropMeta.Position, new Quaternion(animatedPropMeta.Orientation), animatedPropMeta.Scale, rootFollowsBone, bindRoot);
            propPlayer.AnimationRules.Add(animationRule);

            // Add to scene
            root.AddObject(loadedNode);

            var instance = new AnimatedPropInstance(loadedNode, propPlayer, animatedPropMeta.StartTime);
            instance.Update(rootPlayer.GetTimeUs() / 1_000_000f);
            return instance;
        }

        private IMetaDataInstance CreateStaticLocator(DecodedMetaEntryBase metaData, SceneNode root, Vector3 position, string displayName, ParsedMetadataAttribute? selectedMetaDataAttribute, float scale = 0.3f)
        {
            var color = selectedMetaDataAttribute == metaData ? s_selectedColor : s_color;

            var node = new SimpleDrawableNode(displayName);
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, displayName, position, color));
            node.AddItem(LineHelper.AddCircle(position, scale, color));
            root.AddObject(node);

            return new DrawableMetaInstance(metaData.StartTime, metaData.EndTime, node.Name, node);
        }

        // CREW_LOCATION: where an engine's crew member stands and which way he faces, in the model's space; the tag names no
        // bone. Its filter names the crew member: crew1 to crew3 and reserve1 to reserve3 on every vanilla tag that has one.
        // All 740 vanilla tags are 0/0, for the whole animation.
        private IMetaDataInstance CreateCrewLocation(ParsedMetadataAttribute tag, string filter, Vector3 position, Vector4 orientation, SceneNode root, ParsedMetadataAttribute? selectedAttribute)
        {
            var name = "Crew:" + (string.IsNullOrWhiteSpace(filter) ? "(no filter)" : filter);
            var node = new SimpleDrawableNode(name);
            AddAxes(node, position, Matrix.CreateFromQuaternion(new Quaternion(orientation)), name, selectedAttribute == tag ? s_selectedColor : s_color);
            root.AddObject(node);
            return new DrawableMetaInstance(0, 0, name, node);
        }

        // A named locator at a position relative to a bone, riding that bone as the animation plays, the way an EFFECT's
        // locator does. Like the other locators it is drawn for the whole animation, not only at the tag's time.
        private IMetaDataInstance CreateBoneLocator(ParsedMetadataAttribute tag, string name, int bone, Vector3 position, SceneNode root, ISkeletonProvider skeleton, AnimationPlayer rootPlayer, ParsedMetadataAttribute? selectedAttribute)
        {
            var color = selectedAttribute == tag ? s_selectedColor : s_color;
            var node = new SimpleDrawableNode(name);
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, name, position, color));
            node.AddItem(LineHelper.AddLocator(position, 0.2f, color));
            root.AddObject(node);
            var instance = new VfxEffectInstance(node, BoneAt(bone, skeleton, rootPlayer), 0);
            instance.Update(rootPlayer.GetTimeUs() / 1_000_000f);
            return instance;
        }

        // A locator's three axes turned by its rotation, X red, Y green and Z blue, with a label at its position.
        void AddAxes(SimpleDrawableNode node, Vector3 position, Matrix rotation, string label, Color color)
        {
            const float Length = 0.3f;
            const float TextOffset = Length * 1.5f + 0.01f;
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, label, position, color));
            foreach (var (axis, name, axisColor) in new[] { (Vector3.UnitX, "X", Color.Red), (Vector3.UnitY, "Y", Color.Green), (Vector3.UnitZ, "Z", Color.Blue) })
            {
                var local = Vector3.Transform(axis, rotation);
                node.AddItem(LineHelper.AddLine(position, position + local * Length, axisColor));
                node.AddItem(new WorldTextRenderItem(_resourceLibrary, name, position + local * TextOffset, axisColor));
            }
        }

        private IMetaDataInstance CreateSplashAttack(SplashAttack_v10 splashAttack, SceneNode root, string displayName, float scale, ParsedMetadataAttribute? selectedAttribute)
        {
            // A half-typed or brand-new tag (all zeros) gets its locators drawn and a log line, not an
            // exception: an exception here aborts Create() and wipes every other tag from the view.
            var distance = Vector3.Distance(splashAttack.StartPosition, splashAttack.EndPosition);
            var color = selectedAttribute == splashAttack ? s_selectedColor : s_color;

            var node = new SimpleDrawableNode(displayName);
            var textPos = (splashAttack.EndPosition + splashAttack.StartPosition) / 2;
            
            node.AddItem( new WorldTextRenderItem(_resourceLibrary, "StartPos", splashAttack.StartPosition, color));
            node.AddItem(LineHelper.AddLocator(splashAttack.StartPosition, scale, color));
            
            node.AddItem( new WorldTextRenderItem(_resourceLibrary, "EndPos", splashAttack.EndPosition, color));
            node.AddItem(LineHelper.AddLocator(splashAttack.EndPosition, scale, color));
            
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, displayName, textPos, color));
            node.AddItem(LineHelper.AddLine(splashAttack.StartPosition, splashAttack.EndPosition, color));
            root.AddObject(node);
            var instance = new DrawableMetaInstance(splashAttack.StartTime, splashAttack.EndTime, node.Name, node);

            if (MathUtil.CompareEqualFloats(distance))
            {
                _logger.Here().Warning($"{displayName}: StartPosition and EndPosition are the same point, so no area is drawn");
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
                    _logger.Here().Warning($"{displayName}: AngleForCone is 0, so no cone is drawn");
                    return instance;
                }
                var transformationM = rotationM * Matrix.CreateScale(distance) * Matrix.CreateTranslation(splashAttack.StartPosition);
                node.AddItem(LineHelper.AddConeSplash(splashAttack.StartPosition, splashAttack.EndPosition, transformationM, splashAttack.AngleForCone, color));
            }
            if (splashAttack.AoeShape == 1) // Corridor
            {
                if (MathUtil.CompareEqualFloats(splashAttack.WidthForCorridor, tolerance: 0.001f))
                {
                    _logger.Here().Warning($"{displayName}: WidthForCorridor is 0, so no corridor is drawn");
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
            public Vector3 Position { get; set; } = tag is Blood_v12 v ? v.Position : tag.Position;
            public Vector4 Orientation { get; set; } = tag is Blood_v12 v ? v.Orientation : tag.Orientation;
            public int NodeIndex { get; set; } = tag is Blood_v12 v ? v.NodeIndex : tag.NodeIndex;
            public float Scale { get; } = tag is Blood_v12 v ? v.Scale : tag.Scale;
            public float EffectStartTime { get; set; } = tag.StartTime;
            public float EffectEndTime { get; set; } = tag.EndTime;
        }

        private IMetaDataInstance CreateEffect(IEffectMeta effect, ParsedMetadataAttribute tag, string kind, SceneNode root, ISkeletonProvider skeleton, ParsedMetadataAttribute? selectedAttribute, AnimationPlayer rootPlayer, bool persistent)
        {
            var resolved = ShowVfx ? Resolve(effect) : null;
            if (resolved is { Found: false })
                LogOnce($"{tag.Name} {effect.VfxName}: {VfxResolver.PathOf(effect.VfxName)} is not in any loaded pack, so only its locator is drawn");
            foreach (var problem in resolved?.Problems ?? [])
                LogOnce($"{tag.Name} {effect.VfxName}: {problem}");

            var color = selectedAttribute == tag ? s_selectedColor : s_color;
            var rotMatrix = Matrix.CreateFromQuaternion(new Quaternion(effect.Orientation));

            var node = new SimpleDrawableNode($"{kind}:{effect.VfxName}");
            var label = resolved is { Found: false } ? effect.VfxName + " (not found)" : effect.VfxName;
            AddAxes(node, effect.Position, rotMatrix, label, color);
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
                var locatorOnly = new VfxEffectInstance(node, boneAt, startTime);
                locatorOnly.Update(rootPlayer.GetTimeUs() / 1_000_000f);
                return locatorOnly;
            }

            var settings = resolved.Emitters.Select(EmitterSettings.From).ToList();
            foreach (var emitter in settings)
            {
                foreach (var gap in emitter.Skipped)
                    LogOnce($"{tag.Name} {effect.VfxName}, emitter {emitter.Name}: {gap}");
            }

            var offset = Matrix.CreateScale(ScaleOf(effect)) * rotMatrix * Matrix.CreateTranslation(effect.Position);
            // A 0/0 tag (or one ending before it starts) spawns for the whole animation.
            var spawnEnd = effect.EffectEndTime > startTime ? effect.EffectEndTime - startTime : float.PositiveInfinity;
            // A persistent effect's clock runs past the clip's end; its bone is read where the looping clip is then.
            var loop = persistent ? rootPlayer.AnimationClip?.PlayTimeInSec ?? 0 : 0;
            float MainTime(float effectTime) => loop > 0 ? ((effectTime + startTime) % loop + loop) % loop : effectTime + startTime;
            var simulator = new ParticleSimulator(settings, StableSeed(effect), t => offset * boneAt(MainTime(t)), spawnEnd,
                message => LogOnce($"{tag.Name} {effect.VfxName}, {message}"));
            var particles = root.AddObject(new VfxEffectNode("Vfx:" + effect.VfxName,
                new ParticleRenderItem(simulator, _vfxTextures, () => _shaders.GetStaticEffect(ShaderTypes.Particle), _vfxMeshes)));

            var instance = new VfxEffectInstance(node, boneAt, startTime, particles, simulator, loop);
            instance.Update(rootPlayer.GetTimeUs() / 1_000_000f);
            return instance;
        }

        // The tag's bone at a time on the main clock, sampled from the main clip, so a re-simulation can ask
        // for any time. -1, or a bone the skeleton lacks, is the model's origin.
        // ponytail: sampled without the player's rules (hand poses, TRANSFORM); pass rootPlayer.AnimationRules
        // if an effect is ever placed on a bone a rule moves.
        Func<float, Matrix> BoneAt(int boneIndex, ISkeletonProvider skeleton, AnimationPlayer rootPlayer) => mainTime =>
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
            var keptAs = new KeptAs(string.Join("\u001f", values.Select(x => x.GetValue(tag))), tag == picked, ShowVfx, rootPlayer.AnimationClip, skeleton.Skeleton);
            if (_previous.TryGetValue(tag, out var kept) && kept.As == keptAs && root.Children.Contains(kept.Node))
            {
                _kept[tag] = kept;
                return kept.Instance;
            }

            // What a build adds to the scene starts with the node its instance cleans up.
            var before = root.Children.Count;
            var instance = build();
            if (instance != null && root.Children.Count > before)
                _kept[tag] = new Kept(keptAs, root.Children[before], instance);
            return instance;
        }

        VfxResolveResult Resolve(IEffectMeta effect)
        {
            var key = (effect.VfxName, StableSeed(effect));
            if (!_resolved.TryGetValue(key, out var resolved))
                _resolved[key] = resolved = new VfxResolver(_packFileService).Resolve(effect.VfxName, new Random(key.Item2));
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
