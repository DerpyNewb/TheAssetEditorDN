using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Editors.VfxEditor.Document;
using Editors.VfxEditor.ViewModels;

namespace Test.VfxEditor
{
    class CurveEditorTests
    {
        const string Text = "x_min,8405056,1;0.0,0.1,linear,log3,-0.05,0.0,0.05,0.0,0;1.0,0.75,sine,sine_recip,-0.05,0.0,0.05,0.0,0|x_max,16728128,1;0.0,0.1,linear,log3,-0.05,0.0,0.05,0.0,0:x,1;0,1";

        // The document re-reads the curve after every key edit. The channel picker, bound as the view binds it, must not
        // drop the picked channel on the way, or the key rows are rebuilt and the box being typed in loses its focus.
        [Test, Apartment(ApartmentState.STA)]
        public void A_reload_keeps_the_key_rows_the_boxes_are_bound_to()
        {
            var editor = new CurveEditor(CurveText.Parse(Text)!, _ => { }, _ => { });
            var picker = new ComboBox { DataContext = editor, DisplayMemberPath = "Name" };
            picker.SetBinding(ItemsControl.ItemsSourceProperty, "Channels");
            picker.SetBinding(Selector.SelectedItemProperty, "SelectedChannel");
            var rows = editor.Keys.ToList();

            editor.Reload(Text.Replace("0.75", "0.8"));

            Assert.That(editor.Keys, Is.EqualTo(rows).AsCollection, "the same row objects");
            Assert.That(editor.Keys[1].Value, Is.EqualTo("0.8"));
        }
    }
}
