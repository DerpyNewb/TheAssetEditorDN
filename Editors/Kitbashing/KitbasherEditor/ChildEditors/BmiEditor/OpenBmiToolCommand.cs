using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Animation;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using KitbasherEditor.Views.EditorViews;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    // One BMI window per Kitbash tab, for the mesh selected when it opened.
    public class OpenBmiToolCommand : IScopedKitbasherUiCommand, IDisposable
    {
        public string ToolTip { get; set; } = "BMI tool: grow or shrink the selected mesh along its normals, by bone";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.OneObjectSelected;
        public Hotkey? HotKey { get; } = null;

        private readonly IStandardDialogs _standardDialogs;
        private readonly ISkeletonAnimationLookUpHelper _skeletonHelper;
        private readonly SelectionManager _selectionManager;
        private readonly IAbstractFormFactory<BmiWindow> _windowFactory;
        BmiWindow? _windowHandle;

        public OpenBmiToolCommand(IStandardDialogs standardDialogs, ISkeletonAnimationLookUpHelper skeletonHelper, SelectionManager selectionManager, IAbstractFormFactory<BmiWindow> windowFactory)
        {
            _standardDialogs = standardDialogs;
            _skeletonHelper = skeletonHelper;
            _selectionManager = selectionManager;
            _windowFactory = windowFactory;
        }

        public void Execute()
        {
            if (_windowHandle != null)
            {
                _windowHandle.Activate();
                return;
            }

            var state = _selectionManager.GetState<ObjectSelectionState>();
            if (state?.GetSingleSelectedObject() is not Rmv2MeshNode meshNode)
                return;

            if (meshNode.Geometry.VertexFormat == UiVertexFormat.Static)
            {
                _standardDialogs.ShowDialogBox("The BMI tool moves a mesh by its bone weights, and a static mesh has none.", "BMI tool");
                return;
            }

            var skeletonFile = _skeletonHelper.GetSkeletonFileFromName(meshNode.Geometry.SkeletonName);
            if (skeletonFile == null)
            {
                _standardDialogs.ShowDialogBox($"The mesh's skeleton '{meshNode.Geometry.SkeletonName}' was not found in the loaded packs.", "BMI tool");
                return;
            }

            _windowHandle = _windowFactory.Create();
            _windowHandle.ViewModel.Initialize(new GameSkeleton(skeletonFile, null), meshNode);
            _windowHandle.Show();
            _windowHandle.Closed += OnWindowClosed;
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (_windowHandle != null)
                _windowHandle.Closed -= OnWindowClosed;
            _windowHandle = null;
        }

        public void Dispose()
        {
            _windowHandle?.Close();
            _windowHandle = null;
        }
    }
}
