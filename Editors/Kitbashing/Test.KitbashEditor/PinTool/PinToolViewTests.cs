using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Editors.KitbasherEditor.ChildEditors.PinTool;
using Editors.KitbasherEditor.ViewModels.PinTool;
using Shared.Core.Commands;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.PinTool
{
    // The mode list is the window's only way into Bone mode, and a DataTrigger on the mode's name swaps its panel in.
    public class PinToolViewTests
    {
        AssetEditorTestRunner _runner = null!;

        [SetUp]
        public void OpenKarl()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(TestFiles.KarlPackFile, true);
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
        }

        // Hosted in a plain window, because an AssetEditorWindow needs the app's main window, which belongs to another test's thread.
        [Test, Apartment(ApartmentState.STA)]
        public void PickingBoneInTheModeList_ShowsTheBoneList()
        {
            var viewModel = _runner.GetRequiredServiceInCurrentEditorScope<PinToolViewModel>();
            var view = new PinToolView { DataContext = viewModel };
            var host = new Window { Content = view, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -10000, Width = 400, Height = 600 };
            try
            {
                host.Show();
                Pump();
                var modes = Find<ComboBox>(view, "PinToolMode");
                Assert.That(Find<ComboBox>(view, "PinToolBone"), Is.Null, "the bone list shows in Pin mode");

                modes!.SelectedItem = RiggingMode.Bone;
                Pump();

                Assert.That(viewModel.SelectedRiggingMode, Is.EqualTo(RiggingMode.Bone));
                var bones = Find<ComboBox>(view, "PinToolBone");
                Assert.That(bones, Is.Not.Null, "Bone mode did not show its bone list");
                Assert.That(bones!.Items, Is.Not.Empty, "the bone list is empty with Karl Franz loaded");
            }
            finally
            {
                host.Close();
            }
        }

        static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }

        static T? Find<T>(DependencyObject root, string automationId) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match && AutomationProperties.GetAutomationId(child) == automationId)
                    return match;
                if (Find<T>(child, automationId) is { } found)
                    return found;
            }
            return null;
        }
    }
}
