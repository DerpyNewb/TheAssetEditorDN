using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Services;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.AnimationFragmentEditor.AnimationPack.BinGrid
{
    /// <summary>A WH3 battle table as a grid. Edits go through the animpack editor's EditTable, so each is one undo step and the
    /// XML view shows the same bytes.</summary>
    public partial class BinGridViewModel : ObservableObject
    {
        readonly Action<AnimationBinWh3, Action<AnimationBinWh3>> _edit;
        readonly BinTableChecks _checks;
        readonly IStandardDialogs _dialogs;
        readonly IUiCommandFactory _commands;
        readonly IPackFileService _pfs;
        readonly ISkeletonAnimationLookUpHelper _skeletons;
        readonly IReadOnlyList<string> _otherBins;
        readonly Func<IReadOnlyCollection<string>, string?> _pickSlot;

        public AnimationBinWh3 Table { get; }

        public BinGridViewModel(AnimationBinWh3 table, Action<AnimationBinWh3, Action<AnimationBinWh3>> edit, BinTableChecks checks, IStandardDialogs dialogs,
            IUiCommandFactory commands, IPackFileService pfs, ISkeletonAnimationLookUpHelper skeletons, IReadOnlyList<string> otherBins, Func<IReadOnlyCollection<string>, string?> pickSlot)
        {
            Table = table;
            _edit = edit;
            _checks = checks;
            _dialogs = dialogs;
            _commands = commands;
            _pfs = pfs;
            _skeletons = skeletons;
            _otherBins = otherBins;
            _pickSlot = pickSlot;
        }

        /// <summary>Rebuilds the rows from the table, after an undo or redo changed it underneath.</summary>
        public void Reload()
        {
        }
    }
}
