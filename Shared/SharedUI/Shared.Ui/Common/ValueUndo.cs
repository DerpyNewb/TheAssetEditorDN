using System;
using System.Collections.Generic;
using Shared.Core.Events;

namespace Shared.Ui.Common
{
    /// <summary>Puts a panel's value edits on an editor's undo stack: one step per run of edits to one value (EditCoalescer), so a
    /// slider's whole drag undoes at once. The edit has already happened when it is reported; undo puts the old value back through
    /// set, which is the panel's own setter, so the box shows it too.</summary>
    public sealed class ValueUndo(CommandManager commands)
    {
        readonly EditCoalescer _coalescer = new();
        bool _applying;

        /// <summary>key: one per value, compared with Equals. name: what Kitbash's undo hint calls it.</summary>
        public void Changed<T>(object key, T oldValue, T newValue, Action<T> set, string name)
        {
            if (_applying || EqualityComparer<T>.Default.Equals(oldValue, newValue))
                return;

            var continues = _coalescer.Continues(key);
            if (continues && commands.Peek() is Edit<T> top && top.Owner == this && Equals(top.Key, key))
            {
                top.NewValue = newValue;
                return;
            }
            commands.ExecuteCommand(new Edit<T>(this, key, oldValue, newValue, set, name));
        }

        void Apply<T>(Action<T> set, T value)
        {
            _applying = true;
            try
            {
                set(value);
            }
            finally
            {
                _applying = false;
            }
        }

        sealed class Edit<T>(ValueUndo owner, object key, T oldValue, T newValue, Action<T> set, string name) : IAeUndoCommandCommand
        {
            public ValueUndo Owner => owner;
            public object Key => key;
            public T NewValue { get; set; } = newValue;
            public string HintText => $"Edit {name}";
            public bool IsMutation => true;

            // The edit is already in place; setting it again changes nothing.
            public void Execute() => owner.Apply(set, NewValue);
            public void Undo() => owner.Apply(set, oldValue);
        }
    }
}
