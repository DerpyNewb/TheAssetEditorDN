using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Editors.UnitSimulator.Personalities;
using Editors.UnitSimulator.Views;

namespace Test.UnitSimulator
{
    // Where each personality sits: its rows shown once it is added, its bone beside its key, and a marker in the 3D view.
    public class AttachPointTests
    {
        [Test]
        public void APersonalityAdded_IsSelected_SoItsRowsShow_AndASet_SelectsItsFirst()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.WarsphinxModel);

            sim.Personalities.AddSet(PersonalityTests.Warsphinx);
            Assert.That(sim.Personalities.Selected, Is.SameAs(sim.Personalities.Parts[0]));

            var (_, casket) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            var priest = PersonalityTests.AddByKey(casket, PersonalityTests.CasketRider);
            Assert.That(casket.Personalities.Selected, Is.SameAs(priest));
        }

        [Test]
        public void TheAttachedList_ShowsEachKeyWithItsBone_AndFollowsABonePicked()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            var priest = PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider);
            var told = new List<string?>();
            ((INotifyPropertyChanged)priest).PropertyChanged += (_, e) => told.Add(e.PropertyName);

            Assert.That(priest.ListText, Is.EqualTo($"{PersonalityTests.CasketRider} on ap_riderposition_0"));
            priest.Bone = PersonalityList.NoBone;

            Assert.That(priest.ListText, Is.EqualTo($"{PersonalityTests.CasketRider} at the origin"));
            Assert.That(told, Does.Contain(nameof(PersonalityPart.ListText)));
        }

        [Test]
        public void AMarker_StandsWhereItsPartIsPlaced_OncePerBone_AndNoneAtTheOrigin()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.WarsphinxModel);
            sim.Personalities.AddSet(PersonalityTests.Warsphinx);
            var parts = sim.Personalities.Parts;
            parts[1].Bone = parts[0].Bone;
            parts[2].Bone = PersonalityList.NoBone;
            sim.Personalities.PlaceAll();

            var markers = sim.Personalities.Markers();

            Assert.That(markers.Select(x => x.Bone), Is.EquivalentTo(new[] { parts[0].Bone, parts[3].Bone }));
            foreach (var part in new[] { parts[0], parts[3] })
                Assert.That(AttachBonesTests.Off(markers.Single(x => x.Bone == part.Bone).World, part.Part.Data.Offset), Is.LessThan(0.001f));
        }

        static IEnumerable<DependencyObject> Tree(DependencyObject root) =>
            LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(x => Tree(x).Prepend(x));

        static PersonalitiesView Show(PersonalityList list)
        {
            var view = new PersonalitiesView { DataContext = list };
            view.Measure(new Size(600, 4000));
            view.Arrange(new Rect(0, 0, 600, 4000));
            view.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            return view;
        }

        // The rows' section is bound to the selected part: with none, every box in it was empty and nothing said why.
        [Test, Apartment(ApartmentState.STA)]
        public void WithNothingSelected_TheRowsAreHidden_AndThePanelSaysWhatToPick()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider);
            var view = Show(sim.Personalities);
            var rows = Tree(view).OfType<StackPanel>().Single(x => BindingOperations.GetBinding(x, FrameworkElement.DataContextProperty)?.Path.Path == "Selected");
            var hint = (FrameworkElement)Tree(view).OfType<TextBlock>().Single(x => x.Text.StartsWith("Pick a personality")).Parent;
            Assert.That((rows.Visibility, hint.Visibility), Is.EqualTo((Visibility.Visible, Visibility.Collapsed)));

            sim.Personalities.Selected = null;
            view.UpdateLayout();

            Assert.That((rows.Visibility, hint.Visibility), Is.EqualTo((Visibility.Collapsed, Visibility.Visible)));
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TheAttachedListsRow_ReadsTheKeyAndItsBone()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            var priest = PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider);
            var view = Show(sim.Personalities);
            var list = Tree(view).OfType<ListBox>().Single(x => BindingOperations.GetBinding(x, ItemsControl.ItemsSourceProperty)?.Path.Path == "Parts");

            var row = (DependencyObject)list.ItemContainerGenerator.ContainerFromIndex(0);
            var shown = Visuals(row).OfType<TextBlock>().Select(x => x.Text);

            Assert.That(shown, Does.Contain(priest.ListText));
        }

        static IEnumerable<DependencyObject> Visuals(DependencyObject root) =>
            Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(root)).Select(i => VisualTreeHelper.GetChild(root, i)).SelectMany(x => Visuals(x).Prepend(x));

        // A content control reads one underscore as its access key and hides it: "jettison_rider" showed as "jettisonrider".
        [Test, Apartment(ApartmentState.STA)]
        public void NoLabelWritesAnUnderscoreAContentControlWouldHide()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            var view = Show(sim.Personalities);

            var eaten = Tree(view).OfType<ContentControl>().Select(x => x.Content).OfType<string>().Where(x => x.Replace("__", "").Contains('_'));

            Assert.That(eaten, Is.Empty);
        }
    }
}
