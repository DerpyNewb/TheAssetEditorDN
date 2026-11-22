using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation
{
    /// <summary>Which tags SuperView draws, for the mark beside each tag in the meta editor's list and in the New tag dialog.
    /// This is the one list of them: ShownInSuperViewTests builds every tag version through <see cref="MetaDataBuilder"/> and
    /// fails when the builder draws a tag this list does not name, or this list names one it does not draw.</summary>
    public static class ShownInSuperView
    {
        public enum Drawn { No, Partly, Yes }

        public sealed record Mark(Drawn Drawn, string Tooltip);

        const string NotDrawn = "SuperView doesn't draw this tag, so changing it moves nothing in the viewport.";
        const string OlderSplice = "SuperView draws this the way it draws SPLICE_11, with no blend out and, for version 5, no blend in: the generic bone to its depth, the neck, torso, arms and legs that the skeleton's battle_skeletons row names, and with Weapon Pose the weapon bones. These are readings of the fields, not checked in game. SuperView ignores Stretch and Exclude Stripped.";
        const string Locator = "SuperView draws a locator at this tag's position. It turns yellow when the tag fires.";

        // Keyed by the types MetaDataBuilder asks the metas for, so a tag version, or a tag that inherits a drawn one
        // (IMPACT_DIRECTION_POS is an IMPACT_POS), is matched the way the builder matches it. The first entry a tag's type
        // is assignable to wins, so a type the builder skips sits above the type it inherits from.
        public static readonly IReadOnlyList<(Type Tag, Drawn Drawn, string Tooltip)> Tags =
        [
            (typeof(IAnimatedPropMeta), Drawn.Yes, "SuperView draws this prop's model on its bone between its Start and End Time, as the game does. An animated prop also plays its own animation. While the animation set shows the weapon that Override Prop names, the game doesn't draw the prop at all, and neither does SuperView."),
            (typeof(IEffectMeta), Drawn.Yes, "SuperView draws this effect's locator and plays its particles. Show VFX and the VFX filter decide which effects play."),
            (typeof(Blood_v11), Drawn.Yes, "SuperView draws this tag the way it draws an EFFECT: its locator and its particles."),
            (typeof(ImpactPosition_v10), Drawn.Yes, Locator),
            (typeof(TargetPos_10), Drawn.Yes, Locator),
            (typeof(FirePos_v10), Drawn.Yes, Locator),
            (typeof(CameraShakePos), Drawn.Yes, Locator),
            (typeof(SplashAttack_v10), Drawn.Yes, "SuperView draws the attack's area between its start and end positions. It turns yellow while the attack is on."),
            (typeof(SoundTrigger_v10), Drawn.Yes, "SuperView draws a locator on this tag's bone. It turns yellow when the sound fires, and with Sound on, you hear it."),
            (typeof(SoundTrigger_v4), Drawn.Yes, "SuperView draws a locator on this tag's bone. It turns yellow when the sound fires, and with Sound on, you hear it."),
            (typeof(Transform_v10), Drawn.Partly, "SuperView moves and turns this tag's target bone from its start to its end time, fading in and out over its blend times, and on top of a dock. A tag at 0 to 0 with no blends holds for the whole clip. SuperView ignores the tag's source bone."),
            (typeof(DisableModel), Drawn.Yes, "SuperView hides the model while this tag is on."),
            (typeof(ReScale), Drawn.Yes, "SuperView draws the model at this tag's scale while the tag is on."),
            (typeof(VfxAlpha_v10), Drawn.Partly, "SuperView fades every effect on the model by these keys, in a straight line from one key to the next. That reading isn't checked in game. A rigid model inside an effect is drawn whole at any alpha above 0."),
            (typeof(Alpha_v10), Drawn.Partly, "SuperView hides the model where this key leaves it invisible. It can't draw a fade, so the model stays fully opaque at any alpha above 0."),
            (typeof(CrewLocation_v10), Drawn.Partly, "SuperView draws where this crew member stands and which way it faces. It ignores the tag's times and draws it for the whole clip; every vanilla tag is 0 to 0."),
            (typeof(CrewLocation_v2), Drawn.Partly, "SuperView draws where this crew member stands and which way it faces. It ignores the tag's times and draws it for the whole clip; every vanilla tag is 0 to 0."),
            (typeof(SpliceOverride_v11), Drawn.No, NotDrawn),
            (typeof(SpliceOverride_v12), Drawn.No, NotDrawn),
            (typeof(Splice_v11), Drawn.Partly, "SuperView drives the generic bone to its depth, the neck, torso, arms and legs that the skeleton's battle_skeletons row names, and with Weapon Pose the weapon bones. An additive splice adds its difference from the reference animation's first frame, or from the bind pose. These are readings of the fields, not checked in game. SuperView ignores Stretch, Exclude Stripped and Primary Persistent."),
            (typeof(Splice_v10), Drawn.Partly, OlderSplice),
            (typeof(Splice_v6), Drawn.Partly, OlderSplice),
            (typeof(Splice_v5), Drawn.Partly, OlderSplice),
            (typeof(IHandPose), Drawn.Partly, "SuperView poses the fingers with the animation set's HAND_POSE_* slot that this tag names, so a set without that slot leaves the fingers as the animation has them. The pose switches at once at the tag's start time, where the game may blend. Versions 0 and 1 are not drawn."),
            (typeof(FacePose), Drawn.Partly, "SuperView poses the face with the animation set's FACE_POSE_* slot that this tag names. It needs at least two of those slots to tell the face's bones from the body's, so a set with fewer leaves the face as the animation has it."),
            // WEAPON_* tags are dock tags with no dock slot of their own; the builder tries them and logs that the slot is missing.
            (typeof(WeaponHip_v10), Drawn.No, NotDrawn),
            (typeof(WeaponRHand_v10), Drawn.No, NotDrawn),
            (typeof(WeaponLHand_v10), Drawn.No, NotDrawn),
            (typeof(WeaponOn_v10), Drawn.No, NotDrawn),
            (typeof(DockEquipment), Drawn.Partly, "SuperView moves the weapon with the animation set's DOCK_EQUIPMENT_* animation for this hand, waist or back, so it shows only when an animation set with that slot is picked."),
            (typeof(DisablePersistant_v10), Drawn.Partly, "SuperView stops drawing the persistent meta's tags while this tag is in the animation's own meta. It ignores the tag's times."),
            (typeof(DisablePersistantVfx_v10), Drawn.Partly, "SuperView stops drawing the persistent meta's effects while this tag is in the animation's own meta. It ignores the tag's times."),
        ];

        public static Mark Of(Type tag)
        {
            foreach (var (type, drawn, tooltip) in Tags)
            {
                if (type.IsAssignableFrom(tag))
                    return new Mark(drawn, tooltip);
            }
            return new Mark(Drawn.No, NotDrawn);
        }
    }
}
