# Quick Actions Contextual Guidance Implementation Plan

Date: 2026-04-24

## Task 1

Add a contextual-action display model plus a ViewModel collection, navigation hook, and execution command.

## Task 2

Load the shared Operations snapshot in `QuickActionsViewModel` and synthesize up to four contextual recommendations from document readiness, intake backlog, and connector posture.

## Task 3

Render a contextual-actions section above the tab strip in `QuickActionsPage.xaml` and sync the selected tab when an in-page recommendation executes.

## Task 4

Add new focused `QuickActionsViewModelTests` coverage for recommendation synthesis and command dispatch, then run the available validation commands.
