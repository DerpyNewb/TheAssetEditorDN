// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Windows.Controls;
using Editors.AnimationFragmentEditor.AnimationPack.BinGrid;
using Shared.GameFormats.AnimationPack;
using Shared.Ui.Common;

namespace CommonControls.Editors.AnimationPack
{
    /// <summary>
    /// Interaction logic for AnimationPackView.xaml
    /// </summary>
    public partial class AnimationPackView : UserControl
    {
        public AnimationPackView()
        {
            InitializeComponent();
            DataContextChanged += (_, e) =>
            {
                if (e.NewValue is AnimPackViewModel editor)
                    editor.PickSlot = taken => SlotPickerWindow.Pick(AnimationSlotTypeHelperWh3.GetInstance().Values.Select(x => x.Value), taken);
            };
            UndoRedoKeys.Attach(this, () => (DataContext as AnimPackViewModel)?.UndoAction(), () => (DataContext as AnimPackViewModel)?.RedoAction());
        }

    }
}
