# Chapter Choice Manager Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a chapter-card context-menu popup that displays and edits every section, Choice, target, inferred common route, and custom AllRoad marker in one staged-save workflow.

**Architecture:** Extract chapter flow calculation from `UnrealSyncService` into a pure `StoryBranchFlowService`, then make both Unreal mapping and the popup consume the same section-level result. The popup edits a cloned `StoryDocument`; cancel discards it and save atomically replaces the chapter document after validation.

**Tech Stack:** C# 12, .NET 8, WinUI 3, PowerShell regression scripts, existing StoryDocument repository and custom overlay conventions.

---

### Task 1: Shared chapter flow model

**Files:**
- Create: `Models/StoryBranchFlowModels.cs`
- Create: `Services/StoryBranchFlowService.cs`
- Modify: `Services/UnrealSyncService.cs`
- Test: `Scripts/Test-StoryBranchFlow.ps1`

- [ ] **Step 1: Write the failing flow test**

Create a five-section document where section 1 has explicit A/C targets, B/D enter the inferred common section, section 4 is custom, and section 5 is terminal. Assert the service returns choice, branch, common, custom, and terminal states plus the same target sections used by Unreal mapping.

- [ ] **Step 2: Run test to verify RED**

Run:

```powershell
& .\Scripts\Test-StoryBranchFlow.ps1
```

Expected: failure because `StoryBranchFlowService` does not exist.

- [ ] **Step 3: Implement the pure flow service**

Define immutable flow records containing source section, section state, choice function, option key/text, explicit target, effective target, inferred-common flag, first-sentence preview, and validation issues. Move convergence and unbound-choice handling from `BuildChapterStoryBranchSyncEntries` into this service.

- [ ] **Step 4: Adapt Unreal mapping**

Map the shared flow's effective target section to `UnrealStoryTableSyncEntry.TableAsset`; omit custom sections and emit an empty route list for terminal sections. Keep strict Unreal-side route-count verification.

- [ ] **Step 5: Verify GREEN and commit**

Run the new flow test plus `Test-UnrealStoryBranches.ps1`, then commit shared flow extraction.

### Task 2: Staged chapter editing service

**Files:**
- Create: `Models/StoryChoiceManagerModels.cs`
- Create: `Services/StoryChoiceManagerService.cs`
- Test: `Scripts/Test-StoryChoiceManager.ps1`

- [ ] **Step 1: Write failing draft/save tests**

Assert loading creates an independent clone, editing option text/target/custom status changes only the draft, cancel leaves the source file hash unchanged, and save persists all changes in one repository write.

- [ ] **Step 2: Run test to verify RED**

Run `Scripts\Test-StoryChoiceManager.ps1`; expect missing manager service.

- [ ] **Step 3: Implement draft operations**

Expose section summaries, choice groups, option edits, explicit target changes, custom marker changes, validation, and atomic save through one focused service. Reuse `StoryBranchService.ReplaceRoutesForChoice` and `StoryDocumentRepository`.

- [ ] **Step 4: Verify GREEN and commit**

Run manager, flow, JSON, and branch-route tests; commit staged editing support.

### Task 3: Chapter-card popup

**Files:**
- Create: `MainWindow.Workbench.ChoiceManager.cs`
- Modify: `MainWindow.Workbench.Chapters.cs`
- Modify: `MainWindow.xaml`
- Test: `Scripts/Test-ChapterChoiceManagerOverlay.ps1`

- [ ] **Step 1: Write failing source/UI checks**

Require an `选项与分支` chapter-card menu item, a modal overlay, three columns, search/filterable section list, grouped Choice list, detail editor, generated AllRoad preview, validation area, and save/cancel actions.

- [ ] **Step 2: Run test to verify RED**

Run `Scripts\Test-ChapterChoiceManagerOverlay.ps1`; expect missing overlay and menu entry.

- [ ] **Step 3: Implement the overlay**

Use a centered large modal over the workbench. Left rows show section number, first sentence, and state badges; middle rows group Choice functions and options; right edits option text/target and current-section custom logic while displaying explicit and effective routes.

- [ ] **Step 4: Implement interaction contract**

Open from chapter-card right click. Keep all changes in the draft. Enter saves when focus is not in a multiline editor; Esc, right click outside, close, and cancel discard. Refresh chapter cards after save and show one unified success/error tip.

- [ ] **Step 5: Verify GREEN and commit**

Run overlay, source-health, flow, and manager tests; compile Debug x64 with zero warnings/errors; commit the popup.

### Task 4: Final real-chapter verification

**Files:**
- Modify only if verification finds a defect in the files above.

- [ ] **Step 1: Verify M2 read model**

Load the real `WHK-M2-00` StoryDocument read-only. Assert four Choice groups are shown with chapter-wide numbering and M2-07 displays A -> section 9, B -> common section 10, section 9 -> section 10.

- [ ] **Step 2: Verify custom and terminal states**

Assert a custom section is excluded from generated routes and the final section remains terminal.

- [ ] **Step 3: Run complete checks**

Run all new scripts, related story tests, `Test-SourceHealth.ps1`, `git diff --check`, and Debug x64 build.

- [ ] **Step 4: Commit final fixes**

Commit only if the real-data verification required changes; otherwise leave the prior task commits as the final history.
