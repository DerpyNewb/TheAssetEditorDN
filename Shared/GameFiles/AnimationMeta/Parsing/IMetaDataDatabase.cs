using System.Reflection;

namespace Shared.GameFormats.AnimationMeta.Parsing
{
    public interface IMetaDataDatabase
    {
        string GetDescription(string metaDataTagName);
        string GetDescriptionSafe(string metaDataTagName);
        List<string> GetSupportedTypes();
        int GetWarhammer3UseCount(string tagAndVersion);

        List<Type> GetDefinition(string metadataName);
    }

    public class MetaDataDatabase : IMetaDataDatabase
    {
        private readonly ILogger _logger = Logging.Create<MetaDataDatabase>();

        private bool _isInitialized = false;
        private readonly Dictionary<string, List<Type>> _typeTable = [];
        private readonly Dictionary<string, string> _descriptionMap = [];

        public List<Type> GetDefinition(string metadataName)
        {
            EnsureMappingTableCreated();

            var result = _typeTable.TryGetValue(metadataName, out var typeTable);
            if (result == false || typeTable == null)
                throw new Exception($"No metadata tags for {metadataName}");
            return typeTable;
        }

        public void EnsureMappingTableCreated()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            CreateDescriptions();

            // Only consider assemblies that are part of this application's base directory to avoid scanning system/GAC assemblies
            var appBase = AppContext.BaseDirectory;
            var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            var wantedAssemplies = new List<Assembly>();
            foreach (var ass in allAssemblies)
            { 
                var name = ass.GetName().Name;
                if (name == null)
                    continue;

                if(name.Contains("AssetEditor", StringComparison.InvariantCultureIgnoreCase))
                    wantedAssemplies.Add(ass);

                if (name.Contains("Shared.GameFormats", StringComparison.InvariantCultureIgnoreCase))
                    wantedAssemplies.Add(ass);
            }

            _logger.Here().Information($"Scanning for metadata attributes in assemblies: {string.Join(", ", wantedAssemplies.Select(a => a.GetName().Name))}"); 

            var typesWithMyAttribute = wantedAssemplies
                .SelectMany(a =>
                {
                    try
                    {
                        return a.GetTypes();
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        return Array.Empty<Type>();
                    }
                })
                .Select(t => new
                {
                    Type = t,
                    Attributes = t.GetCustomAttributes(typeof(MetaDataAttribute), true).Cast<MetaDataAttribute>()
                })
                .Where(x => x.Attributes != null && x.Attributes.Any());

            var typesWithMyAttributeList = typesWithMyAttribute.Select(x => new { x.Type, AttributeInfo = x.Attributes.First() })
                .OrderBy(x => x.AttributeInfo.Priority)
                .ToList();

            _logger.Here().Information($"{typesWithMyAttributeList.Count} metadata attributes found");
            foreach (var instance in typesWithMyAttributeList)
            {
                var type = instance.Type;
                var key = instance.AttributeInfo.VersionName;
                if (_typeTable.ContainsKey(key) == false)
                    _typeTable.Add(key, new List<Type>());

                _typeTable[key].Add(type);

                var orderedPropertiesList = type.GetProperties()
                    .Where(x => x.CanWrite)
                    .Where(x => Attribute.IsDefined(x, typeof(MetaDataTagAttribute)))
                    .OrderBy(x => x.GetCustomAttributes<MetaDataTagAttribute>(false).Single().Order)
                    .Select(x => x.GetCustomAttributes<MetaDataTagAttribute>(false).Single());

                var allNumbers = orderedPropertiesList.Select(x => x.Order).ToArray();
                if (IsSequential(allNumbers) == false)
                    throw new Exception("Invalid ids");

                // Ensure we have a decription
                GetDescription(instance.AttributeInfo.Name);
            }

#if DEBUG
            // Check that we can create an instance
            foreach (var type in _typeTable.Keys)
            {
                var instance = CreateDefault(type);

                var t = instance.GetType();
                var props = t.GetProperties();

                foreach (var prop in props)
                {
                    if (prop.Name.ToLower() == "data")
                        continue;

                    var value = prop.GetValue(instance);
                    if (value == null)
                        throw new Exception($"{type} contains null value for attribute {prop.Name}");
                }
            }
#endif
        }


        ParsedMetadataAttribute CreateDefault(string itemName)
        {
            EnsureMappingTableCreated();

            if (_typeTable.ContainsKey(itemName) == false)
                throw new Exception("Unknown metadata item " + itemName);

            var type = _typeTable[itemName].First();
            var instance = Activator.CreateInstance(type) as ParsedMetadataAttribute;

            var itemNameSplit = itemName.ToUpper().Split("_");
            instance.Version = int.Parse(itemNameSplit.Last());
            instance.Name = string.Join("_", itemNameSplit.Take(itemNameSplit.Length - 1));
            return instance;
        }


        void CreateDescriptions()
        {
            _descriptionMap["TIME"] = "Generic time marker";
            _descriptionMap["DISABLE_PERSISTENT"] = "Disable persistent animation metadata";
            _descriptionMap["DISABLE_PERSISTENT_VFX"] = "Disable persistent vfx metadata";
            _descriptionMap["DISABLE_FACIAL"] = "Disable facial animations";
            _descriptionMap["DISABLE_HEAD_TRACKING"] = "Disable head tracking";
            _descriptionMap["DISABLE_MODEL"] = "Disable model draw.";
            _descriptionMap["SNIP"] = "Snip the starting and ending frame of an animation";
            _descriptionMap["FULL_BODY"] = "Indicates that this animation should be full body, that is, not torso spliced";
            _descriptionMap["NOT_BUILDING"] = "Indicates that this animation cannot be used for attacking buildings";
            _descriptionMap["USE_BASE_METADATA"] = "Force the use of metadata in the base anim slot during a hardcoded splice. All metadata in the hardcoded splice slot is ignored.";
            _descriptionMap["IGNORE_FOOT_SLIDING"] = "Mark the animation as not affected by foot sliding, so don't do anything to try to correct it.";
            _descriptionMap["WEAPON_ON"] = "Weapon bone is displayed between start and end times; set value to weapon bone index";
            _descriptionMap["WEAPON_LHAND"] = "Weapon bone is spliced relative to left hand between start and end times; set value to weapon bone index";
            _descriptionMap["WEAPON_RHAND"] = "Weapon bone is spliced relative to right hand between start and end times; set value to weapon bone index";
            _descriptionMap["WEAPON_HIP"] = "Weapon bone is spliced relative to hip between start and end times; set value to weapon bone index";
            _descriptionMap["DOCK_EQPT_LHAND"] = "Weapon bone is moved to the left hand between start and end times; set value to weapon bone index. Where it sits in the hand comes from the DOCK_EQUIPMENT_LEFT_HAND animation in the animation set.";
            _descriptionMap["DOCK_EQPT_RHAND"] = "Weapon bone is moved to the right hand between start and end times; set value to weapon bone index. Where it sits in the hand comes from the DOCK_EQUIPMENT_RIGHT_HAND animation in the animation set.";
            _descriptionMap["DOCK_EQPT_LHAND_2"] = "As DOCK_EQPT_LHAND, using the second left-hand position, the DOCK_EQUIPMENT_LEFT_HAND_2 animation.";
            _descriptionMap["DOCK_EQPT_RHAND_2"] = "As DOCK_EQPT_RHAND, using the second right-hand position, the DOCK_EQUIPMENT_RIGHT_HAND_2 animation.";
            _descriptionMap["DOCK_EQPT_LWAIST"] = "Weapon bone is moved to the left hip (sheathed) between start and end times; set value to weapon bone index. Where it sits comes from the DOCK_EQUIPMENT_LEFT_WAIST animation in the animation set.";
            _descriptionMap["DOCK_EQPT_RWAIST"] = "Weapon bone is moved to the right hip (sheathed) between start and end times; set value to weapon bone index. Where it sits comes from the DOCK_EQUIPMENT_RIGHT_WAIST animation in the animation set.";
            _descriptionMap["DOCK_EQPT_BACK"] = "Weapon bone is spliced relative to spine2 between start and end times; set value to weapon bone index";
            _descriptionMap["BLEND_OVERRIDE"] = "Override the blend method and blend time";
            _descriptionMap["DISABLE_PERSISTENT_ID"] = "Disable persistent metadata with a particular ID";
            _descriptionMap["MIN_TARGET_SIZE"] = "Can only use this animation against target larger or equal to this size";
            _descriptionMap["MAX_TARGET_SIZE"] = "Can only use this animation against target smaller or equal to this size";
            _descriptionMap["RIDER_CUSTOM_ANIMATION"] = "force the rider to play a custom animation";
            _descriptionMap["BEARING"] = "Generic bearing in degrees";
            _descriptionMap["DISTANCE"] = "Distance in cm; currently used for jump animations";
            _descriptionMap["IMPACT_SPEED"] = "On attack animations, speed of the strike; On death animations, speed change threshold to trigger animation";
            _descriptionMap["SC_RADIUS"] = "Change the soft collision radius (multiplier)";
            _descriptionMap["SC_HEIGHT"] = "Change the soft collision height (multiplier)";
            _descriptionMap["SC_RATIO"] = "Change the soft collision ratio";
            _descriptionMap["ALPHA"] = "Change the alpha value";
            _descriptionMap["VFX_ALPHA"] = "Change the alpha value of the VFX on this model (0 = invisible, 1 = fully visible). Same fields as ALPHA.";
            _descriptionMap["CAMERA_SHAKE_SCALE"] = "Set the camera shake scale";
            _descriptionMap["RIDER_IDLE_SPEED_SCALE"] = "scale the rider animation speed with some factor";
            _descriptionMap["RESCALE"] = "rescale character to target scale";
            _descriptionMap["ALLOWED_DELTA_SCALE"] = "maximum allowed delta scale to use the animation";
            _descriptionMap["PERSISTENT_SPEED_SCALE"] = "change the speed of the persistent metadata";
            _descriptionMap["BOUNDING_VOLUME_OVERRIDE"] = "A scale factor for the bounding sphere. This is a workaround for if the character animates far from the origin (or spreads out) and is being culled by the camera.";
            _descriptionMap["POSITION"] = "Generic position";
            _descriptionMap["FIRE_POS"] = "Position where projectile is created; start time is the time of projectile spawn";
            _descriptionMap["IMPACT_POS"] = "Position where impact originates; start time is the time of impact";
            _descriptionMap["IMPACT_DIRECTION_POS"] = "Same fields as IMPACT_POS; by name, the position the impact's direction is taken from. Not used by vanilla WH3.";
            _descriptionMap["TARGET_POS"] = "Indicates position of target";
            _descriptionMap["CAMERA_SHAKE_POS"] = "Start a camera shake at position";
            _descriptionMap["WOUNDED_POSE"] = "Specify the wounded pose in the last frame";
            _descriptionMap["LHAND_POSE"] = "Pose the left hand's fingers with the animation set's HAND_POSE_* slot that Hand Pose Id names (3 = GRIP). A key: it takes effect at Start Time and holds until this hand's next key; 0/0 means from the start.";
            _descriptionMap["RHAND_POSE"] = "Pose the right hand's fingers with the animation set's HAND_POSE_* slot that Hand Pose Id names (3 = GRIP). A key: it takes effect at Start Time and holds until this hand's next key; 0/0 means from the start.";
            _descriptionMap["FACE_POSE"] = "Face pose key";
            _descriptionMap["DISMEMBER"] = "Triggers dismembering at the start time";
            _descriptionMap["SPLICE"] = "Splices animation specified in metadata to this animation. Weights range from 0 to 1.";
            _descriptionMap["SPLICE_OVERRIDE"] = "Override hardcoded splice. Weights range from 0 to 1.";
            _descriptionMap["TRANSFORM"] = "Transform a node. Optionally override with another bone";
            _descriptionMap["CREW_LOCATION"] = "Position and face artillery crew";
            _descriptionMap["EFFECT"] = "Trigger a particle effect at a location relative to a node index";
            _descriptionMap["BLOOD"] = "Trigger a blood effect at a location relative to a node index";
            _descriptionMap["VOLUMETRIC_EFFECT"] = "Trigger a volumetric particle effect group. Only the VFX name is decoded; in vanilla WH3's one use the unnamed fields hold a position, two rotations and a scale of (1,1,1).";
            _descriptionMap["PROP"] = "Display a prop model at a location relative to a node index";
            _descriptionMap["ANIMATED_PROP"] = "Display an animated prop model at a location relative to a node index";
            _descriptionMap["RIDER_ATTACHMENT"] = "Mark an attachment point for a rider";
            _descriptionMap["TURRET_ATTACHMENT"] = "Mark an attachment point for a turret";
            _descriptionMap["SPLASH_ATTACK"] = "Trigger a splash attack and mark the area of effect: the volume from Start Position to End Position that the hit knocks enemies back through. Aoe Shape 0 is a cone/circle sized by Angle For Cone, 1 a corridor sized by Width For Corridor. This is the physical knock-back, separate from splash damage in the weapon tables.";
            _descriptionMap["SOUND_IMPACT"] = "Old-format (v2) impact sound: a time plus two values, by name the attack and defend types. Not used by vanilla WH3.";
            _descriptionMap["SOUND_ATTACK_TYPE"] = "Attack type of this combat animation.";
            _descriptionMap["SOUND_DEFEND_TYPE"] = "Defend type of this combat animation.";
            _descriptionMap["SOUND_SPHERE_LINK"] = "Link this entity with the spheres of matched combatants. Vanilla WH3 uses it in matched-combat death .snd.meta files.";
            _descriptionMap["SOUND_BUILDING"] = "Time, event and position of sound to be triggered";
            _descriptionMap["SOUND_TRIGGER"] = "Play a sound at the start time. Sound Event is the audio event name; the sound comes from Position, relative to Bone Index.";
            _descriptionMap["SOUND_SPHERE"] = "Time and type of sound sphere to be triggered";
            // Each name below is set once. A second assignment further down silently replaces the first.
            _descriptionMap["SOUND_POSITION"] = "Place a named sound emitter at a position and rotation on the model. Vanilla WH3 names them Loop_1, Loop_2 and Engine_1 to Engine_3.";
            _descriptionMap["SYNC_MARKER"] = "A time marker with no data of its own. Vanilla WH3 puts it on two run/charge cycles, so probably a locomotion sync point.";
            _descriptionMap["PARENT_CONSTRAINT"] = "By name and fields: holds the model to a parent bone with a position and rotation offset between start and end times. The two leading values and the trailing two are not decoded.";
            _descriptionMap["CANNOT_DISMEMBER"] = "mark an attack as not causing dismemberment";
            _descriptionMap["ALLOW_LEG_DISMEMBER"] = "Let this attack dismember legs. Not used by vanilla WH3.";
            _descriptionMap["ALLOW_FRONT_LEG_DISMEMBER"] = "Let this attack dismember front legs. Not used by vanilla WH3.";
            _descriptionMap["CAMPAIGN_DISMEMBER"] = "Old-format (v2) dismember tag naming an animation. Not used by vanilla WH3.";
            _descriptionMap["FACEFX"] = "Old-format (v2) FaceFX facial animation reference. Not used by vanilla WH3.";
            _descriptionMap["PULL_ROPE"] = "Old-format (v2) marker with no data of its own, by name for rope-pulling animations. Not used by vanilla WH3.";
            _descriptionMap["CROP_MARKER"] = "A time marker with no data of its own; purpose not confirmed. Not used by vanilla WH3.";
            _descriptionMap["NOT_HERO_TARGET"] = "By name: this animation is not picked against hero targets. Not used by vanilla WH3.";
            _descriptionMap["DIE_PERMANENTLY"] = "By name: the death this animation plays is final. Not used by vanilla WH3.";
            _descriptionMap["DISABLE_ENEMY_COLLISION"] = "By name: enemies do not collide with this model while the tag is active. Not used by vanilla WH3.";
            _descriptionMap["MARK_RIGHT_FOOT_FRONT"] = "By name: marks the time the right foot is forward, for matching locomotion cycles. Not used by vanilla WH3.";
            _descriptionMap["NO CHAINING"] = "By name: this animation is not chained into the next one. Not used by vanilla WH3, and the space in the name is unverified.";
            _descriptionMap["NOT_REGULAR_TARGET"] = "By name: this animation is not picked against regular (non-hero) targets. Not used by vanilla WH3.";
            _descriptionMap["START_CLIMB"] = "By name: marks where a climb starts. Not used by vanilla WH3.";
            _descriptionMap["MATERIAL_FLAG"] = "Purpose not confirmed and fields not decoded. Not used by vanilla WH3.";
            _descriptionMap["FREEZE_WEAPON"] = "By name: holds a weapon still. Fields not decoded. Not used by vanilla WH3.";
            _descriptionMap["RIDER_ANIMATION_REQUIRED"] = "Mark the mount animation as needing a synced rider animation is corresponding rider slot.";
            _descriptionMap["SHADER_PARAMETER"] = "Modify specified shader parameter. Blends between two closest values.";
            _descriptionMap["EJECT_ATTACHED"] = "Eject the attached riders. Defines the direction of ejection(2D, Y is ignored).";
        }

        public string GetDescription(string metaDataTagName)
        {
            EnsureMappingTableCreated();

            if (_descriptionMap.ContainsKey(metaDataTagName) == false)
                throw new Exception($"Unable to get description of {metaDataTagName}");
            return _descriptionMap[metaDataTagName];
        }

        public string GetDescriptionSafe(string metaDataTagName)
        {
            EnsureMappingTableCreated();

            if (_descriptionMap.ContainsKey(metaDataTagName) == false)
                return "Missing";
            return _descriptionMap[metaDataTagName];
        }

        public List<string> GetSupportedTypes()
        {
            EnsureMappingTableCreated();

            return _typeTable.Select(x => x.Key).ToList();
        }

        // Uses of each tag+version across vanilla WH3's anim.pack, anim2.pack and anim_3.pack, counted
        // 2026-10-11 by Test.AnimationMeta's VanillaMetaSurvey. A version missing here is used by no
        // vanilla WH3 animation; most of those are formats from older games.
        static readonly Dictionary<string, int> s_warhammer3UseCount = new()
        {
            ["ALLOWED_DELTA_SCALE_10"] = 11, ["ALPHA_10"] = 1400, ["ANIMATED_PROP_10"] = 10, ["ANIMATED_PROP_11"] = 23,
            ["ANIMATED_PROP_13"] = 1, ["ANIMATED_PROP_14"] = 2074, ["ANIMATED_PROP_15"] = 1948, ["BEARING_10"] = 38,
            ["BLEND_OVERRIDE_11"] = 81, ["BLOOD_11"] = 1689, ["BLOOD_12"] = 470, ["BOUNDING_VOLUME_OVERRIDE_10"] = 2,
            ["CAMERA_SHAKE_POS_10"] = 1094, ["CAMERA_SHAKE_SCALE_10"] = 833, ["CANNOT_DISMEMBER_10"] = 12, ["CREW_LOCATION_10"] = 734,
            ["CREW_LOCATION_2"] = 6, ["DISABLE_FACIAL_10"] = 1, ["DISABLE_HEAD_TRACKING_10"] = 567, ["DISABLE_MODEL_10"] = 207,
            ["DISABLE_PERSISTENT_10"] = 6896, ["DISABLE_PERSISTENT_2"] = 5, ["DISABLE_PERSISTENT_ID_10"] = 3, ["DISABLE_PERSISTENT_ID_11"] = 2573,
            ["DISABLE_PERSISTENT_VFX_10"] = 528, ["DISMEMBER_10"] = 129, ["DISTANCE_10"] = 38, ["DOCK_EQPT_BACK_10"] = 91,
            ["DOCK_EQPT_BACK_11"] = 2505, ["DOCK_EQPT_LHAND_10"] = 374, ["DOCK_EQPT_LHAND_11"] = 6683, ["DOCK_EQPT_LHAND_2_11"] = 128,
            ["DOCK_EQPT_LHAND_3"] = 5, ["DOCK_EQPT_LWAIST_10"] = 52, ["DOCK_EQPT_LWAIST_11"] = 1258, ["DOCK_EQPT_LWAIST_3"] = 2,
            ["DOCK_EQPT_RHAND_10"] = 407, ["DOCK_EQPT_RHAND_11"] = 8090, ["DOCK_EQPT_RHAND_2_11"] = 171, ["DOCK_EQPT_RHAND_3"] = 5,
            ["DOCK_EQPT_RWAIST_10"] = 9, ["DOCK_EQPT_RWAIST_11"] = 234, ["EFFECT_11"] = 1649, ["EFFECT_12"] = 19009,
            ["EJECT_ATTACHED_10"] = 16, ["FACE_POSE_10"] = 16814, ["FIRE_POS_10"] = 2785, ["FULL_BODY_10"] = 383,
            ["IGNORE_FOOT_SLIDING_10"] = 9, ["IMPACT_POS_10"] = 13923, ["IMPACT_POS_2"] = 15, ["IMPACT_SPEED_10"] = 354,
            ["LHAND_POSE_10"] = 11360, ["LHAND_POSE_2"] = 14, ["MAX_TARGET_SIZE_11"] = 78, ["MIN_TARGET_SIZE_11"] = 26,
            ["NOT_BUILDING_10"] = 13, ["PARENT_CONSTRAINT_10"] = 53, ["POSITION_10"] = 8, ["PROP_10"] = 41,
            ["PROP_11"] = 2, ["PROP_13"] = 11, ["PROP_14"] = 361, ["PROP_15"] = 303,
            ["PROP_4"] = 9, ["RESCALE_10"] = 970, ["RHAND_POSE_10"] = 11368, ["RHAND_POSE_2"] = 12,
            ["RIDER_ANIMATION_REQUIRED_10"] = 1, ["RIDER_CUSTOM_ANIMATION_10"] = 3, ["RIDER_IDLE_SPEED_SCALE_10"] = 34, ["SC_HEIGHT_10"] = 119,
            ["SC_RADIUS_10"] = 107, ["SC_RATIO_10"] = 101, ["SHADER_PARAMETER_11"] = 2, ["SHADER_PARAMETER_12"] = 1194,
            ["SNIP_10"] = 65, ["SOUND_DEFEND_TYPE_10"] = 288, ["SOUND_DEFEND_TYPE_11"] = 60, ["SOUND_POSITION_10"] = 46,
            ["SOUND_SPHERE_10"] = 32, ["SOUND_SPHERE_LINK_10"] = 2, ["SOUND_TRIGGER_10"] = 12181, ["SOUND_TRIGGER_11"] = 261641,
            ["SOUND_TRIGGER_4"] = 158, ["SPLASH_ATTACK_10"] = 6101, ["SPLASH_ATTACK_3"] = 4, ["SPLICE_10"] = 12,
            ["SPLICE_11"] = 199, ["SPLICE_12"] = 10090, ["SPLICE_5"] = 3, ["SPLICE_6"] = 3,
            ["SPLICE_OVERRIDE_12"] = 299, ["SYNC_MARKER_10"] = 2, ["TARGET_POS_10"] = 215, ["TIME_10"] = 26,
            ["TRANSFORM_10"] = 5799, ["TURRET_ATTACHMENT_14"] = 1, ["USE_BASE_METADATA_10"] = 3, ["VFX_ALPHA_10"] = 10,
            ["VOLUMETRIC_EFFECT_10"] = 1, ["WEAPON_HIP_11"] = 13, ["WEAPON_HIP_3"] = 1, ["WEAPON_LHAND_10"] = 1,
            ["WEAPON_LHAND_11"] = 150, ["WEAPON_ON_10"] = 64, ["WEAPON_ON_11"] = 913, ["WEAPON_RHAND_10"] = 5,
            ["WEAPON_RHAND_11"] = 684, ["WEAPON_RHAND_3"] = 1, ["WOUNDED_POSE_10"] = 5,
        };

        public int GetWarhammer3UseCount(string tagAndVersion) => s_warhammer3UseCount.GetValueOrDefault(tagAndVersion);

        static bool IsSequential(int[] array)
        {
            return array.Zip(array.Skip(1), (a, b) => a + 1 == b).All(x => x);
        }
    }
}
