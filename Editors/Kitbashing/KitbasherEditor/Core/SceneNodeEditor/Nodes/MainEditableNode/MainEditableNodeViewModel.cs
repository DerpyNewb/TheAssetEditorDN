using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.UiCommands;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using KitbasherEditor.Views.EditorViews;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel.Types;
using Shared.Ui.Common.DataTemplates;
using static CommonControls.FilterDialog.FilterUserControl;

namespace Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes
{

    public partial class AttachmentPoint : ObservableObject
    {
        [ObservableProperty] public partial int BoneIndex { get; set; }
        [ObservableProperty] public partial string Name { get; set; }
        [ObservableProperty] public partial bool IsIdentiy { get; set; }
    }

    public partial class MainEditableNodeViewModel : ObservableObject, ISceneNodeEditor, IViewProvider<MainEditableNodeView>
    {
        static public OnSeachDelegate FilterByFullPath { get { return (item, expression) => { return expression.Match(item.ToString()).Success; }; } }

        private readonly KitbasherRootScene _kitbasherRootScene;
        private readonly RenderEngineComponent _renderEngineComponent;
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IStandardDialogs _standardDialogs;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;

        MainEditableNode? _mainNode;

        [ObservableProperty] public partial ObservableCollection<string> SkeletonNameList { get; set; }
        [ObservableProperty] public partial ObservableCollection<AttachmentPoint> AttachmentPointList { get; set; } = [];
        [ObservableProperty] public partial string? SkeletonName { get; set; }

        public MainEditableNodeViewModel(KitbasherRootScene kitbasherRootScene,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper,
            RenderEngineComponent renderEngineComponent, 
            IUiCommandFactory uiCommandFactory,
            IStandardDialogs standardDialogs)
        {
            _kitbasherRootScene = kitbasherRootScene;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _renderEngineComponent = renderEngineComponent;
            _uiCommandFactory = uiCommandFactory;
            _standardDialogs = standardDialogs;
            SkeletonNameList = _skeletonAnimationLookUpHelper.GetAllSkeletonFileNames();
     
        }

        static private ObservableCollection<AttachmentPoint> CreateAttachmentPointList(List<RmvAttachmentPoint> attachmentPoints)
        { 
            var output = new ObservableCollection<AttachmentPoint>();
            foreach (var attachmentPoint in attachmentPoints)
            {
                var name = attachmentPoint.Name;
                var newAttachmentPoint = new AttachmentPoint()
                {
                    BoneIndex = attachmentPoint.BoneIndex,
                    Name = name,
                    IsIdentiy = attachmentPoint.Matrix.IsIdentity()
                };
                output.Add(newAttachmentPoint);
            }

            return output;
        }

        [RelayCommand]
        private void ResetAttachmentPointList()
        {
            if (_mainNode != null)
            {
                _mainNode.SetAttachmentPoints([], true);
                AttachmentPointList = CreateAttachmentPointList(_mainNode.AttachmentPoints);
            }
        }

        partial void OnSkeletonNameChanged(string? oldValue, string? newValue)
        {
            var cleanSkeletonName = "";
            if (!string.IsNullOrWhiteSpace(newValue))
                cleanSkeletonName = Path.GetFileNameWithoutExtension(newValue);
            var skeletonBefore = _kitbasherRootScene.Skeleton?.SkeletonName;
            _kitbasherRootScene.SetSkeletonFromName(cleanSkeletonName);

            // Changing the skeleton can make the current attachment points invalid, so rebuild them when the scene's skeleton
            // changed - not when the name did. The filter box sends null each time typing refilters its list, and that keeps
            // the skeleton; keyed on the old name, the pick that followed found it null and kept the old skeleton's points.
            if (_kitbasherRootScene.Skeleton?.SkeletonName != skeletonBefore)
                ResetAttachmentPointList();
        }

        public void Initialize(ISceneNode node)
        {
            if (node is MainEditableNode mainEditableNode)
            {
                _mainNode = mainEditableNode;
                if (_mainNode.SkeletonNode != null && _mainNode.SkeletonNode.Skeleton != null)
                    SkeletonName = SkeletonNameList.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).ToLower() == _mainNode.SkeletonNode.Skeleton.SkeletonName.ToLower());

                AttachmentPointList = CreateAttachmentPointList(_mainNode.AttachmentPoints);
            }
            else
            {
                throw new Exception($"{node} is not of type {nameof(MainEditableNode)}");
            }
        }

        public void CopyTexturesToOutputPack() => _uiCommandFactory.Create<CopyTexturesToPackCommand>(x => x.Configure(_mainNode)).Execute();
        public void Dispose() { }
    }
}
