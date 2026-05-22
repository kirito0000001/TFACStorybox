using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GalExcleTools.ViewModels;

internal sealed class StoryEditorViewModel : ObservableObject
{
    private ChapterInfo? _chapter;
    private AssetLibraryInfo? _assetLibrary;
    private string? _csvPath;
    private int _currentRowIndex;
    private bool _isLoadingRow;
    private bool _isRowDirty;
    private bool _isUpdatingRowIndexText;
    private bool _isUpdatingSectionOptions;
    private bool _isPersistingRows;
    private bool _isDebugModeEnabled;
    private bool _canUndo;
    private string _rowIndexText = "1";
    private string _rowTotalText = "/ 1 句";
    private string _title = string.Empty;
    private string _csvPathText = string.Empty;
    private string _speakerText = string.Empty;
    private string _storyText = string.Empty;
    private string _currentBackgroundText = "当前背景图：无";
    private string _currentBgmText = "当前BGM：无";
    private string _currentSceneText = "当前环境音：无";
    private string _currentFunctionText = "当前函数：无";
    private string _assetStatusText = string.Empty;
    private bool _hasCurrentFunction;
    private bool _hasCurrentChoices;

    public StoryEditorViewModel()
    {
        UndoCommand = new RelayCommand(() => { });
        PreviousRowCommand = new RelayCommand(() => { });
        NextRowCommand = new RelayCommand(() => { });
        InsertRowCommand = new RelayCommand(() => { });
        DeleteRowCommand = new RelayCommand(() => { });
        PreviousSectionCommand = new RelayCommand(() => { });
        NextSectionCommand = new RelayCommand(() => { });
        AddSectionCommand = new RelayCommand(() => { });
        ChangeBackgroundCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        ChangeBgmCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        ChangeSceneCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        ChooseFunctionCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        ViewChoicesCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        RemoveFunctionCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        ClearFunctionCommand = new RelayCommand(() => { });
        ClearCurrentRowCommand = new RelayCommand(() => { });
    }

    public List<StoryRow> Rows { get; } = [];

    public Dictionary<string, int> RowSections { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<StoryEditorUndoState> UndoStack { get; } = [];

    public RelayCommand UndoCommand { get; private set; }

    public RelayCommand PreviousRowCommand { get; private set; }

    public RelayCommand NextRowCommand { get; private set; }

    public RelayCommand InsertRowCommand { get; private set; }

    public RelayCommand DeleteRowCommand { get; private set; }

    public RelayCommand PreviousSectionCommand { get; private set; }

    public RelayCommand NextSectionCommand { get; private set; }

    public RelayCommand AddSectionCommand { get; private set; }

    public AsyncRelayCommand ChangeBackgroundCommand { get; private set; }

    public AsyncRelayCommand ChangeBgmCommand { get; private set; }

    public AsyncRelayCommand ChangeSceneCommand { get; private set; }

    public AsyncRelayCommand ChooseFunctionCommand { get; private set; }

    public AsyncRelayCommand ViewChoicesCommand { get; private set; }

    public AsyncRelayCommand RemoveFunctionCommand { get; private set; }

    public RelayCommand ClearFunctionCommand { get; private set; }

    public RelayCommand ClearCurrentRowCommand { get; private set; }

    public ChapterInfo? Chapter
    {
        get => _chapter;
        set => SetProperty(ref _chapter, value);
    }

    public AssetLibraryInfo? AssetLibrary
    {
        get => _assetLibrary;
        set => SetProperty(ref _assetLibrary, value);
    }

    public string? CsvPath
    {
        get => _csvPath;
        set
        {
            if (SetProperty(ref _csvPath, value))
            {
                CsvPathText = value ?? string.Empty;
            }
        }
    }

    public int CurrentRowIndex
    {
        get => _currentRowIndex;
        set
        {
            if (SetProperty(ref _currentRowIndex, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(CurrentRowNumber));
            }
        }
    }

    public int CurrentRowNumber => CurrentRowIndex + 1;

    public bool IsLoadingRow
    {
        get => _isLoadingRow;
        set => SetProperty(ref _isLoadingRow, value);
    }

    public bool IsRowDirty
    {
        get => _isRowDirty;
        set => SetProperty(ref _isRowDirty, value);
    }

    public bool IsUpdatingRowIndexText
    {
        get => _isUpdatingRowIndexText;
        set => SetProperty(ref _isUpdatingRowIndexText, value);
    }

    public bool IsUpdatingSectionOptions
    {
        get => _isUpdatingSectionOptions;
        set => SetProperty(ref _isUpdatingSectionOptions, value);
    }

    public bool IsPersistingRows
    {
        get => _isPersistingRows;
        set => SetProperty(ref _isPersistingRows, value);
    }

    public bool IsDebugModeEnabled
    {
        get => _isDebugModeEnabled;
        set => SetProperty(ref _isDebugModeEnabled, value);
    }

    public bool CanUndo
    {
        get => _canUndo;
        set => SetProperty(ref _canUndo, value);
    }

    public string RowIndexText
    {
        get => _rowIndexText;
        set => SetProperty(ref _rowIndexText, value);
    }

    public string RowTotalText
    {
        get => _rowTotalText;
        set => SetProperty(ref _rowTotalText, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string CsvPathText
    {
        get => _csvPathText;
        set => SetProperty(ref _csvPathText, value);
    }

    public string SpeakerText
    {
        get => _speakerText;
        set => SetProperty(ref _speakerText, value);
    }

    public string StoryText
    {
        get => _storyText;
        set => SetProperty(ref _storyText, value);
    }

    public string CurrentBackgroundText
    {
        get => _currentBackgroundText;
        set => SetProperty(ref _currentBackgroundText, value);
    }

    public string CurrentBgmText
    {
        get => _currentBgmText;
        set => SetProperty(ref _currentBgmText, value);
    }

    public string CurrentSceneText
    {
        get => _currentSceneText;
        set => SetProperty(ref _currentSceneText, value);
    }

    public string CurrentFunctionText
    {
        get => _currentFunctionText;
        set => SetProperty(ref _currentFunctionText, value);
    }

    public string AssetStatusText
    {
        get => _assetStatusText;
        set => SetProperty(ref _assetStatusText, value);
    }

    public bool HasCurrentFunction
    {
        get => _hasCurrentFunction;
        set => SetProperty(ref _hasCurrentFunction, value);
    }

    public bool HasCurrentChoices
    {
        get => _hasCurrentChoices;
        set => SetProperty(ref _hasCurrentChoices, value);
    }

    public void RefreshCollectionState()
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(RowSections));
        OnPropertyChanged(nameof(CurrentRowNumber));
    }

    public void RefreshUndoState()
    {
        CanUndo = UndoStack.Count > 0;
        UndoCommand.NotifyCanExecuteChanged();
    }

    public void ConfigureCommands(
        Action undo,
        Action previousRow,
        Action nextRow,
        Action insertRow,
        Action deleteRow,
        Action previousSection,
        Action nextSection,
        Action addSection,
        Func<Task> changeBackground,
        Func<Task> changeBgm,
        Func<Task> changeScene,
        Func<Task> chooseFunction,
        Func<Task> viewChoices,
        Func<Task> removeFunction,
        Action clearFunction,
        Action clearCurrentRow)
    {
        UndoCommand = new RelayCommand(undo, () => CanUndo);
        PreviousRowCommand = new RelayCommand(previousRow, () => Rows.Count > 0 && CurrentRowIndex > 0);
        NextRowCommand = new RelayCommand(nextRow, () => Rows.Count > 0);
        InsertRowCommand = new RelayCommand(insertRow, () => Rows.Count > 0 && CsvPath is not null && !IsDebugModeEnabled);
        DeleteRowCommand = new RelayCommand(deleteRow, () => Rows.Count > 0 && CsvPath is not null);
        PreviousSectionCommand = new RelayCommand(previousSection, () => Rows.Count > 0);
        NextSectionCommand = new RelayCommand(nextSection, () => Rows.Count > 0);
        AddSectionCommand = new RelayCommand(addSection, () => Rows.Count > 0 && CsvPath is not null);
        ChangeBackgroundCommand = new AsyncRelayCommand(changeBackground, () => Rows.Count > 0 && CsvPath is not null);
        ChangeBgmCommand = new AsyncRelayCommand(changeBgm, () => Rows.Count > 0 && CsvPath is not null);
        ChangeSceneCommand = new AsyncRelayCommand(changeScene, () => Rows.Count > 0 && CsvPath is not null);
        ChooseFunctionCommand = new AsyncRelayCommand(chooseFunction, () => Rows.Count > 0 && CsvPath is not null);
        ViewChoicesCommand = new AsyncRelayCommand(viewChoices, () => Rows.Count > 0 && HasCurrentChoices);
        RemoveFunctionCommand = new AsyncRelayCommand(removeFunction, () => Rows.Count > 0 && HasCurrentFunction);
        ClearFunctionCommand = new RelayCommand(clearFunction, () => Rows.Count > 0 && HasCurrentFunction);
        ClearCurrentRowCommand = new RelayCommand(clearCurrentRow, () => Rows.Count > 0 && CsvPath is not null);
        OnPropertyChanged(nameof(UndoCommand));
        OnPropertyChanged(nameof(PreviousRowCommand));
        OnPropertyChanged(nameof(NextRowCommand));
        OnPropertyChanged(nameof(InsertRowCommand));
        OnPropertyChanged(nameof(DeleteRowCommand));
        OnPropertyChanged(nameof(PreviousSectionCommand));
        OnPropertyChanged(nameof(NextSectionCommand));
        OnPropertyChanged(nameof(AddSectionCommand));
        OnPropertyChanged(nameof(ChangeBackgroundCommand));
        OnPropertyChanged(nameof(ChangeBgmCommand));
        OnPropertyChanged(nameof(ChangeSceneCommand));
        OnPropertyChanged(nameof(ChooseFunctionCommand));
        OnPropertyChanged(nameof(ViewChoicesCommand));
        OnPropertyChanged(nameof(RemoveFunctionCommand));
        OnPropertyChanged(nameof(ClearFunctionCommand));
        OnPropertyChanged(nameof(ClearCurrentRowCommand));
        RefreshCommandStates();
    }

    public void RefreshCommandStates()
    {
        UndoCommand.NotifyCanExecuteChanged();
        PreviousRowCommand.NotifyCanExecuteChanged();
        NextRowCommand.NotifyCanExecuteChanged();
        InsertRowCommand.NotifyCanExecuteChanged();
        DeleteRowCommand.NotifyCanExecuteChanged();
        PreviousSectionCommand.NotifyCanExecuteChanged();
        NextSectionCommand.NotifyCanExecuteChanged();
        AddSectionCommand.NotifyCanExecuteChanged();
        ChangeBackgroundCommand.NotifyCanExecuteChanged();
        ChangeBgmCommand.NotifyCanExecuteChanged();
        ChangeSceneCommand.NotifyCanExecuteChanged();
        ChooseFunctionCommand.NotifyCanExecuteChanged();
        ViewChoicesCommand.NotifyCanExecuteChanged();
        RemoveFunctionCommand.NotifyCanExecuteChanged();
        ClearFunctionCommand.NotifyCanExecuteChanged();
        ClearCurrentRowCommand.NotifyCanExecuteChanged();
    }

    public (int LocalIndex, int Total) GetCurrentSectionPositionInfo()
    {
        if (Rows.Count == 0)
        {
            return (1, 1);
        }

        var currentSection = GetCurrentSection();
        var total = 0;
        var localIndex = 1;
        for (var i = 0; i < Rows.Count; i++)
        {
            if (GetSectionAtRowIndex(i) != currentSection)
            {
                continue;
            }

            total++;
            if (i == CurrentRowIndex)
            {
                localIndex = total;
            }
        }

        return (Math.Max(1, localIndex), Math.Max(1, total));
    }

    public int GetCurrentSection()
    {
        return Rows.Count == 0 ? 1 : GetSectionAtRowIndex(CurrentRowIndex);
    }

    public int GetSectionAtRowIndex(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count)
        {
            return 1;
        }

        var rowName = Rows[rowIndex].Get("Name");
        return RowSections.TryGetValue(rowName, out var section) ? Math.Max(1, section) : 1;
    }

    public void SetCurrentSection(int section)
    {
        if (Rows.Count == 0)
        {
            return;
        }

        RowSections[Rows[CurrentRowIndex].Get("Name")] = Math.Max(1, section);
        OnPropertyChanged(nameof(RowSections));
    }

    public bool SetCurrentSectionIfChanged(int section)
    {
        if (Rows.Count == 0)
        {
            return false;
        }

        section = Math.Max(1, section);
        var rowName = Rows[CurrentRowIndex].Get("Name");
        if (RowSections.TryGetValue(rowName, out var currentSection) &&
            Math.Max(1, currentSection) == section)
        {
            return false;
        }

        RowSections[rowName] = section;
        OnPropertyChanged(nameof(RowSections));
        return true;
    }
}
