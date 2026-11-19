using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Editors.Ipc;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;

namespace Test.Ipc
{
    // The vm action's paths, against a view model shaped like the editors': lists of rows, a NotifyAttr, a vector, an enum,
    // a dictionary, a command and a public method.
    public class ViewModelPathsTests
    {
        public enum Mode { First, Second }

        public class Row
        {
            public string Name { get; set; } = "";
            public float Speed { get; set; }
        }

        public class Vm
        {
            public List<Row> Rows { get; } = [new Row { Name = "a" }, new Row { Name = "b", Speed = 2 }];
            public NotifyAttr<int> Count { get; } = new(3);
            public Vector3 Offset { get; set; }
            public Mode Mode { get; set; }
            public Dictionary<string, int> Map { get; } = new() { ["x"] = 1 };
            public Row? Picked { get; set; }
            public string Fixed => "r";
            public int Bumped { get; private set; }
            public ICommand Bump { get; }
            public ICommand Never { get; } = new RelayCommand(() => { }, () => false);
            public Vm() => Bump = new RelayCommand(() => Bumped++);
            public int Add(int n) => Bumped += n;
        }

        [Test]
        public void Tree_ListsValuesListsCommandsAndMethods_InTheFormSetTakesBack()
        {
            var (members, truncated) = ViewModelPaths.Tree(new Vm(), null, 1, 100);
            var byPath = members.ToDictionary(x => x.Path);

            Assert.That(truncated, Is.False);
            Assert.That(byPath["Rows"], Is.EqualTo(new ViewModelPaths.Member("Rows", "list", "List<Row>", Writable: false, Count: 2)));
            Assert.That(byPath["Count"], Is.EqualTo(new ViewModelPaths.Member("Count", "value", "Int32", "3", true)), "a NotifyAttr reads as its value");
            Assert.That(byPath["Offset"].Value, Is.EqualTo("0,0,0"));
            Assert.That(byPath["Picked"].Kind, Is.EqualTo("null"));
            Assert.That(byPath["Fixed"].Writable, Is.False);
            Assert.That(byPath["Bump"], Is.EqualTo(new ViewModelPaths.Member("Bump", "command", "RelayCommand", CanExecute: true)));
            Assert.That(byPath["Never"].CanExecute, Is.False);
            Assert.That(byPath["Add"], Is.EqualTo(new ViewModelPaths.Member("Add", "method", "Int32")));
            Assert.That(members.Any(x => x.Path.StartsWith("Rows[")), Is.False, "depth 1 does not open the list");
        }

        [Test]
        public void Tree_OpensListsToTheDepthAsked_AndStopsAtTheLimit()
        {
            Assert.That(ViewModelPaths.Tree(new Vm(), "Rows", 1, 100).Members.Select(x => x.Path), Is.EqualTo(new[] { "Rows[0]", "Rows[1]" }));
            var members = ViewModelPaths.Tree(new Vm(), "Rows", 2, 100).Members;
            Assert.That(members.Select(x => x.Path), Is.EqualTo(new[] { "Rows[0]", "Rows[0].Name", "Rows[0].Speed", "Rows[1]", "Rows[1].Name", "Rows[1].Speed" }));

            var (cut, truncated) = ViewModelPaths.Tree(new Vm(), "Rows", 2, 4);
            Assert.That((cut.Count, truncated), Is.EqualTo((4, true)));
        }

        [TestCase("Rows[1].Speed", "2")]
        [TestCase("Rows[Name=b].Speed", "2")]
        [TestCase("Rows[ name = B ].Speed", "2", Description = "a row picked by a property, case and spaces aside")]
        [TestCase("Map[x]", "1")]
        [TestCase("Count", "3")]
        public void Get_FollowsThePath(string path, string value) => Assert.That(ViewModelPaths.Get(new Vm(), path).Value, Is.EqualTo(value));

        [TestCase("Rows[2]", "Rows has 2 items; [2] is past them")]
        [TestCase("Rows[Name=c]", "No item of Rows has Name = c")]
        [TestCase("Nope", "Vm has no property Nope")]
        [TestCase("Rows[0].Nope", "Row at Rows[0] has no property Nope")]
        [TestCase("Picked.Name", "Picked is null")]
        [TestCase("Rows[0", "Can't read the path from \"[0\"")]
        public void Get_SaysWhereAPathGoesWrong(string path, string error) =>
            Assert.That(() => ViewModelPaths.Get(new Vm(), path), Throws.ArgumentException.With.Message.EqualTo(error));

        [Test]
        public void Set_ConvertsTheText_ToWhatThePropertyHolds()
        {
            var vm = new Vm();
            ViewModelPaths.Set(vm, "Rows[Name=b].Speed", "2.5");
            ViewModelPaths.Set(vm, "Count", "7");
            ViewModelPaths.Set(vm, "Offset", "1, 2.5, -3");
            ViewModelPaths.Set(vm, "Mode", "second");
            ViewModelPaths.Set(vm, "Rows[0].Name", "@@at");

            Assert.That((vm.Rows[1].Speed, vm.Count.Value, vm.Offset, vm.Mode, vm.Rows[0].Name), Is.EqualTo((2.5f, 7, new Vector3(1, 2.5f, -3), Mode.Second, "@at")));
        }

        [Test]
        public void Set_AnAtPath_PassesThatObject()
        {
            var vm = new Vm();
            ViewModelPaths.Set(vm, "Picked", "@Rows[1]");
            Assert.That(vm.Picked, Is.SameAs(vm.Rows[1]));
            ViewModelPaths.Set(vm, "Picked", "null");
            Assert.That(vm.Picked, Is.Null);
            Assert.That(() => ViewModelPaths.Set(vm, "Picked", "@Map"), Throws.ArgumentException.With.Message.Contains("not Row"));
        }

        [Test]
        public void Set_RefusesWhatTheUiCouldNotChange()
        {
            Assert.That(() => ViewModelPaths.Set(new Vm(), "Fixed", "x"), Throws.ArgumentException.With.Message.Contains("no public setter"));
            Assert.That(() => ViewModelPaths.Set(new Vm(), "Bumped", "4"), Throws.ArgumentException.With.Message.Contains("no public setter"));
        }

        [Test]
        public void Invoke_RunsACommand_OrAMethod_AndRefusesOneThatCannotRun()
        {
            var vm = new Vm();
            ViewModelPaths.Invoke(vm, "Bump", null);
            Assert.That(vm.Bumped, Is.EqualTo(1));
            Assert.That(ViewModelPaths.Invoke(vm, "Add", "5").Value, Is.EqualTo("6"));
            Assert.That(() => ViewModelPaths.Invoke(vm, "Never", null), Throws.ArgumentException.With.Message.Contains("can't run now"));
        }
    }
}
