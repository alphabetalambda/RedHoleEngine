using RedHoleEngine.Editor.Commands;
using RedHoleEngine.Editor.Project;

namespace RedHoleEngine.Tests.Editor;

public class SceneDirtyTrackingTests
{
    private sealed class CounterCommand : ICommand
    {
        private readonly int[] _counter;
        private int _amount;
        private readonly bool _mergeable;

        public CounterCommand(int[] counter, int amount, bool mergeable = false)
        {
            _counter = counter;
            _amount = amount;
            _mergeable = mergeable;
        }

        public string Description => $"Add {_amount}";
        public void Execute() => _counter[0] += _amount;
        public void Undo() => _counter[0] -= _amount;
        public bool CanMergeWith(ICommand other) => _mergeable && other is CounterCommand { _mergeable: true };
        public void MergeWith(ICommand other) => _amount += ((CounterCommand)other)._amount;
    }

    [Fact]
    public void StateVersion_ReturnsToSameValue_AfterUndoAndRedo()
    {
        var undo = new UndoRedoManager();
        var counter = new int[1];
        var initial = undo.StateVersion;

        undo.ExecuteCommand(new CounterCommand(counter, 1));
        var afterFirst = undo.StateVersion;
        undo.ExecuteCommand(new CounterCommand(counter, 2));
        var afterSecond = undo.StateVersion;

        Assert.NotEqual(initial, afterFirst);
        Assert.NotEqual(afterFirst, afterSecond);

        undo.Undo();
        Assert.Equal(afterFirst, undo.StateVersion);
        undo.Undo();
        Assert.Equal(initial, undo.StateVersion);
        undo.Redo();
        Assert.Equal(afterFirst, undo.StateVersion);
        undo.Redo();
        Assert.Equal(afterSecond, undo.StateVersion);
    }

    [Fact]
    public void StateVersion_ChangesOnMerge()
    {
        var undo = new UndoRedoManager();
        var counter = new int[1];

        undo.ExecuteCommand(new CounterCommand(counter, 1, mergeable: true));
        var before = undo.StateVersion;
        undo.ExecuteCommand(new CounterCommand(counter, 1, mergeable: true));

        Assert.Equal(1, undo.UndoCount);
        Assert.NotEqual(before, undo.StateVersion);
    }

    [Fact]
    public void StateVersion_ChangesOnClear()
    {
        var undo = new UndoRedoManager();
        var initial = undo.StateVersion;
        undo.Clear();
        Assert.NotEqual(initial, undo.StateVersion);
    }

    [Fact]
    public void StateVersion_AfterHistoryTrim_UndoingEverythingDoesNotReturnToOriginalState()
    {
        var undo = new UndoRedoManager(maxHistorySize: 2);
        var counter = new int[1];
        var initial = undo.StateVersion;

        undo.ExecuteCommand(new CounterCommand(counter, 1));
        undo.ExecuteCommand(new CounterCommand(counter, 1));
        undo.ExecuteCommand(new CounterCommand(counter, 1));

        undo.Undo();
        undo.Undo();
        Assert.False(undo.CanUndo);
        Assert.Equal(1, counter[0]); // the trimmed command cannot be undone
        Assert.NotEqual(initial, undo.StateVersion);
    }

    [Fact]
    public void Tracker_IsCleanInitially_AndDirtyAfterCommand()
    {
        var undo = new UndoRedoManager();
        var tracker = new SceneDirtyTracker(undo);
        Assert.False(tracker.IsDirty);

        undo.ExecuteCommand(new CounterCommand(new int[1], 1));
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void Tracker_UndoBackToSavePoint_IsClean()
    {
        var undo = new UndoRedoManager();
        var tracker = new SceneDirtyTracker(undo);
        var counter = new int[1];

        undo.ExecuteCommand(new CounterCommand(counter, 1));
        tracker.MarkClean();
        Assert.False(tracker.IsDirty);

        undo.ExecuteCommand(new CounterCommand(counter, 1));
        Assert.True(tracker.IsDirty);

        undo.Undo();
        Assert.False(tracker.IsDirty);

        undo.Undo();
        Assert.True(tracker.IsDirty); // before the save point is also a change

        undo.Redo();
        Assert.False(tracker.IsDirty);
    }

    [Fact]
    public void Tracker_NewCommandAfterUndo_StaysDirtyEvenIfCountMatches()
    {
        var undo = new UndoRedoManager();
        var tracker = new SceneDirtyTracker(undo);
        var counter = new int[1];

        undo.ExecuteCommand(new CounterCommand(counter, 1));
        tracker.MarkClean();

        undo.Undo();
        undo.ExecuteCommand(new CounterCommand(counter, 5)); // same stack depth, different state
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void Tracker_UntrackedChange_StaysDirtyUntilSaved()
    {
        var undo = new UndoRedoManager();
        var tracker = new SceneDirtyTracker(undo);
        var counter = new int[1];

        tracker.MarkDirty();
        Assert.True(tracker.IsDirty);

        undo.ExecuteCommand(new CounterCommand(counter, 1));
        undo.Undo();
        Assert.True(tracker.IsDirty);

        tracker.MarkClean();
        Assert.False(tracker.IsDirty);
    }

    [Fact]
    public void Tracker_MarkCleanAfterClear_IsClean()
    {
        var undo = new UndoRedoManager();
        var tracker = new SceneDirtyTracker(undo);

        undo.ExecuteCommand(new CounterCommand(new int[1], 1));
        undo.Clear(); // e.g. a new scene was loaded
        Assert.True(tracker.IsDirty);

        tracker.MarkClean();
        Assert.False(tracker.IsDirty);
    }

    [Fact]
    public void Tracker_RaisesDirtyChanged_OnlyOnTransitions()
    {
        var undo = new UndoRedoManager();
        var tracker = new SceneDirtyTracker(undo);
        var counter = new int[1];
        var events = new List<bool>();
        tracker.DirtyChanged += events.Add;

        undo.ExecuteCommand(new CounterCommand(counter, 1));
        undo.ExecuteCommand(new CounterCommand(counter, 1));
        tracker.MarkDirty();
        tracker.MarkClean();
        tracker.MarkClean();

        Assert.Equal(new[] { true, false }, events);
    }

    [Fact]
    public void AutoSaveManager_ReportsUnsavedChangesFromTracker()
    {
        var undo = new UndoRedoManager();
        var tracker = new SceneDirtyTracker(undo);
        var saves = new List<string>();
        var autoSave = new AutoSaveManager(new ProjectManager(), () => "scene.rhes", () => tracker.IsDirty, saves.Add);

        Assert.False(autoSave.HasUnsavedChanges);

        tracker.MarkDirty();
        autoSave.MarkDirty();
        Assert.True(autoSave.HasUnsavedChanges);

        // Without a project, auto-save is off and never writes
        autoSave.Update();
        Assert.Empty(saves);

        // SaveNow forces a save while there are pending changes
        autoSave.SaveNow();
        Assert.Equal(new[] { "scene.rhes" }, saves);

        tracker.MarkClean();
        Assert.False(autoSave.HasUnsavedChanges);
    }

    [Fact]
    public void AutoSaveManager_SaveNow_SkipsWhenChangesWereUndone()
    {
        var undo = new UndoRedoManager();
        var tracker = new SceneDirtyTracker(undo);
        var saves = new List<string>();
        var autoSave = new AutoSaveManager(new ProjectManager(), () => "scene.rhes", () => tracker.IsDirty, saves.Add);
        undo.StateChanged += autoSave.MarkDirty;

        undo.ExecuteCommand(new CounterCommand(new int[1], 1));
        undo.Undo();

        autoSave.SaveNow();
        Assert.Empty(saves);
    }
}
