using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using GalExcleTools.Services;
using Microsoft.UI.Xaml.Media.Imaging;

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
    private bool _isDebugModeEnabled;
    private bool _canUndo;
    private bool _canRedo;
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
    private string _currentTransitionText = "默认转场";
    private string _currentBackgroundValue = "无";
    private string _currentBgmValue = "无";
    private string _currentSceneValue = "无";
    private string _currentFunctionCountText = "0 项";
    private bool _hasCurrentTransitionFunction;
    private bool _hasCurrentBranch;
    private string _assetStatusText = string.Empty;
    private string _todoText = string.Empty;
    private bool _isLoadingTodo;
    private string _saveStatusText = "已加载";
    private bool _hasCurrentFunction;
    private bool _hasMultipleCurrentFunctions;
    private bool _hasCurrentChoices;
    private StoryCharacterSlotClipboard? _characterSlotClipboard;
    private StoryAssetClipboard? _assetClipboard;
    private string? _backgroundPreviewKey;
    private int _backgroundTransitionMode;
    private bool _bgmPlaybackSuppressed;
    private int? _hoveredCharacterSlotIndex;
    private string? _hoveredAssetFieldName;

    public StoryEditorViewModel()
    {
        UndoCommand = new RelayCommand(() => { });
        RedoCommand = new RelayCommand(() => { });
        PreviousRowCommand = new RelayCommand(() => { });
        NextRowCommand = new RelayCommand(() => { });
        InsertRowCommand = new RelayCommand(() => { });
        InsertAfterRowCommand = new RelayCommand(() => { });
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

    public event EventHandler<Exception>? CommandExecutionFailed;

    public StoryDocument Document { get; } = new();

    public StoryUndoService UndoService { get; } = new();

    public List<StoryRow> Rows => Document.Rows;

    public Dictionary<string, int> RowSections => Document.RowSections;

    public Dictionary<string, BitmapImage> PreviewImageCache { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<int, string> CharacterPreviewKeys { get; } = [];

    public ObservableCollection<string> CurrentFunctionItems { get; } = [];

    public ObservableCollection<StoryBranchInspectorItem> CurrentBranchItems { get; } = [];

    public RelayCommand UndoCommand { get; private set; }

    public RelayCommand RedoCommand { get; private set; }

    public RelayCommand PreviousRowCommand { get; private set; }

    public RelayCommand NextRowCommand { get; private set; }

    public RelayCommand InsertRowCommand { get; private set; }

    public RelayCommand InsertAfterRowCommand { get; private set; }

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

    public string? DocumentPath
    {
        get => _csvPath;
        set
        {
            if (SetProperty(ref _csvPath, value))
            {
                DocumentPathText = value ?? string.Empty;
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

    public bool IsDebugModeEnabled
    {
        get => _isDebugModeEnabled;
        set
        {
            if (SetProperty(ref _isDebugModeEnabled, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public bool CanUndo
    {
        get => _canUndo;
        set => SetProperty(ref _canUndo, value);
    }

    public bool CanRedo
    {
        get => _canRedo;
        set => SetProperty(ref _canRedo, value);
    }

    public string RowIndexText
    {
        get => _rowIndexText;
        set => SetProperty(ref _rowIndexText, value);
    }

    /// <summary>
    /// 当前句的 TODO 备注正文（右边那个多行文本框，双向绑定）。
    ///
    /// 回车在这里**就是换行**，不做任何拦截 —— 备注经常要写好几行，
    /// 拿回车当"提交"会让它没法用。存盘交给编辑器本来那套自动保存
    /// （<c>ScheduleStoryTodoSave</c> 走同一个 debounce 计时器）。
    /// </summary>
    public string TodoText
    {
        get => _todoText;
        set => SetProperty(ref _todoText, value);
    }

    /// <summary>
    /// 全部备注（按章节 + 行号稳定排序）。
    ///
    /// 为什么用 <see cref="ObservableCollection{T}"/>：增删一条要能立刻反映到界面上，
    /// 而每次改动都整体重建列表会把滚动位置和勾选状态一起丢掉。
    /// </summary>
    public ObservableCollection<StoryTodoRowViewModel> TodoItems { get; } = [];

    /// <summary>载入某一句的备注时把它置起来，免得 TextChanged 又把"载入"当成"用户编辑"。</summary>
    public bool IsLoadingTodo
    {
        get => _isLoadingTodo;
        set => SetProperty(ref _isLoadingTodo, value);
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

    public string DocumentPathText
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

    public string CurrentTransitionText
    {
        get => _currentTransitionText;
        set => SetProperty(ref _currentTransitionText, value);
    }

    public string CurrentBackgroundValue
    {
        get => _currentBackgroundValue;
        set => SetProperty(ref _currentBackgroundValue, value);
    }

    public string CurrentBgmValue
    {
        get => _currentBgmValue;
        set => SetProperty(ref _currentBgmValue, value);
    }

    public string CurrentSceneValue
    {
        get => _currentSceneValue;
        set => SetProperty(ref _currentSceneValue, value);
    }

    public string CurrentFunctionCountText
    {
        get => _currentFunctionCountText;
        set => SetProperty(ref _currentFunctionCountText, value);
    }

    public bool HasCurrentTransitionFunction
    {
        get => _hasCurrentTransitionFunction;
        set => SetProperty(ref _hasCurrentTransitionFunction, value);
    }

    public string AssetStatusText
    {
        get => _assetStatusText;
        set => SetProperty(ref _assetStatusText, value);
    }

    public string SaveStatusText
    {
        get => _saveStatusText;
        set => SetProperty(ref _saveStatusText, value);
    }

    public bool HasCurrentFunction
    {
        get => _hasCurrentFunction;
        set
        {
            if (SetProperty(ref _hasCurrentFunction, value))
            {
                ClearFunctionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasMultipleCurrentFunctions
    {
        get => _hasMultipleCurrentFunctions;
        set
        {
            if (SetProperty(ref _hasMultipleCurrentFunctions, value))
            {
                RemoveFunctionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasCurrentChoices
    {
        get => _hasCurrentChoices;
        set
        {
            if (SetProperty(ref _hasCurrentChoices, value))
            {
                ViewChoicesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public StoryCharacterSlotClipboard? CharacterSlotClipboard
    {
        get => _characterSlotClipboard;
        set => SetProperty(ref _characterSlotClipboard, value);
    }

    public StoryAssetClipboard? AssetClipboard
    {
        get => _assetClipboard;
        set => SetProperty(ref _assetClipboard, value);
    }

    public string? BackgroundPreviewKey
    {
        get => _backgroundPreviewKey;
        set => SetProperty(ref _backgroundPreviewKey, value);
    }

    public int BackgroundTransitionMode
    {
        get => _backgroundTransitionMode;
        set => SetProperty(ref _backgroundTransitionMode, value);
    }

    public bool BgmPlaybackSuppressed
    {
        get => _bgmPlaybackSuppressed;
        set => SetProperty(ref _bgmPlaybackSuppressed, value);
    }

    public int? HoveredCharacterSlotIndex
    {
        get => _hoveredCharacterSlotIndex;
        set => SetProperty(ref _hoveredCharacterSlotIndex, value);
    }

    public string? HoveredAssetFieldName
    {
        get => _hoveredAssetFieldName;
        set => SetProperty(ref _hoveredAssetFieldName, value);
    }

    public void RefreshCollectionState()
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(RowSections));
        OnPropertyChanged(nameof(CurrentRowNumber));
    }

    public bool HasCurrentBranch
    {
        get => _hasCurrentBranch;
        set => SetProperty(ref _hasCurrentBranch, value);
    }

    public void SetCurrentFunctionItems(IReadOnlyList<string> values)
    {
        CurrentFunctionItems.Clear();
        foreach (var value in values)
        {
            CurrentFunctionItems.Add(value);
        }

        CurrentFunctionCountText = $"{values.Count} 项";

        OnPropertyChanged(nameof(CurrentFunctionItems));
    }

    public void SetCurrentBranchItems(IReadOnlyList<StoryBranchInspectorItem> values)
    {
        CurrentBranchItems.Clear();
        foreach (var value in values)
        {
            CurrentBranchItems.Add(value);
        }

        HasCurrentBranch = values.Count > 0;
        OnPropertyChanged(nameof(CurrentBranchItems));
    }

    public void RefreshUndoState()
    {
        CanUndo = UndoService.CanUndo;
        CanRedo = UndoService.CanRedo;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    public void ConfigureCommands(
        Action undo,
        Action redo,
        Action previousRow,
        Action nextRow,
        Action insertRow,
        Action insertAfterRow,
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
        UndoCommand = new RelayCommand(undo, () => CanUndo && !IsDebugModeEnabled);
        RedoCommand = new RelayCommand(redo, () => CanRedo && !IsDebugModeEnabled);
        PreviousRowCommand = new RelayCommand(previousRow, () => Rows.Count > 0 && CurrentRowIndex > 0);
        NextRowCommand = new RelayCommand(nextRow, () => Rows.Count > 0 && CurrentRowIndex < Rows.Count - 1);
        InsertRowCommand = new RelayCommand(insertRow, () => Rows.Count > 0 && DocumentPath is not null && !IsDebugModeEnabled);
        InsertAfterRowCommand = new RelayCommand(insertAfterRow, () => Rows.Count > 0 && DocumentPath is not null && !IsDebugModeEnabled);
        DeleteRowCommand = new RelayCommand(deleteRow, () => Rows.Count > 0 && DocumentPath is not null && !IsDebugModeEnabled);
        PreviousSectionCommand = new RelayCommand(previousSection, () => Rows.Count > 0);
        NextSectionCommand = new RelayCommand(nextSection, () => Rows.Count > 0);
        AddSectionCommand = new RelayCommand(addSection, () => Rows.Count > 0 && DocumentPath is not null && !IsDebugModeEnabled);
        ChangeBackgroundCommand = new AsyncRelayCommand(changeBackground, () => Rows.Count > 0 && DocumentPath is not null && !IsDebugModeEnabled);
        ChangeBgmCommand = new AsyncRelayCommand(changeBgm, () => Rows.Count > 0 && DocumentPath is not null && !IsDebugModeEnabled);
        ChangeSceneCommand = new AsyncRelayCommand(changeScene, () => Rows.Count > 0 && DocumentPath is not null && !IsDebugModeEnabled);
        ChooseFunctionCommand = new AsyncRelayCommand(chooseFunction, () => Rows.Count > 0 && DocumentPath is not null && !IsDebugModeEnabled);
        ViewChoicesCommand = new AsyncRelayCommand(viewChoices, () => Rows.Count > 0 && HasCurrentChoices);
        RemoveFunctionCommand = new AsyncRelayCommand(removeFunction, () => Rows.Count > 0 && HasMultipleCurrentFunctions && !IsDebugModeEnabled);
        ClearFunctionCommand = new RelayCommand(clearFunction, () => Rows.Count > 0 && HasCurrentFunction && !IsDebugModeEnabled);
        ClearCurrentRowCommand = new RelayCommand(clearCurrentRow, () => Rows.Count > 0 && DocumentPath is not null && !IsDebugModeEnabled);
        foreach (var command in new[]
                 {
                     ChangeBackgroundCommand,
                     ChangeBgmCommand,
                     ChangeSceneCommand,
                     ChooseFunctionCommand,
                     ViewChoicesCommand,
                     RemoveFunctionCommand
                 })
        {
            command.ExecutionFailed += (_, exception) => CommandExecutionFailed?.Invoke(this, exception);
        }
        OnPropertyChanged(nameof(UndoCommand));
        OnPropertyChanged(nameof(RedoCommand));
        OnPropertyChanged(nameof(PreviousRowCommand));
        OnPropertyChanged(nameof(NextRowCommand));
        OnPropertyChanged(nameof(InsertRowCommand));
        OnPropertyChanged(nameof(InsertAfterRowCommand));
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
        RedoCommand.NotifyCanExecuteChanged();
        PreviousRowCommand.NotifyCanExecuteChanged();
        NextRowCommand.NotifyCanExecuteChanged();
        InsertRowCommand.NotifyCanExecuteChanged();
        InsertAfterRowCommand.NotifyCanExecuteChanged();
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
}
