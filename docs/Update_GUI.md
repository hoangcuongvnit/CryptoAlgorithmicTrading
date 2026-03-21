# Frontend UI Improvement Plan

## 1) Current Issues

- The sidebar does not stretch to the full viewport height, making the layout look visually broken.
- The overall background is too light gray, and the contrast between content blocks and the page background is weak.
- The main content area lacks visual hierarchy/depth, so the dashboard feels flat.

## 2) Goals After the Update

- Ensure the sidebar is always full-height on desktop.
- Improve readability with a higher-contrast color system.
- Create clear visual layering between background, cards, and status elements (success/warning/error).
- Keep the UI clean and usable on both desktop and mobile.

## 3) Proposed UI Approach

## 3.1 Fix the Overall Layout (high priority)

- Use a stable two-column layout:
	- Desktop: fixed/sticky sidebar on the left, main content scrolls independently.
	- Mobile: sidebar becomes an off-canvas drawer.
- Enforce minimum page height:
	- Wrapper: `min-height: 100vh`.
	- Sidebar: `height: 100vh` (or `position: sticky; top: 0; height: 100vh`).
- Avoid making sidebar height depend on main content height.

Suggested CSS:

```css
.app-shell {
	display: grid;
	grid-template-columns: 260px 1fr;
	min-height: 100vh;
}

.sidebar {
	position: sticky;
	top: 0;
	height: 100vh;
	overflow-y: auto;
}

.main {
	min-width: 0;
	padding: 24px;
}

@media (max-width: 1024px) {
	.app-shell {
		grid-template-columns: 1fr;
	}
}
```

## 3.2 Improve Background Colors and Contrast (high priority)

- Avoid a single flat gray background across the whole page.
- Split colors into 3 visual layers:
	- `page background`: soft light tone with a subtle gradient.
	- `surface/card`: white or off-white.
	- `sidebar`: deep navy as a visual anchor.
- Define color tokens and reuse them consistently across the app.

Suggested palette (readable, not too aggressive):

```css
:root {
	--bg-page: #f3f7fb;
	--bg-page-alt: #e9f0f8;
	--bg-surface: #ffffff;

	--text-primary: #0f172a;
	--text-secondary: #475569;

	--sidebar-bg: #0b1730;
	--sidebar-border: #1d2b4a;
	--sidebar-text: #dbe7ff;
	--sidebar-active: #2f6fed;

	--success: #16a34a;
	--warning: #d97706;
	--danger: #dc2626;
	--info: #0284c7;

	--border-soft: #dbe4ef;
	--shadow-soft: 0 8px 30px rgba(15, 23, 42, 0.08);
}

body {
	background: linear-gradient(180deg, var(--bg-page) 0%, var(--bg-page-alt) 100%);
	color: var(--text-primary);
}

.card {
	background: var(--bg-surface);
	border: 1px solid var(--border-soft);
	box-shadow: var(--shadow-soft);
}
```

## 3.3 Strengthen Sidebar Visual Design

- Use a dark sidebar with light text and a clearly visible active state.
- Increase vertical spacing between menu groups.
- Add a subtle divider below the brand/header area.
- Pin the sidebar footer (refresh info) to the bottom for better balance.

Suggestion:

- Keep logo + app name in a fixed-height top block.
- Keep main navigation in the middle.
- Use `margin-top: auto` on the "Data refreshes..." block to push it to the bottom.

## 3.4 Improve Main Content Density and Structure

- Constrain content width with a max-width container for better readability.
- Apply consistent vertical rhythm between sections (`gap` system).
- Use equal/min card heights for stats to keep rows aligned.
- For empty `Recent Activity`, show a clearer placeholder container (icon + text + soft background).

## 3.5 Responsive Behavior

- >= 1280px: keep fixed sidebar, show 4-5 stats cards per row depending on width.
- 768px - 1279px: reduce paddings, use 2-3 columns for cards.
- < 768px: convert sidebar to drawer, stack cards in 1 column, prioritize key information first.

## 4) Step-by-Step Implementation Plan

1. Standardize color tokens in `src/index.css` (or a shared theme file).
2. Update layout shell in `src/App.jsx` so sidebar is always full-height.
3. Refactor sidebar styling (colors, active state, footer pinned to bottom).
4. Align card styles across pages: `Overview`, `Safety`, `Trading`, `Events`.
5. Verify responsive behavior at 3 core breakpoints: 1366x768, 1024x768, 390x844.
6. Final visual polish (padding, spacing, contrast) after real UI testing.

## 5) Acceptance Checklist

- Sidebar is full-height on desktop with no cut-off.
- Sidebar remains correct when main content is very short or very long.
- Text/background contrast is comfortably readable (especially secondary text).
- Cards in the same row are visually aligned in height.
- Mobile view has no horizontal overflow, smooth menu open/close, and no important content obstruction.

## 6) Optional Enhancements

- Add Light/Dim theme modes instead of full dark mode.
- Add subtle live-data animation (small pulse on status badge).
- Use consistent semantic status colors to reduce visual ambiguity in trading states.

