using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes
{
    public partial class SkeletonBoneNode : ObservableObject
    {
        [ObservableProperty] string _boneName;
        [ObservableProperty] int _boneIndex;
        [ObservableProperty] int _parentBoneIndex;
        [ObservableProperty] ObservableCollection<SkeletonBoneNode> _children = [];
        // Bound to the tree's item, which is how a bone selected from code shows as selected there.
        [ObservableProperty] bool _isSelected;

        public SkeletonBoneNode(int boneId, int parentBoneId, string boneName)
        {
            BoneIndex = boneId;
            BoneName = boneName;
            ParentBoneIndex = parentBoneId;
        }   
    }

}
