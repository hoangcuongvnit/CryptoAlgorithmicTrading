# System Guidance UI Plan (For First-Time Users)

## 1. Purpose

Build a dedicated guidance page that explains:

- How the trading system works end-to-end
- What rules protect the system and users
- What is happening right now (live activities)

The page should help a first-time visitor understand the platform in less than 5 minutes and know where to look for details.

## 2. Target Users

- New operators who just opened the dashboard
- Non-technical stakeholders (product/ops)
- Team members who need a quick health and process overview

## 3. Page Goals

1. Explain the workflow clearly from market data to order execution.
2. Explain key safety/risk rules in simple language.
3. Show current live activity and status signals.
4. Provide confidence: "System is healthy" or "Action needed".
5. Let users drill down into details without overwhelming first-time users.

## 4. Information Architecture (Single Guidance Page)

### Section A: "How the System Works"

Display a simple step-by-step flow:

1. Data Ingestor receives market ticks.
2. Analyzer computes indicators/signals.
3. Strategy evaluates opportunities.
4. RiskGuard validates risk rules.
5. Executor places live order on Binance.
6. Notifier publishes updates and alerts.

UI pattern:

- Horizontal timeline on desktop
- Vertical timeline on mobile
- Each step has icon + one-line explanation + "What can go wrong?" tooltip

### Section B: "Rules & Safety"

Show top rules as cards with plain-language descriptions:

- Max drawdown
- Max position size
- Minimum risk/reward
- Cooldown between trades
- Live trading mode state

For each rule card include:

- Rule meaning (non-technical)
- Current configured value
- Current state (Pass / Warning / Block)
- Impact if violated

### Section C: "Live Activity Now"

A real-time feed that merges key events from services:

- Price update received
- Signal generated
- Risk check passed/failed
- Order requested/executed/rejected
- Notification sent

Include filters:

- By symbol (BTCUSDT, ETHUSDT, ...)
- By service (Analyzer, Strategy, RiskGuard, Executor)
- By severity (Info, Warning, Error)

### Section D: "Current System Health"

Simple health widgets:

- Services online/offline
- Last event timestamp per service
- Message delay (fresh/stale)
- gRPC/Rabbit/Redis connectivity status (based on available probes)

Use traffic-light colors with text labels:

- Green: Healthy
- Yellow: Degraded
- Red: Attention Required

### Section E: "What Should I Do Next?"

Guided actions for newcomers:

- "Read flow in 60 seconds"
- "Check risk rules before enabling live mode"
- "Open trading page"
- "Open event timeline"
- "Contact on-call if red status"

## 5. UX Content Rules (For First-Time Clarity)

- Use plain language, avoid jargon in headings.
- Put technical terms in tooltip or "More details" drawers.
- One key idea per card.
- Show examples (e.g., "Order blocked because drawdown exceeded 8%.").
- Keep text scannable: short paragraphs, bullets, clear labels.

## 6. Data Mapping to Existing System

Map existing backend events/data into guidance widgets:

- Redis Pub/Sub channels for market/signal events
- RiskGuard gRPC decisions for rule states
- Executor audit events for execution lifecycle
- Health endpoints from API services

Define a normalized frontend model:

- `activityItem`: timestamp, service, symbol, type, severity, message, metadata
- `ruleState`: ruleKey, configuredValue, evaluatedValue, status, reason
- `serviceHealth`: service, status, lastSeen, latencyMs

## 7. Suggested Frontend Structure

- New page: `GuidancePage`
- Components:
	- `SystemFlowTimeline`
	- `RuleStatusGrid`
	- `LiveActivityFeed`
	- `SystemHealthPanel`
	- `QuickStartChecklist`
- Hooks:
	- `useGuidanceData` (aggregate + normalize)
	- `useLiveActivityStream` (polling or websocket abstraction)

## 8. Implementation Phases

### Phase 1: Content & Wireframe

- Finalize plain-language copy for each section.
- Build low-fidelity wireframe for desktop/mobile.
- Validate with 1-2 non-technical users.

### Phase 2: UI Skeleton

- Create page route and static components.
- Implement responsive layout and card system.
- Add loading/empty/error states.

### Phase 3: Data Integration

- Connect to existing dashboard hooks/services.
- Normalize activity and rule data.
- Add filters and sorting in live feed.

### Phase 4: Health & Rules Logic

- Compute rule statuses consistently.
- Add service health aggregation logic.
- Add status color legend + severity mapping.

### Phase 5: First-Time Experience Polish

- Add onboarding checklist and helper text.
- Add tooltips + "learn more" links.
- Improve readability, spacing, and visual hierarchy.

### Phase 6: Validation

- Test mobile and desktop behavior.
- Test with live trading enabled.
- Run usability check with first-time users.
- Capture feedback and iterate.

## 9. Acceptance Criteria

1. A first-time user can explain the system flow after reading the page once.
2. Users can identify at least 3 core risk rules and current states.
3. Live activity updates are visible and filterable.
4. Health status is understandable without technical knowledge.
5. Page is responsive and readable on mobile.

## 10. Risks and Mitigations

- Risk: Too much information on first screen.
	- Mitigation: progressive disclosure (expand/details drawer).
- Risk: Inconsistent status logic across services.
	- Mitigation: central status mapping in one normalization layer.
- Risk: Real-time feed noise.
	- Mitigation: default filters + severity grouping + pause feed option.

## 11. Delivery Checklist

- [ ] Final UX copy approved
- [ ] Guidance page route created
- [ ] Components implemented
- [ ] Live data integrated
- [ ] Health/rule indicators validated
- [ ] Mobile responsive verified
- [ ] Basic user walkthrough documented
