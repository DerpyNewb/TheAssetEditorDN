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

        private static Color s_color = Color.Black;
        private static Color s_selectedColor = Color.Red;

        public bool ShowVfx { get; set; } = true;

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
            _vfxMeshes = new VfxMeshCache(LoadRigid);
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
            // Clear all
            var output = new List<IMetaDataInstance>();

            // Apply persistent meta data, if no disable is given.
            var usePersistent = metaData == null || metaData.GetItemsOfType<DisablePersistant_v10>().Count == 0;
            // DISABLE_PERSISTENT_VFX drops the persistent meta's effects and keeps the rest of it.
            var usePersistentVfx = metaData == null || metaData.GetItemsOfType<DisablePersistantVfx_v10>().Count == 0;
            if (usePersistent)
            {
                var metaDataPersistent = ApplyMetaData(persistent, selectedMetaDataAttribute, root, skeleton, rootPlayer, fragment, usePersistentVfx, persistent: true);
                output.AddRange(metaDataPersistent);
            }

            var metaDataInstances = ApplyMetaData(metaData, selectedMetaDataAttribute,root, skeleton, rootPlayer, fragment);
            output.AddRange(metaDataInstances);

            // Hand poses are keys across both files, so they are built from both at once.
            CreateHandPoses(usePersistent ? persistent : null, metaData, fragment, skeleton, rootPlayer);
            return output;
        }

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
            if (handPoseId < 0 || handPoseId >= s_handPoseSlots.Length)
                return null;

            var slot = s_handPoseSlots[handPoseId];
            var animPath = fragment?.Entries.FirstOrDefault(x => x.SlotName == slot)?.AnimationFile;
            var file = string.IsNullOrWhiteSpace(animPath) ? null : _packFileService.FindFile(animPath);
            if (file == null)
            {
                _logger.Here().Warning($"Hand pose {slot} is not in the animation set, so the fingers keep the animation's pose");
                return null;
            }

            return AnimationSampler.Sample(0, 0, skeleton, new AnimationClip(AnimationFile.Create(file), skeleton));
        }

        private IEnumerable<IMetaDataInstance> ApplyMetaData(ParsedMetadataFile? file, ParsedMetadataAttribute? selectedAttribute, SceneNode root, ISkeletonProvider skeleton, AnimationPlayer rootPlayer, IAnimationBinGenericFormat fragment, bool includeEffects = true, bool persistent = false)
        {
            var output = new List<IMetaDataInstance>();
            if (file == null)
                return output;

            output.AddRange(file.GetItemsOfType<IAnimatedPropMeta>().Select(x => CreateAnimatedProp(x, root, skeleton, selectedAttribute, rootPlayer)).Where(x => x != null));

            output.AddRange(file.GetItemsOfType<ImpactPosition_v10>().Select(meteDataItem => CreateStaticLocator(meteDataItem, root, meteDataItem.Position, "ImpactPos", selectedAttribute)));

            output.AddRange(file.GetItemsOfType<TargetPos_10>().Select(meteDataItem => CreateStaticLocator(meteDataItem, root, meteDataItem.Position, "TargetPos", selectedAttribute)));

            output.AddRange(file.GetItemsOfType<FirePos_v10>().Select(meteDataItem => CreateStaticLocator(meteDataItem, root, meteDataItem.Position, "FirePos", selectedAttribute)));

            output.AddRange(file.GetItemsOfType<SplashAttack_v10>().Select(meteDataItem => CreateSplashAttack(meteDataItem, root, $"SplashAttack_{Math.Round(meteDataItem.EndTime, 2)}", 0.1f, selectedAttribute)));

            if (includeEffects)
                output.AddRange(file.GetItemsOfType<IEffectMeta>().Select(x => CreateEffect(x, root, skeleton, selectedAttribute, rootPlayer, persistent)));

            foreach (var meteDataItem in file.GetItemsOfType<DockEquipment>())
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
            if (animationPath != null)
            {
                var skeletonName = SceneNodeHelper.GetSkeletonName(loadedNode);
                var skeletonFile = _skeletonAnimationLookUpHelper.GetSkeletonFileFromName(skeletonName);
                if (skeletonFile == null)
                    throw new Exception($"Unable to find skeleton for animated prop. Searched for {skeletonName} and failed to find it in the skeleton animation look up helper. This is required to play the animation of the prop.");

                var skeleton = new GameSkeleton(skeletonFile, propPlayer);
                var animFile = AnimationFile.Create(animationPath);
                var clip = new AnimationClip(animFile, skeleton);
                propPlayer.SetAnimation(clip, skeleton);

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
            var animationRule = new CopyRootTransform(rootSkeleton, animatedPropMeta.BoneId, animatedPropMeta.Position, new Quaternion(animatedPropMeta.Orientation), animatedPropMeta.Scale);
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

        private IMetaDataInstance CreateEffect(IEffectMeta effect, SceneNode root, ISkeletonProvider skeleton, ParsedMetadataAttribute? selectedAttribute, AnimationPlayer rootPlayer, bool persistent)
        {
            var resolved = ShowVfx ? new VfxResolver(_packFileService).Resolve(effect.VfxName, new Random(StableSeed(effect))) : null;
            if (resolved is { Found: false })
                LogOnce($"EFFECT {effect.VfxName}: {VfxResolver.PathOf(effect.VfxName)} is not in any loaded pack, so only its locator is drawn");
            foreach (var problem in resolved?.Problems ?? [])
                LogOnce($"EFFECT {effect.VfxName}: {problem}");

            var color = selectedAttribute == effect ? s_selectedColor : s_color;
            var locatorScale = 0.3f;
            var textOffset = locatorScale * 1.5f + 0.01f;

            var rotationQuat = new Quaternion(effect.Orientation);
            var rotMatrix = Matrix.CreateFromQuaternion(rotationQuat);

            var localX = Vector3.Transform(Vector3.UnitX, rotMatrix);
            var localY = Vector3.Transform(Vector3.UnitY, rotMatrix);
            var localZ = Vector3.Transform(Vector3.UnitZ, rotMatrix);

            var node = new SimpleDrawableNode("Effect:" + effect.VfxName);
            node.AddItem(LineHelper.AddLine(effect.Position, effect.Position + localX * locatorScale, Color.Red));
            node.AddItem(LineHelper.AddLine(effect.Position, effect.Position + localY * locatorScale, Color.Green));
            node.AddItem(LineHelper.AddLine(effect.Position, effect.Position + localZ * locatorScale, Color.Blue));

            var label = resolved is { Found: false } ? effect.VfxName + " (not found)" : effect.VfxName;
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, label, effect.Position, color));
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, "X", effect.Position + localX * textOffset, Color.Red));
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, "Y", effect.Position + localY * textOffset, Color.Green));
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, "Z", effect.Position + localZ * textOffset, Color.Blue));

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
                    LogOnce($"EFFECT {effect.VfxName}, emitter {emitter.Name}: {gap}");
            }

            var offset = Matrix.CreateScale(ScaleOf(effect)) * rotMatrix * Matrix.CreateTranslation(effect.Position);
            // A 0/0 tag (or one ending before it starts) spawns for the whole animation.
            var spawnEnd = effect.EffectEndTime > startTime ? effect.EffectEndTime - startTime : float.PositiveInfinity;
            // A persistent effect's clock runs past the clip's end; its bone is read where the looping clip is then.
            var loop = persistent ? rootPlayer.AnimationClip?.PlayTimeInSec ?? 0 : 0;
            float MainTime(float effectTime) => loop > 0 ? ((effectTime + startTime) % loop + loop) % loop : effectTime + startTime;
            var simulator = new ParticleSimulator(settings, StableSeed(effect), t => offset * boneAt(MainTime(t)), spawnEnd,
                message => LogOnce($"EFFECT {effect.VfxName}, {message}"));
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
        static Func<float, Matrix> BoneAt(int boneIndex, ISkeletonProvider skeleton, AnimationPlayer rootPlayer) => mainTime =>
        {
            var gameSkeleton = skeleton.Skeleton;
            if (gameSkeleton == null || boneIndex < 0 || boneIndex >= gameSkeleton.BoneCount)
                return Matrix.Identity;
            var clip = rootPlayer.AnimationClip;
            if (clip == null || clip.PlayTimeInSec <= 0)
                return gameSkeleton.GetWorldTransform(boneIndex);
            var frame = AnimationSampler.Sample(Math.Clamp(mainTime / clip.PlayTimeInSec, 0, 1), gameSkeleton, clip);
            return frame.GetSkeletonAnimatedWorld(gameSkeleton, boneIndex);
        };

        // Scale is on EFFECT v3, v5, v7, v11 and v12, not on IEffectMeta; the other versions draw at 1.
        static float ScaleOf(IEffectMeta effect) => effect switch
        {
            Effect_v3 e => e.Scale,
            Effect_v7 e => e.Scale,
            Effect_v11 e => e.Scale,
            Effect_v12 e => e.Scale,
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
