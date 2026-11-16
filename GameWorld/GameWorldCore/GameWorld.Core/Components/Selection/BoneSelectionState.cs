using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.Animation;
using GameWorld.Core.Animation.Posing;
using GameWorld.Core.SceneNodes;

namespace GameWorld.Core.Components.Selection
{
    public delegate void BoneModifiedEvent(BoneSelectionState state);

    public class BoneSelectionState : ISelectionState
    {
        public GeometrySelectionMode Mode => GeometrySelectionMode.Bone;
        public event SelectionStateChanged SelectionChanged;
        public AnimationClip CurrentAnimation { get; set; }
        public GameSkeleton Skeleton { get; set; }
        public ISelectable RenderObject { get; set; }
        public List<int> SelectedBones { get; set; } = new List<int>();
        public bool EnableInverseKinematics { get; set; }
        public int InverseKinematicsEndBoneIndex { get; set; }
        /// <summary>The limits an IK drag is clamped to; null leaves IK unlimited.</summary>
        public JointLimits? JointLimits { get; set; }
        /// <summary>Bones held in place while others are edited; re-solved after every edit to the current frame.</summary>
        public List<Pin> Pins { get; set; } = new();
        public int CurrentFrame { get; set; }
        public event BoneModifiedEvent BoneModifiedEvent;
        public List<int> ModifiedBones { get; set; } = new List<int>();

        public BoneSelectionState(ISelectable renderObj)
        {
            RenderObject = renderObj;
        }

        public void ModifySelection(IEnumerable<int> newSelectionItems, bool onlyRemove)
        {
            if (onlyRemove)
            {
                foreach (var newSelectionItem in newSelectionItems)
                {
                    if (SelectedBones.Contains(newSelectionItem))
                        SelectedBones.Remove(newSelectionItem);
                }
            }
            else
            {
                foreach (var newSelectionItem in newSelectionItems)
                {
                    if (!SelectedBones.Contains(newSelectionItem))
                        SelectedBones.Add(newSelectionItem);
                }
            }
            SelectionChanged?.Invoke(this, true);
        }


        public List<int> CurrentSelection()
        {
            return SelectedBones;
        }

        public void Clear()
        {
            SelectedBones.Clear();
            SelectionChanged?.Invoke(this, true);
        }


        public void EnsureSorted()
        {
            SelectedBones = SelectedBones.Distinct().OrderBy(x => x).ToList();
        }

        public void DeselectAnimRootNode()
        {
            SelectedBones.RemoveAll(bone => bone == 0);
        }

        public ISelectionState Clone()
        {
            return new BoneSelectionState(RenderObject)
            {
                SelectedBones = new List<int>(SelectedBones),
                Skeleton = Skeleton,
                CurrentAnimation = CurrentAnimation,
                SelectionChanged = SelectionChanged,
                CurrentFrame = CurrentFrame,
                RenderObject = RenderObject,
                EnableInverseKinematics = EnableInverseKinematics,
                InverseKinematicsEndBoneIndex = InverseKinematicsEndBoneIndex,
                JointLimits = JointLimits,
                Pins = Pins,
            };
        }

        public int SelectionCount()
        {
            return SelectedBones.Count();
        }

        public ISelectable GetSingleSelectedObject()
        {
            return RenderObject;
        }

        public List<ISelectable> SelectedObjects()
        {
            return new List<ISelectable>() { RenderObject };
        }

        public void TriggerModifiedBoneEvent(List<int> modifiedBones)
        {
            ModifiedBones = modifiedBones;
            BoneModifiedEvent.Invoke(this);
        }
    }
}

