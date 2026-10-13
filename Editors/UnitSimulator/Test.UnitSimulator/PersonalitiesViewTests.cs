using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Editors.UnitSimulator.Personalities;
using Editors.UnitSimulator.Views;

namespace Test.UnitSimulator
{
    // The tab's own bindings on a live view: a picker whose list loses the selected part's value must not write that value away.
    public class PersonalitiesViewTests
    {
        static IEnumerable<DependencyObject> Tree(DependencyObject root) =>
            LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(x => Tree(x).Prepend(x));

        static ComboBox Picker(DependencyObject root, string list) =>
            Tree(root).OfType<ComboBox>().Single(x => BindingOperations.GetBinding(x, ItemsControl.ItemsSourceProperty)?.Path.Path.EndsWith(list) == true);

        static PersonalitiesView Show(PersonalityList list)
        {
            var view = new PersonalitiesView { DataContext = list };
            Lay(view);
            return view;
        }

        static void Lay(FrameworkElement view)
        {
            view.Measure(new Size(600, 4000));
            view.Arrange(new Rect(0, 0, 600, 4000));
            view.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }

        // Review Focus 2, in the view.
        [Test, Apartment(ApartmentState.STA)]
        public void AMountSwappedWithAPartSelected_KeepsItsBone_AndTheOldModelPutsItBack()
        {
            var (runner, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            var priest = PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider);
            sim.Personalities.Selected = priest;
            var view = Show(sim.Personalities);
            Assert.That(Picker(view, nameof(PersonalityList.BoneNames)).SelectedItem, Is.EqualTo("ap_riderposition_0"));

            PersonalityTests.SetMount(runner, sim, PersonalityTests.WarsphinxModel);
            Lay(view);
            Assert.That(priest.Bone, Is.EqualTo("ap_riderposition_0"));
            Assert.That(priest.Status, Does.Contain("ap_riderposition_0"));

            PersonalityTests.SetMount(runner, sim, PersonalityTests.CasketModel);
            Lay(view);
            Assert.That(priest.Bone, Is.EqualTo("ap_riderposition_0"));
            Assert.That(priest.Status, Is.Empty);
            Assert.That(Picker(view, nameof(PersonalityList.BoneNames)).SelectedItem, Is.EqualTo("ap_riderposition_0"));
        }

        [Test, Apartment(ApartmentState.STA)]
        public void APartOnABoneOrAnAttachTheListsLack_KeepsBoth_WhenSelected()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            var priest = PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider);
            (priest.Bone, priest.Attach) = ("ap_derpy_nowhere", "derpy_mod_attach");
            var view = Show(sim.Personalities);

            sim.Personalities.Selected = priest;
            Lay(view);

            Assert.That(priest.Bone, Is.EqualTo("ap_derpy_nowhere"));
            Assert.That(priest.Attach, Is.EqualTo("derpy_mod_attach"));
            Assert.That(Picker(view, nameof(PersonalityList.AttachTypes)).SelectedItem, Is.EqualTo("derpy_mod_attach"));
        }
    }
}
