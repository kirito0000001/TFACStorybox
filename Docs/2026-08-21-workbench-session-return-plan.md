# Workbench Session Return Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Return to the previous stable project-workbench page after visiting another shell module, while refreshing story asset bindings without rebuilding the editor session.

**Architecture:** Keep a small in-memory enum on the `MainWindow` partial class. Stable project pages update the enum when opened; shell navigation saves an active story before leaving and dispatches back through one restoration method. Story restoration reuses the current document and undo stack, but resolves the linked asset library again and reloads the active row preview.

**Tech Stack:** C# 12, .NET 8, WinUI 3, PowerShell 7 source regression scripts

---

### Task 1: Specify navigation-session behavior

**Files:**
- Create: `Scripts/Test-WorkbenchPageMemory.ps1`

- [ ] Add source assertions for the workbench return-page enum, save-before-leave sequence, story-session restoration and shell-navigation dispatch.
- [ ] Run `pwsh -NoProfile -File Scripts/Test-WorkbenchPageMemory.ps1` and confirm it fails because the session methods do not exist.

### Task 2: Add focused workbench-session state

**Files:**
- Create: `MainWindow.WorkbenchSession.cs`
- Modify: `MainWindow.Navigation.cs`
- Modify: `MainWindow.Workbench.cs`
- Modify: `MainWindow.Workbench.TextTool.cs`
- Modify: `MainWindow.Story.cs`
- Modify: `MainWindow.Settings.cs`

- [ ] Add `WorkbenchReturnPage` values for root, project detail, story editor and project text tool.
- [ ] Mark each stable project page when it opens.
- [ ] Before leaving the project module, save the active row and chapter progress, stop the debounce timer, flush the save coordinator, then stop story audio.
- [ ] Restore the remembered page. For story restoration, validate the project and chapter, resolve the linked asset library again, preserve the loaded document and undo stack, and call `LoadStoryRowIntoUi()`.
- [ ] Keep `ShowAssetLibraryPage()` as the asset-library root entry.
- [ ] Run the new test and confirm it passes.

### Task 3: Verify and commit

**Files:**
- Test: `Scripts/Test-WorkbenchPageMemory.ps1`
- Test: existing story and source-health scripts

- [ ] Build Debug with `dotnet build .\GalExcleTools.csproj --no-restore --configuration Debug --runtime win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true` and require 0 warnings and 0 errors.
- [ ] Run the workbench memory, story editor reset, asset propagation, function picker, function cards, one-shot sound, voice indexing and source-health scripts.
- [ ] Parse source XAML as XML and run `git diff --check`.
- [ ] Commit the complete stage with its regression script.
