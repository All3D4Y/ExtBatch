using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;

namespace CmOxeProvisioner.Services
{
    public interface IDialPlanEditCommand
    {
        string Description { get; }
        void Execute();
        void Undo();
    }

    /// <summary>
    /// Command-pattern undo/redo history stack (Ctrl+Z / Ctrl+Y) for DialPlan editing.
    /// </summary>
    public sealed class DialPlanCommandHistory
    {
        private readonly List<IDialPlanEditCommand> _undoStack = new List<IDialPlanEditCommand>();
        private readonly List<IDialPlanEditCommand> _redoStack = new List<IDialPlanEditCommand>();
        private readonly int _maxCapacity;

        public event EventHandler HistoryChanged;

        public DialPlanCommandHistory(int maxCapacity = 150)
        {
            _maxCapacity = Math.Max(10, maxCapacity);
        }

        public bool CanUndo { get { return _undoStack.Count > 0; } }
        public bool CanRedo { get { return _redoStack.Count > 0; } }
        public int UndoCount { get { return _undoStack.Count; } }
        public int RedoCount { get { return _redoStack.Count; } }

        public string NextUndoDescription
        {
            get { return _undoStack.Count > 0 ? _undoStack[_undoStack.Count - 1].Description : string.Empty; }
        }

        public string NextRedoDescription
        {
            get { return _redoStack.Count > 0 ? _redoStack[_redoStack.Count - 1].Description : string.Empty; }
        }

        public void ExecuteAndPush(IDialPlanEditCommand command)
        {
            if (command == null) return;
            command.Execute();
            PushInternal(command);
        }

        public void PushAlreadyExecuted(IDialPlanEditCommand command)
        {
            if (command == null) return;
            PushInternal(command);
        }

        public IDialPlanEditCommand Undo()
        {
            if (_undoStack.Count == 0) return null;
            int last = _undoStack.Count - 1;
            IDialPlanEditCommand cmd = _undoStack[last];
            _undoStack.RemoveAt(last);
            cmd.Undo();
            _redoStack.Add(cmd);
            RaiseChanged();
            return cmd;
        }

        public IDialPlanEditCommand Redo()
        {
            if (_redoStack.Count == 0) return null;
            int last = _redoStack.Count - 1;
            IDialPlanEditCommand cmd = _redoStack[last];
            _redoStack.RemoveAt(last);
            cmd.Execute();
            _undoStack.Add(cmd);
            RaiseChanged();
            return cmd;
        }

        public void Clear()
        {
            if (_undoStack.Count == 0 && _redoStack.Count == 0) return;
            _undoStack.Clear();
            _redoStack.Clear();
            RaiseChanged();
        }

        private void PushInternal(IDialPlanEditCommand command)
        {
            _undoStack.Add(command);
            if (_undoStack.Count > _maxCapacity)
                _undoStack.RemoveAt(0);
            _redoStack.Clear();
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            EventHandler h = HistoryChanged;
            if (h != null) h(this, EventArgs.Empty);
        }
    }

    public sealed class PropertyEditCommand : IDialPlanEditCommand
    {
        private readonly object _target;
        private readonly string _propertyName;
        private readonly object _oldValue;
        private readonly object _newValue;
        private readonly string _description;
        private readonly Action<bool> _afterApply;

        public string Description { get { return _description; } }

        public PropertyEditCommand(
            object target,
            string propertyName,
            object oldValue,
            object newValue,
            string description,
            Action<bool> afterApply = null)
        {
            _target = target;
            _propertyName = propertyName ?? string.Empty;
            _oldValue = oldValue;
            _newValue = newValue;
            _description = string.IsNullOrWhiteSpace(description) ? propertyName : description;
            _afterApply = afterApply;
        }

        public void Execute()
        {
            WriteValue(_target, _propertyName, _newValue);
            if (_afterApply != null) _afterApply(false);
        }

        public void Undo()
        {
            WriteValue(_target, _propertyName, _oldValue);
            if (_afterApply != null) _afterApply(true);
        }

        public static object ReadValue(object target, string propertyName)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName)) return null;

            DataRowView drv = target as DataRowView;
            if (drv != null)
            {
                if (drv.Row != null && drv.Row.Table != null && drv.Row.Table.Columns.Contains(propertyName))
                    return drv.Row[propertyName];
                return null;
            }

            PropertyInfo prop = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return prop != null ? prop.GetValue(target, null) : null;
        }

        public static void WriteValue(object target, string propertyName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName)) return;

            DataRowView drv = target as DataRowView;
            if (drv != null)
            {
                if (drv.Row != null && drv.Row.Table != null && drv.Row.Table.Columns.Contains(propertyName))
                {
                    DataColumn col = drv.Row.Table.Columns[propertyName];
                    if (col.DataType == typeof(int))
                    {
                        int iv;
                        if (value is int) drv.Row[propertyName] = (int)value;
                        else if (int.TryParse(Convert.ToString(value), out iv)) drv.Row[propertyName] = iv;
                    }
                    else
                    {
                        drv.Row[propertyName] = value == null || value == DBNull.Value ? string.Empty : Convert.ToString(value);
                    }
                }
                return;
            }

            PropertyInfo prop = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (prop == null || !prop.CanWrite) return;

            if (prop.PropertyType == typeof(bool))
            {
                bool bv = value is bool ? (bool)value : string.Equals(Convert.ToString(value), "true", StringComparison.OrdinalIgnoreCase);
                prop.SetValue(target, bv, null);
            }
            else if (prop.PropertyType == typeof(int))
            {
                int iv = 0;
                if (value is int) iv = (int)value;
                else int.TryParse(Convert.ToString(value), out iv);
                prop.SetValue(target, iv, null);
            }
            else if (prop.PropertyType == typeof(string))
            {
                prop.SetValue(target, value == null ? string.Empty : Convert.ToString(value), null);
            }
            else
            {
                prop.SetValue(target, value, null);
            }
        }
    }

    public sealed class ListInsertRemoveCommand<T> : IDialPlanEditCommand
    {
        private readonly IList<T> _list;
        private readonly T _item;
        private readonly int _index;
        private readonly bool _isInsert;
        private readonly string _description;
        private readonly Action<T, bool> _afterApply;

        public string Description { get { return _description; } }

        public ListInsertRemoveCommand(
            IList<T> list,
            T item,
            int index,
            bool isInsert,
            string description,
            Action<T, bool> afterApply = null)
        {
            _list = list;
            _item = item;
            _index = index;
            _isInsert = isInsert;
            _description = string.IsNullOrWhiteSpace(description) ? (isInsert ? "행 추가" : "행 삭제") : description;
            _afterApply = afterApply;
        }

        public void Execute()
        {
            if (_list == null || _item == null) return;
            if (_isInsert)
                InsertAtSafe(_index);
            else
                RemoveSafe();
            if (_afterApply != null) _afterApply(_item, false);
        }

        public void Undo()
        {
            if (_list == null || _item == null) return;
            if (_isInsert)
                RemoveSafe();
            else
                InsertAtSafe(_index);
            if (_afterApply != null) _afterApply(_item, true);
        }

        private void InsertAtSafe(int index)
        {
            if (_list.Contains(_item)) return;
            int safe = Math.Max(0, Math.Min(index, _list.Count));
            _list.Insert(safe, _item);
        }

        private void RemoveSafe()
        {
            _list.Remove(_item);
        }
    }
}
