# Bilingual UI Upgrade Plan (Vietnamese Default, English Secondary)

## 1. Goal

Upgrade the frontend to fully support two languages:

- Vietnamese (`vi`) as the default language.
- English (`en`) as the secondary language.

The system must provide a complete, consistent, and professional Vietnamese translation set for all UI text, while preserving English for switching and fallback.

## 2. Scope

This plan covers:

- All visible UI text in pages, components, forms, tables, charts, tooltips, modals, notifications, and errors.
- Navigation labels, button text, placeholders, helper text, validation messages, status indicators, and empty/loading states.
- Date/time and number formatting per selected locale.
- Language switch behavior and persistence.

Out of scope for this phase:

- Back-end content translation (unless surfaced directly as user-facing text).
- PDF/report localization (can be planned in a follow-up phase).

## 3. Guiding Principles

- Single source of truth for all UI text via translation keys (no hard-coded strings in components).
- Vietnamese quality first: terminology must be natural, consistent, and domain-accurate for trading/risk contexts.
- Predictable key naming and maintainable structure.
- Safe fallback: if a Vietnamese key is missing, fall back to English and log it.
- Zero functional regressions in dashboard behavior.

## 4. Technical Strategy

### 4.1 i18n Library and Setup

- Use `react-i18next` + `i18next` (+ `i18next-browser-languagedetector` if needed).
- Initialize i18n in the app bootstrap layer.
- Set default language to `vi`.
- Keep `en` as fallback language.

### 4.2 Translation File Structure

Recommended structure:

```text
frontend/src/i18n/
	index.js
	locales/
		vi/
			common.json
			navigation.json
			overview.json
			trading.json
			safety.json
			events.json
			guidance.json
			errors.json
			validation.json
		en/
			common.json
			navigation.json
			overview.json
			trading.json
			safety.json
			events.json
			guidance.json
			errors.json
			validation.json
```

### 4.3 Key Naming Convention

- Use semantic, stable keys (not sentence-like keys).
- Pattern: `section.group.item` (example: `trading.signalCard.title`).
- Avoid duplicate meanings across keys.
- Reuse global keys from `common` where appropriate.

### 4.4 Language Switching UX

- Add a language switch control in the top-level layout/header.
- Default selection: Vietnamese.
- Persist user preference via `localStorage`.
- Apply language immediately without page reload.

## 5. UI Text Inventory and Migration

### Phase A: Inventory (Text Audit)

- Scan all frontend pages/components for hard-coded user-facing strings.
- Build a master inventory sheet with columns:
	- Key
	- English source
	- Vietnamese translation
	- Context/screen
	- Owner/reviewer

### Phase B: Externalize Strings

- Replace all hard-coded strings with `t('...')` calls.
- Group keys by page/domain to reduce maintenance overhead.
- Convert dynamic template strings to interpolation format.

### Phase C: Vietnamese Standardization

- Create a Vietnamese terminology glossary for trading domain terms.
- Normalize wording style:
	- Tone: professional, concise, and user-friendly.
	- Avoid mixed Vietnamese-English unless term is industry-standard.
	- Use consistent wording for statuses/actions across the app.

## 6. Vietnamese Translation Quality Standard

Define and enforce the following quality rules:

- Correct financial/trading terminology.
- Consistent verb style in CTA buttons.
- Consistent capitalization and punctuation.
- No machine-translation artifacts.
- Natural phrasing for alerts, errors, and guidance.

Recommended glossary examples (to refine with product owner):

- Signal -> Tin hieu
- Risk Guard -> Kiem soat rui ro
- Drawdown -> Muc giam von
- Position Size -> Quy mo vi the
- Cooldown -> Thoi gian cho
- Order Executed -> Lenh da thuc thi

## 7. Formatting and Localization Rules

- Vietnamese locale: `vi-VN`
- English locale: `en-US`
- Localize:
	- Date/time formatting
	- Number separators
	- Currency formatting (if shown)
	- Relative time labels (if applicable)

## 8. Validation and QA Plan

### 8.1 Functional QA

- Verify every page renders correctly in both `vi` and `en`.
- Verify no missing keys (`t` fallback keys displayed in UI).
- Verify language switch persistence after browser reload.

### 8.2 Visual QA

- Check text overflow/truncation in cards, badges, tables, and mobile layouts.
- Validate alignment and spacing impact due to longer Vietnamese strings.

### 8.3 Regression QA

- Ensure existing data polling, charts, and interaction logic remain unchanged.
- Verify all alert/notification paths still trigger expected text.

## 9. Delivery Plan and Milestones

### Milestone 1: Foundation

- Add i18n infrastructure.
- Add language switcher.
- Create initial translation namespaces.

### Milestone 2: Core Screens

- Localize Overview, Trading, Safety pages and shared components.
- Complete first Vietnamese translation pass.

### Milestone 3: Remaining Screens + Hardening

- Localize Events and Guidance pages.
- Translate all residual messages (errors/validation/tooltips).
- Execute QA checklist and fix visual/wording issues.

### Milestone 4: Release Readiness

- Complete terminology review.
- Freeze translation keys.
- Publish bilingual UI release notes.

## 10. Acceptance Criteria

- 100% user-facing UI strings are localized through i18n keys.
- Vietnamese is the default language for first-time users.
- English can be switched at runtime and persisted.
- No missing translation keys in production UI.
- Vietnamese translations pass terminology and readability review.

## 11. Risks and Mitigations

- Risk: Inconsistent terminology across components.
	- Mitigation: Shared glossary + single reviewer approval workflow.

- Risk: Late discovery of hard-coded strings.
	- Mitigation: Add lint/check script to detect literal UI strings in JSX.

- Risk: UI breakage from longer text.
	- Mitigation: Early visual QA on responsive breakpoints.

## 12. Implementation Checklist

- [ ] Add i18n dependencies and bootstrap configuration.
- [ ] Define locale file structure and namespaces.
- [ ] Add language switcher UI and persistence.
- [ ] Audit and externalize all hard-coded strings.
- [ ] Complete standard Vietnamese translation set.
- [ ] Validate formatting for `vi-VN` and `en-US`.
- [ ] Execute full QA and regression checks.
- [ ] Final terminology review and release.

