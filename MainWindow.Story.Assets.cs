using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GalExcleTools.Services;
using GalExcleTools.ViewModels;
using GalExcleTools.Views;
using static GalExcleTools.Services.ColorUtility;
using static GalExcleTools.Services.FileSystemUtility;
using static GalExcleTools.Services.TextUtility;
using static GalExcleTools.Services.WorkspacePathUtility;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace GalExcleTools
{
    public sealed partial class MainWindow
    {
        private MenuFlyout CreateStoryCharacterSlotMenu(int slotIndex)
        {
            return GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("角色", async (_, _) => await ChooseStoryCharacterAsync(slotIndex)),
                GridViewItemFactory.CreateMenuItem("服装", async (_, _) => await ChooseStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Cloth)),
                GridViewItemFactory.CreateMenuItem("表情", async (_, _) => await ChooseStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Face)),
                GridViewItemFactory.CreateMenuItem("装饰", async (_, _) => await ChooseStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Adorn)),
                GridViewItemFactory.CreateMenuItem("滤镜", async (_, _) => await ChooseStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Vfx)));
        }

        private MenuFlyout CreateStorySpeakerSlotMenu()
        {
            return GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("服装", async (_, _) => await ChooseStoryCharacterLayerAsync(0, CharacterLayerKind.Cloth)),
                GridViewItemFactory.CreateMenuItem("表情", async (_, _) => await ChooseStoryCharacterLayerAsync(0, CharacterLayerKind.Face)),
                GridViewItemFactory.CreateMenuItem("装饰", async (_, _) => await ChooseStoryCharacterLayerAsync(0, CharacterLayerKind.Adorn)),
                GridViewItemFactory.CreateMenuItem("滤镜", async (_, _) => await ChooseStoryCharacterLayerAsync(0, CharacterLayerKind.Vfx)));
        }

        private async Task ChooseStoryCharacterAsync(int slotIndex)
        {
            if (!CanEditStoryData())
            {
                return;
            }

            if (_currentStoryAssetLibrary is null || _currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "无法选择角色", "当前项目没有可用的绑定素材库。");
                return;
            }

            var characters = GetStoryCharacters();
            if (characters.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有角色", "绑定素材库里还没有角色卡。");
            }

            var selected = await _storyDialogService.SelectPreviewChoiceAsync(
                "选择角色",
                new[] { new StoryObjectChoice(NoStoryCharacterChoice, "无角色", NoStoryCharacterChoice) }
                    .Concat(characters.Select(character => new StoryObjectChoice(character.Code, $"{character.Name} / {character.Code}", character)))
                    .ToList());
            string replacementCharacterCode;
            if (selected is string noCharacter && string.Equals(noCharacter, NoStoryCharacterChoice, StringComparison.Ordinal))
            {
                replacementCharacterCode = string.Empty;
            }
            else if (selected is CharacterInfo character)
            {
                replacementCharacterCode = character.Code;
            }
            else
            {
                return;
            }

            await ReplaceStoryCharacterForwardAsync(slotIndex, replacementCharacterCode, characters);
        }

        private async Task ChooseStoryCharacterLayerAsync(int slotIndex, CharacterLayerKind layerKind)
        {
            if (!CanEditStoryData())
            {
                return;
            }

            var layerSpec = GetStoryCharacterLayerSpec(layerKind);
            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var characterName = row.Get(StoryCharacterSlotService.GetCharacterColumn(slotIndex));
            var character = ResolveStoryCharacter(characterName);
            if (character is null)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, $"无法选择{layerSpec.DisplayName}", slotIndex == 0 ? "请先填写当前说话人。" : "请先为这个位置选择角色。");
                return;
            }

            if (layerSpec.Kind == CharacterLayerKind.Vfx)
            {
                var filters = GetStoryCharacterFilters();
                if (filters.Count == 0)
                {
                    ShowStoryStatus(InfoBarSeverity.Warning, $"没有{layerSpec.DisplayName}", "当前素材库还没有角色滤镜。");
                    return;
                }

                var selectedFilter = await _storyDialogService.SelectPreviewChoiceAsync(
                    $"选择{layerSpec.DisplayName}",
                    filters.Select((filter, index) => new StoryObjectChoice(index.ToString(), $"{index}: {filter.Remark}", index)).ToList());
                if (selectedFilter is not int selectedFilterIndex)
                {
                    return;
                }

                var filterColumn = StoryCharacterSlotService.GetLayerColumn(slotIndex, layerSpec.FieldPrefix);
                if (ParseInt(row.Get(filterColumn)) == selectedFilterIndex)
                {
                    return;
                }

                await ReplaceStoryCharacterLayerForwardAsync(
                    slotIndex,
                    layerSpec,
                    selectedFilterIndex,
                    index => index >= 0 && index < filters.Count
                        ? CharacterFilterService.GetDisplayName(filters[index], index)
                        : index.ToString(CultureInfo.InvariantCulture));
                return;
            }

            var paths = CharacterLayerAssetService.GetLayerPaths(character, layerSpec.Kind);
            var choices = CreateStoryLayerChoices(layerSpec, paths, character, row, slotIndex);
            if (choices.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, $"没有{layerSpec.DisplayName}", $"角色 {character.Name} 还没有可用的{layerSpec.DisplayName}素材。");
                return;
            }

            var selected = await _storyDialogService.SelectPreviewChoiceAsync(
                $"选择{layerSpec.DisplayName}",
                choices);
            if (selected is not int selectedIndex)
            {
                return;
            }

            if (ParseInt(row.Get(StoryCharacterSlotService.GetLayerColumn(slotIndex, layerSpec.FieldPrefix))) == selectedIndex)
            {
                return;
            }

            await ReplaceStoryCharacterLayerForwardAsync(
                slotIndex,
                layerSpec,
                selectedIndex,
                index => StoryCharacterLayerChoiceFactory.GetDisplayName(layerSpec, paths, index));
        }

        private async Task ReplaceStoryCharacterForwardAsync(
            int slotIndex,
            string replacementCharacterCode,
            IReadOnlyList<CharacterInfo> characters)
        {
            var characterField = StoryCharacterSlotService.GetCharacterColumn(slotIndex);
            var row = _storyRows[_currentStoryRowIndex];
            var replacementPlan = StoryAssetFieldService.BuildForwardContiguousValuePlan(
                _storyRows,
                _currentStoryRowIndex,
                characterField,
                replacementCharacterCode);
            var displayName = $"{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}角色";
            if (replacementPlan.Count == 0)
            {
                if (ParseInt(row.Get(StoryCharacterSlotService.GetLayerColumn(slotIndex, "Body"))) == 0 &&
                    ParseInt(row.Get(StoryCharacterSlotService.GetLayerColumn(slotIndex, "Face"))) == 0 &&
                    ParseInt(row.Get(StoryCharacterSlotService.GetLayerColumn(slotIndex, "Adorn"))) == 0 &&
                    ParseInt(row.Get(StoryCharacterSlotService.GetLayerColumn(slotIndex, "Vfx"))) == 0)
                {
                    return;
                }

                CaptureStoryUndoState($"重置{displayName}分层");
                StoryCharacterSlotService.ResetLayerColumns(row, slotIndex);
                QueueCurrentStoryDocumentSave();
                await RefreshStoryPreviewAsync();
                return;
            }

            if (!await ConfirmStoryValueReplacementAsync(
                    replacementPlan,
                    displayName,
                    value => FormatStoryCharacterValue(characters, value)))
            {
                return;
            }

            CaptureStoryUndoState(replacementPlan.Count > 1 ? $"连续更换{displayName}" : $"更换{displayName}");
            var changedCount = StoryAssetFieldService.ApplyForwardContiguousValuePlan(_storyRows, replacementPlan);
            for (var index = replacementPlan.StartIndex; index <= replacementPlan.EndIndex; index++)
            {
                StoryCharacterSlotService.ResetLayerColumns(_storyRows[index], slotIndex);
            }

            QueueCurrentStoryDocumentSave();
            ShowStoryStatus(
                InfoBarSeverity.Success,
                changedCount > 1 ? $"{displayName}已连续更换" : $"{displayName}已更换",
                changedCount > 1
                    ? $"共修改 {changedCount} 句（第 {_currentStoryRowIndex + 1}-{replacementPlan.EndIndex + 1} 句）。"
                    : FormatStoryCharacterValue(characters, replacementCharacterCode));
            await RefreshStoryPreviewAsync();
        }

        private async Task ReplaceStoryCharacterLayerForwardAsync(
            int slotIndex,
            StoryCharacterLayerSpec layerSpec,
            int replacementIndex,
            Func<int, string> formatValue)
        {
            var layerField = StoryCharacterSlotService.GetLayerColumn(slotIndex, layerSpec.FieldPrefix);
            var characterField = StoryCharacterSlotService.GetCharacterColumn(slotIndex);
            var replacementPlan = StoryAssetFieldService.BuildForwardContiguousCharacterLayerPlan(
                _storyRows,
                _currentStoryRowIndex,
                layerField,
                replacementIndex,
                characterField);
            var displayName = $"{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}{layerSpec.DisplayName}";
            if (!await ConfirmStoryAssetReplacementAsync(replacementPlan, displayName, formatValue))
            {
                return;
            }

            CaptureStoryUndoState(replacementPlan.Count > 1 ? $"连续更换{displayName}" : $"更换{displayName}");
            var changedCount = StoryAssetFieldService.ApplyForwardContiguousPlan(_storyRows, replacementPlan);
            if (layerSpec.Kind == CharacterLayerKind.Cloth)
            {
                for (var index = replacementPlan.StartIndex; index <= replacementPlan.EndIndex; index++)
                {
                    var rowCharacter = ResolveStoryCharacter(_storyRows[index].Get(characterField));
                    if (rowCharacter is not null)
                    {
                        NormalizeStoryRowLayerCompatibility(_storyRows[index], rowCharacter, slotIndex);
                    }
                }
            }

            QueueCurrentStoryDocumentSave();
            if (changedCount > 1)
            {
                ShowStoryStatus(
                    InfoBarSeverity.Success,
                    $"{displayName}已连续更换",
                    $"共修改 {changedCount} 句（第 {_currentStoryRowIndex + 1}-{replacementPlan.EndIndex + 1} 句）。");
            }
            else
            {
                ShowStoryLayerChangedStatus(slotIndex, layerSpec.DisplayName, replacementIndex, formatValue(replacementIndex));
            }
            await RefreshStoryPreviewAsync();
        }

        private static string FormatStoryCharacterValue(
            IReadOnlyList<CharacterInfo> characters,
            string characterCode)
        {
            if (string.IsNullOrWhiteSpace(characterCode))
            {
                return "无角色";
            }

            var character = characters.FirstOrDefault(item =>
                string.Equals(item.Code, characterCode, StringComparison.OrdinalIgnoreCase));
            return character is null ? characterCode : $"{character.Name} / {character.Code}";
        }

        private List<StoryObjectChoice> CreateStoryLayerChoices(
            StoryCharacterLayerSpec layerSpec,
            IReadOnlyList<string> paths,
            CharacterInfo character,
            StoryRow row,
            int slotIndex)
        {
            var currentBodyPath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Cloth, row, slotIndex);
            var currentFacePath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Face, row, slotIndex);
            var currentAdornPath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Adorn, row, slotIndex);
            return StoryCharacterLayerChoiceFactory.CreateChoices(
                layerSpec,
                paths,
                currentBodyPath,
                currentFacePath,
                currentAdornPath,
                (clothPath, layerPath) => _characterLayerAssetService.IsCompatibleWithCloth(character, clothPath, layerPath, ComputeFileHash),
                paths => BuildStoryChoicePreviewPaths(paths.ToArray()));
        }

        private List<int> GetStoryCompatibleLayerIndexes(
            CharacterInfo character,
            StoryCharacterLayerSpec layerSpec,
            IReadOnlyList<string> paths,
            StoryRow row,
            int slotIndex)
        {
            var bodyPath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Cloth, row, slotIndex);
            return StoryCharacterLayerChoiceFactory.GetCompatibleIndexes(
                layerSpec,
                paths,
                bodyPath,
                (clothPath, layerPath) => _characterLayerAssetService.IsCompatibleWithCloth(character, clothPath, layerPath, ComputeFileHash));
        }

        private bool NormalizeStoryRowLayerCompatibility(StoryRow row, CharacterInfo character, int slotIndex)
        {
            var changed = false;
            var bodyPath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Cloth, row, slotIndex);
            changed |= NormalizeStoryLayerCompatibility(row, character, slotIndex, GetStoryCharacterLayerSpec(CharacterLayerKind.Face), bodyPath, false);
            changed |= NormalizeStoryLayerCompatibility(row, character, slotIndex, GetStoryCharacterLayerSpec(CharacterLayerKind.Adorn), bodyPath, true);
            return changed;
        }

        private bool NormalizeStoryLayerCompatibility(
            StoryRow row,
            CharacterInfo character,
            int slotIndex,
            StoryCharacterLayerSpec layerSpec,
            string? bodyPath,
            bool allowNone)
        {
            var columnName = StoryCharacterSlotService.GetLayerColumn(slotIndex, layerSpec.FieldPrefix);
            var currentIndex = ParseInt(row.Get(columnName));
            if (allowNone && currentIndex <= 0)
            {
                return false;
            }

            var currentPath = CharacterLayerAssetService.GetStoryLayerPath(character, layerSpec.Kind, currentIndex);
            if (_characterLayerAssetService.IsCompatibleWithCloth(character, bodyPath, currentPath, ComputeFileHash))
            {
                return false;
            }

            var paths = CharacterLayerAssetService.GetLayerPaths(character, layerSpec.Kind);
            var compatible = paths
                .Select((path, index) => new { path, index })
                .FirstOrDefault(item => _characterLayerAssetService.IsCompatibleWithCloth(character, bodyPath, item.path, ComputeFileHash));
            var nextIndex = compatible is null
                ? 0
                : allowNone ? compatible.index + 1 : compatible.index;
            row.Set(columnName, nextIndex.ToString());
            return currentIndex != nextIndex;
        }

        private static IReadOnlyList<string> BuildStoryChoicePreviewPaths(params string?[] paths)
        {
            return paths
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Cast<string>()
                .ToList();
        }

        private static string? GetStoryCharacterLayerPath(
            CharacterInfo character,
            CharacterLayerKind layerKind,
            StoryRow row,
            int slotIndex)
        {
            var spec = GetStoryCharacterLayerSpec(layerKind);
            var index = ParseInt(row.Get(StoryCharacterSlotService.GetLayerColumn(slotIndex, spec.FieldPrefix)));
            return CharacterLayerAssetService.GetStoryLayerPath(character, layerKind, index);
        }

        private string NormalizeStoryCharacterNameForCsv(string characterName)
        {
            var trimmed = characterName.Trim();
            var character = ResolveStoryCharacter(trimmed);
            return character?.Code ?? trimmed;
        }

        private bool NormalizeStoryCharacterCodes()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return false;
            }

            var changed = false;
            foreach (var row in _storyRows)
            {
                var talkCharacter = ResolveStoryCharacter(row.Get("TalkChar"));
                if (talkCharacter is not null &&
                    !string.Equals(row.Get("TalkChar"), talkCharacter.Code, StringComparison.OrdinalIgnoreCase))
                {
                    row.Set("TalkChar", talkCharacter.Code);
                    changed = true;
                }

                for (var slotIndex = 1; slotIndex <= 5; slotIndex++)
                {
                    var column = $"Chara{slotIndex}";
                    var value = row.Get(column);
                    var character = ResolveStoryCharacter(value);
                    if (character is not null &&
                        !string.Equals(value, character.Code, StringComparison.OrdinalIgnoreCase))
                    {
                        row.Set(column, character.Code);
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private CharacterInfo? ResolveStoryCharacter(string characterName)
        {
            if (_currentStoryAssetLibrary is null)
            {
                return null;
            }

            var normalizedCharacterName = characterName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedCharacterName))
            {
                return null;
            }

            return _characterWorkspaceService
                .GetCharactersByFolderOrder(_currentStoryAssetLibrary)
                .FirstOrDefault(character =>
                    string.Equals(character.Name, normalizedCharacterName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(character.Code, normalizedCharacterName, StringComparison.OrdinalIgnoreCase));
        }

        private List<CharacterInfo> GetStoryCharacters()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            return _characterWorkspaceService.GetCharactersByName(_currentStoryAssetLibrary);
        }

        private List<CharacterInfo> GetStoryCharactersByFolderOrder()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            return _characterWorkspaceService.GetCharactersByFolderName(_currentStoryAssetLibrary);
        }

        private List<StoryAssetChoice> GetStoryBackgroundChoices()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            return BackgroundImageService.GetFilePaths(GetBackgroundFolderPath(_currentStoryAssetLibrary))
                .Select((path, index) => new StoryAssetChoice(index, Path.GetFileNameWithoutExtension(path), path))
                .ToList();
        }

        private List<StoryAssetChoice> GetStoryBgmChoices()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            return AudioAssetService.GetFilePaths(GetMusicFolderPath(_currentStoryAssetLibrary))
                .Select((path, index) => new StoryAssetChoice(index, Path.GetFileNameWithoutExtension(path)))
                .ToList();
        }

        private List<StoryAssetChoice> GetStorySceneChoices()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            return AudioAssetService.GetFilePaths(GetAmbientSoundFolderPath(_currentStoryAssetLibrary))
                .Select((path, index) => new StoryAssetChoice(index, Path.GetFileNameWithoutExtension(path)))
                .ToList();
        }

        private AssetLibraryInfo? ResolveProjectAssetLibrary(ProjectInfo project)
        {
            return _projectWorkspaceService.ResolveProjectAssetLibrary(_projectRootPath, project);
        }

    }
}
